using System.IO.Pipelines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ToolBox.Host.Tests;

/// <summary>
/// #34 step 5a: the stdio notification path, in-process. A real SDK server (the production composition,
/// <c>AddToolBoxServer</c> plus the real <see cref="StdioToolListChangedNotifier"/>) talks to a real client
/// over a pipe pair, so <c>RequestReconcile()</c> is reachable. No sleeps: we await a semaphore released by
/// the client's notification handler, and prove "nothing was sent" with a sentinel (a real second change).
/// Step 5b (child process) proves Program.cs registers the notifier; this proves the logic.
/// </summary>
public sealed class ToolListChangedStdioTests : IAsyncLifetime
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-listchanged-").FullName;
    private string FilePath => Path.Combine(_dir, "desired-state.json");

    private IHost _host = null!;
    private McpClient _client = null!;
    private readonly SemaphoreSlim _notifications = new(0);
    private int _count;

    private static string VoxelFile(bool on) =>
        """{"schemaVersion":1,"toolsets":{"voxel":{"enabled":""" + (on ? "true" : "false") + "}}}";

    public async Task InitializeAsync()
    {
        File.WriteAllText(FilePath, VoxelFile(on: false));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Desired:Path"] = FilePath,
            ["Ports:Viewer:Port"] = "0",
        });
        builder.Services
            .AddToolBoxServer(builder.Configuration)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        // The same replacement Program.RunStdioAsync makes.
        builder.Services.Replace(ServiceDescriptor.Singleton<IToolListChangedNotifier, StdioToolListChangedNotifier>());

        _host = builder.Build();
        await _host.StartAsync();

        _client = await McpClient.CreateAsync(new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        _client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (_, _) =>
        {
            Interlocked.Increment(ref _count);
            _notifications.Release();
            return ValueTask.CompletedTask;
        });
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task AwaitNotificationAsync()
    {
        Assert.True(await _notifications.WaitAsync(Bound), "no tools/list_changed within the bound");
    }

    private async Task<string[]> ToolNamesAsync() =>
        (await _client.ListToolsAsync()).Select(t => t.Name).ToArray();

    [Fact]
    public async Task EnablingVoxel_PushesListChanged_ThenToolsListShowsPlaceBlock()
    {
        Assert.DoesNotContain("place_block", await ToolNamesAsync());

        File.WriteAllText(FilePath, VoxelFile(on: true));
        _host.Services.GetRequiredService<IToolsetReconciler>().RequestReconcile();

        await AwaitNotificationAsync();
        // Only after the push: the gate was opened before the notification, so the list is already fresh.
        Assert.Contains("place_block", await ToolNamesAsync());
        Assert.Equal(1, Volatile.Read(ref _count));
    }

    [Fact]
    public async Task AReconcileWithNoChange_SendsNothing_ARealChangeSendsExactlyOneMore()
    {
        var reconciler = _host.Services.GetRequiredService<IToolsetReconciler>();

        reconciler.RequestReconcile(); // nothing changed: must not notify
        File.WriteAllText(FilePath, VoxelFile(on: true));
        reconciler.RequestReconcile();
        await AwaitNotificationAsync();
        Assert.Contains("place_block", await ToolNamesAsync());

        reconciler.RequestReconcile(); // still nothing changed
        File.WriteAllText(FilePath, VoxelFile(on: false));
        reconciler.RequestReconcile(); // sentinel: a real change
        await AwaitNotificationAsync();
        Assert.DoesNotContain("place_block", await ToolNamesAsync());

        // Notifications arrive in order, so both quiet passes would have shown up before the sentinels.
        Assert.Equal(2, Volatile.Read(ref _count));
    }
}
