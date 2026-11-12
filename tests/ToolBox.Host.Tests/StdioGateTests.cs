using System.Text;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>#63: the gate over stdio, through the unmodified production Host as a child process.</summary>
public sealed class StdioGateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-stdio-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Stdio_HidesAndRejectsToolsOfAnOffToolset()
    {
        var hostDll = Path.Combine(AppContext.BaseDirectory, "ToolBox.Host.dll");
        Assert.True(File.Exists(hostDll), $"expected {hostDll} beside the test output");

        var stderr = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "toolbox-stdio-test",
            Command = "dotnet",
            Arguments = [hostDll],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["TOOLBOX_TOOLSETS"] = "basics",
                ["TOOLBOX_Desired__Path"] = Path.Combine(_dir, "desired-state.json"),
                ["TOOLBOX_Ports__Viewer__Port"] = "0",
            },
            StandardErrorLines = line => { lock (stderr) { stderr.AppendLine(line); } },
        });

        try
        {
            // The initialize handshake is the readiness signal; no sleeps.
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            var names = tools.Select(t => t.Name).ToArray();
            // Positive control, so "hidden" below is not vacuous.
            Assert.Contains("ping", names);
            Assert.Contains("server_info", names);
            Assert.Contains("current_time", names);
            Assert.DoesNotContain("place_block", names);

            // Both gate filters read ctx.Services; if it were null over stdio, list and call would both fail.
            var result = await client.CallToolAsync("place_block",
                new Dictionary<string, object?> { ["x"] = 0, ["y"] = 0, ["z"] = 0 }, cancellationToken: cts.Token);
            Assert.True(result.IsError);
            var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
            Assert.Equal(ToolsetGate.OffMessage("voxel"), text);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            string log;
            lock (stderr) { log = stderr.ToString(); }
            throw new InvalidOperationException($"stdio test failed; child stderr:\n{log}", ex);
        }
    }
}
