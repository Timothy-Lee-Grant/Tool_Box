using System.Text;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ToolBox.Host.Tests;

/// <summary>
/// #34 step 5b: end to end through the unmodified production Host as a child process over real stdio.
/// RequestReconcile() is not reachable here, so the real file watcher and debounce drive the reconcile.
/// This is the test that proves Program.RunStdioAsync registers the real notifier: 5a alone would pass
/// even if that registration were forgotten.
/// </summary>
public sealed class ToolListChangedChildProcessTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-listchanged-child-").FullName;
    private string FilePath => Path.Combine(_dir, "desired-state.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string VoxelFile(bool on) =>
        """{"schemaVersion":1,"toolsets":{"voxel":{"enabled":""" + (on ? "true" : "false") + "}}}";

    [Fact]
    public async Task EditingTheDesiredStateFile_MakesTheProductionHostPushListChanged()
    {
        var hostDll = Path.Combine(AppContext.BaseDirectory, "ToolBox.Host.dll");
        Assert.True(File.Exists(hostDll), $"expected {hostDll} beside the test output");

        File.WriteAllText(FilePath, VoxelFile(on: false));

        var stderr = new StringBuilder();
        // Deliberate exception to the fake-clock rule, like LiveReconcile_FileEditChangesToolsList: a real
        // file watcher, debounce and process. The wait exits at the first notification; 60 s only bounds failure.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "toolbox-listchanged-test",
            Command = "dotnet",
            Arguments = [hostDll],
            // No TOOLBOX_TOOLSETS: that env var pins the set and would ignore the file.
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["TOOLBOX_Desired__Path"] = FilePath,
                ["TOOLBOX_Ports__Viewer__Port"] = "0",
            },
            StandardErrorLines = line => { lock (stderr) { stderr.AppendLine(line); } },
        });

        try
        {
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

            var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (_, _) =>
            {
                notified.TrySetResult();
                return ValueTask.CompletedTask;
            });

            var before = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToArray();
            Assert.Contains("ping", before); // positive control: the Host is up and basics is on
            Assert.DoesNotContain("place_block", before);

            // Atomic-style save: write a temp file, then rename over the target.
            var tmp = Path.Combine(_dir, ".tmp-desired");
            File.WriteAllText(tmp, VoxelFile(on: true));
            File.Move(tmp, FilePath, overwrite: true);

            await notified.Task.WaitAsync(cts.Token);

            var after = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToArray();
            Assert.Contains("place_block", after);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            string log;
            lock (stderr) { log = stderr.ToString(); }
            throw new InvalidOperationException($"child-process list_changed test failed; child stderr:\n{log}", ex);
        }
    }
}
