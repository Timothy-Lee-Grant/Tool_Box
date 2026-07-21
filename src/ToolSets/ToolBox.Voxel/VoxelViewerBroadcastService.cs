using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ToolBox.Voxel;

/// <summary>
/// Broadcasts <see cref="VoxelWorld"/> changes to any connected viewer over a raw
/// loopback WebSocket — independent of whichever MCP transport the Host itself is
/// running. This is the "companion infrastructure" pattern from
/// Documentation/ImplementationPlans/003 §2.7/§4: a toolset can bring more than tools,
/// and a <see cref="BackgroundService"/> works the same under stdio or HTTP because
/// both Program.cs paths build on the same <c>Microsoft.Extensions.Hosting</c> host.
///
/// The agent never talks to this — it only ever talks to <see cref="VoxelTools"/>.
/// This class exists purely for human eyes watching a browser tab.
/// </summary>
public sealed class VoxelViewerBroadcastService(VoxelWorld world, ILogger<VoxelViewerBroadcastService> logger)
    : BackgroundService
{
    // Deliberately outside the MCP HTTP transport's own port (8080, ToolBoxHttpApp.cs) —
    // under --transport http both would run in the same process. Fallback list mirrors
    // the reference implementation this is modeled on (Documentation/ImplementationPlans/003 §2.7).
    private static readonly int[] CandidatePorts = [8090, 8091, 8092, 8093];

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly List<WebSocket> _sockets = [];

    private HttpListener? _listener;

    /// <summary>The port actually bound, once <see cref="ExecuteAsync"/> has started —
    /// null before startup, or if every candidate port was taken.</summary>
    public int? Port { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = StartListener();
        if (_listener is null)
        {
            logger.LogWarning(
                "No free port for the voxel viewer among {Ports} — MCP tools still work, viewer disabled.",
                string.Join(", ", CandidatePorts));
            return;
        }

        // HttpListener.GetContextAsync() has no CancellationToken overload; Stop()-ing
        // the listener from a shutdown callback is what actually unblocks it.
        using CancellationTokenRegistration registration = stoppingToken.Register(() => _listener.Stop());

        world.Changed += OnWorldChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break; // Expected: the registration above stopped the listener.
                }

                if (context.Request.IsWebSocketRequest)
                {
                    _ = AcceptSocketAsync(context, stoppingToken);
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                }
            }
        }
        finally
        {
            world.Changed -= OnWorldChanged;
            CloseAllSockets();
            _listener.Close();
        }
    }

    private HttpListener? StartListener()
    {
        foreach (int port in CandidatePorts)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/voxel/");
            try
            {
                listener.Start();
                Port = port;
                logger.LogInformation("Voxel viewer listening on ws://127.0.0.1:{Port}/voxel/", port);
                return listener;
            }
            catch (HttpListenerException)
            {
                // Port taken — try the next candidate.
            }
        }

        return null;
    }

    private async Task AcceptSocketAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        WebSocket socket;
        try
        {
            HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            socket = wsContext.WebSocket;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Voxel viewer WebSocket upgrade failed.");
            return;
        }

        lock (_lock)
        {
            _sockets.Add(socket);
        }

        // A newly-connected (or refreshed) viewer has no history — it needs the full
        // current state once, then diffs from here on.
        if (!await TrySendAsync(socket, BuildSnapshotMessage(), stoppingToken))
        {
            RemoveSocket(socket);
            return;
        }

        await WaitForCloseAsync(socket, stoppingToken);
        RemoveSocket(socket);
    }

    /// <summary>We never read anything meaningful from the viewer — it's receive-only —
    /// but we still have to keep reading *something* or we'd never notice it closed,
    /// and <see cref="_sockets"/> would grow forever.</summary>
    private async Task WaitForCloseAsync(WebSocket socket, CancellationToken stoppingToken)
    {
        var buffer = new byte[1];
        try
        {
            while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, stoppingToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Complete the close handshake — without this, the client sees
                    // "closed without completing the close handshake" even though
                    // both sides intended a clean shutdown.
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Any receive failure means the connection is gone either way — RemoveSocket follows.
        }
    }

    private void OnWorldChanged(VoxelChange change) => _ = BroadcastAsync(BuildChangeMessage(change));

    private async Task BroadcastAsync(string message)
    {
        List<WebSocket> targets;
        lock (_lock)
        {
            targets = [.. _sockets];
        }

        foreach (WebSocket socket in targets)
        {
            if (!await TrySendAsync(socket, message, CancellationToken.None))
            {
                RemoveSocket(socket);
            }
        }
    }

    private async Task<bool> TrySendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return false;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Dropping a voxel viewer socket that failed to receive a broadcast.");
            return false;
        }
    }

    private void RemoveSocket(WebSocket socket)
    {
        lock (_lock)
        {
            _sockets.Remove(socket);
        }

        socket.Dispose();
    }

    private void CloseAllSockets()
    {
        List<WebSocket> sockets;
        lock (_lock)
        {
            sockets = [.. _sockets];
            _sockets.Clear();
        }

        foreach (WebSocket socket in sockets)
        {
            socket.Dispose();
        }
    }

    private string BuildSnapshotMessage()
    {
        var blocks = world.Snapshot().Select(b => new BlockWire(b.Coordinate.X, b.Coordinate.Y, b.Coordinate.Z, b.Material));
        return JsonSerializer.Serialize(new SnapshotMessage("snapshot", [.. blocks]), WireOptions);
    }

    private static string BuildChangeMessage(VoxelChange change) => change switch
    {
        VoxelChange.Placed placed => JsonSerializer.Serialize(
            new SnapshotMessage("batch", [.. placed.Blocks.Select(b => new BlockWire(b.Coordinate.X, b.Coordinate.Y, b.Coordinate.Z, b.Material))]),
            WireOptions),

        VoxelChange.Removed removed => JsonSerializer.Serialize(
            new RemoveMessage("remove", [.. removed.Coordinates.Select(c => new CoordinateWire(c.X, c.Y, c.Z))]),
            WireOptions),

        VoxelChange.Cleared => JsonSerializer.Serialize(new ClearMessage("clear"), WireOptions),

        _ => throw new NotSupportedException($"Unrecognized {nameof(VoxelChange)} type: {change.GetType()}"),
    };

    private sealed record BlockWire(int X, int Y, int Z, string Material);

    private sealed record CoordinateWire(int X, int Y, int Z);

    private sealed record SnapshotMessage(string Type, IReadOnlyList<BlockWire> Blocks);

    private sealed record RemoveMessage(string Type, IReadOnlyList<CoordinateWire> Blocks);

    private sealed record ClearMessage(string Type);
}
