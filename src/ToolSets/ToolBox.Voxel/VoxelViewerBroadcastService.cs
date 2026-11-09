using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ToolBox.Core.Health;
using ToolBox.Core.Ports;

namespace ToolBox.Voxel;

/// <summary>
/// Broadcasts <see cref="VoxelWorld"/> changes to any connected viewer over a raw
/// loopback WebSocket — independent of whichever MCP transport the Host itself is
/// running. This is the "companion infrastructure" pattern from
/// Documentation/ImplementationPlans/003 §2.7/§4: a toolset can bring more than tools,
/// and it works the same under stdio or HTTP.
///
/// Since #32 step 4 this is NOT a container-started hosted service: it is a restartable
/// component driven by <see cref="VoxelLifecycle"/> (Start binds the port, Stop releases it),
/// so the reconciler can turn Voxel off and on without restarting the process.
/// Stopping never touches <see cref="VoxelWorld"/>, so the build survives off→on.
///
/// The agent never talks to this — it only ever talks to <see cref="VoxelTools"/>.
/// This class exists purely for human eyes watching a browser tab.
/// </summary>
public sealed class VoxelViewerBroadcastService(
    VoxelWorld world,
    IConfiguration configuration,
    ComponentStatusRegistry status,
    ILogger<VoxelViewerBroadcastService> logger)
{
    /// <summary>Config keys written by the Host's port table (issue #37). Voxel only READS
    /// them, so the defaults live in one place (the Host) and are never duplicated here.</summary>
    public const string PortKey = "Ports:Viewer:Port";
    public const string AddressKey = "Ports:Viewer:Address";

    /// <summary>The name this component reports under in the status registry (and so in /health).</summary>
    public const string ComponentName = "viewer";

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly List<WebSocket> _sockets = [];

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>The port actually bound, once <see cref="StartAsync"/> has run — null when
    /// stopped, or if the viewer could not start (see the component status).</summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Binds the port and starts serving. The bind is synchronous, so the component status is
    /// set when this returns. A bind failure degrades visibly and does NOT throw (ADR-016: the
    /// viewer is optional, the toolset stays On). Calling it while running is a no-op.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            return Task.CompletedTask;
        }

        HttpListener? listener = StartListener();
        if (listener is null)
        {
            return Task.CompletedTask; // Degraded, not dead: StartListener recorded why.
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _loop = Task.Run(() => RunAsync(listener, token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Stops serving, closes viewer sockets and releases the port. Safe when never
    /// started or already stopped. Leaves the world untouched.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts = _cts;
        Task? loop = _loop;
        _cts = null;
        _loop = null;
        Port = null;

        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync();
                await (loop ?? Task.CompletedTask).WaitAsync(cancellationToken);
            }
            finally
            {
                cts.Dispose();
            }
        }

        // A stopped (or never-bound) viewer stops reporting: no stale "ok" / "unavailable".
        status.Remove(ComponentName);
    }

    private async Task RunAsync(HttpListener listener, CancellationToken stoppingToken)
    {
        // HttpListener.GetContextAsync() has no CancellationToken overload; Stop()-ing
        // the listener from a cancellation callback is what actually unblocks it.
        using CancellationTokenRegistration registration = stoppingToken.Register(() => listener.Stop());

        world.Changed += OnWorldChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break; // Expected: the registration above stopped the listener.
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Voxel viewer listener failed; the viewer has stopped.");
                    break;
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
            listener.Close();
        }
    }

    /// <summary>
    /// Binds the ONE configured port (no fallback: the browser page and Docker both assume
    /// it). The viewer is optional (R2), so any failure records a visible status and
    /// returns null instead of throwing; the MCP server keeps running.
    /// </summary>
    private HttpListener? StartListener()
    {
        if (!int.TryParse(configuration[PortKey], out int port) || port is < 0 or > 65535
            || !IPAddress.TryParse(configuration[AddressKey], out IPAddress? address))
        {
            return Degrade($"unavailable: {PortKey}/{AddressKey} not configured", null);
        }

        try
        {
            // HttpListener cannot bind port 0 itself, so for "OS picks" (tests) find a free
            // port first. Small race, but it only affects port-0 callers.
            if (port == 0)
            {
                port = PortProbe.FindFreePort(address);
            }

            // Pre-flight probe on the SAME address as the real bind (D2) ...
            PortProbe.Check(address, port, "viewer", PortKey);
        }
        catch (PortConflictException ex)
        {
            return Degrade($"unavailable: port {port} in use", ex);
        }

        var listener = new HttpListener();
        foreach (string prefix in BuildPrefixes(address, port))
        {
            listener.Prefixes.Add(prefix);
        }

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            // ... and the real bind's failure is the backstop if we lost the race (D1).
            return PortConflictException.IsAddressInUse(ex)
                ? Degrade($"unavailable: port {port} in use", ex)
                : Degrade($"unavailable: {ex.Message}", ex);
        }

        Port = port;
        status.Set(ComponentName, "ok");
        logger.LogInformation("Voxel viewer listening on {Address}:{Port} (path /voxel/)", address, port);
        return listener;
    }

    private HttpListener? Degrade(string statusText, Exception? ex)
    {
        status.Set(ComponentName, statusText);
        logger.LogWarning(ex, "Voxel viewer disabled ({Status}); MCP tools still work.", statusText);
        return null;
    }

    /// <summary>
    /// HttpListener prefixes for a bind address. 0.0.0.0 maps to the strong wildcard "+"
    /// (needed behind Docker's port publishing, ADR-012). A loopback address gets TWO
    /// prefixes, the literal IP and "localhost", because HttpListener matches the
    /// request's Host header and viewer/index.html connects to ws://localhost:8090.
    /// </summary>
    public static IReadOnlyList<string> BuildPrefixes(IPAddress address, int port)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return [$"http://+:{port}/voxel/"];
        }

        string host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]" : address.ToString();
        var prefixes = new List<string> { $"http://{host}:{port}/voxel/" };
        if (IPAddress.IsLoopback(address))
        {
            prefixes.Add($"http://localhost:{port}/voxel/");
        }

        return prefixes;
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
