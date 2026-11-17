using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core.Health;
using ToolBox.Core.Instance;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>Issue #38: background run mode: exit codes through the real Host, host.json lifecycle, graceful /shutdown.</summary>
[Collection(VoxelViewerCollection.Name)]
public sealed class BackgroundModeTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-bg-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private async Task<(int Exit, string Stderr)> RunHostAsync(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "ToolBox.Host.dll");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.Environment["TOOLBOX_Instance__Dir"] = _dir;
        psi.Environment["TOOLBOX_Admin__TokenPath"] = Path.Combine(_dir, "admin-token");
        psi.Environment["TOOLBOX_Ports__Admin__Port"] = "0";
        psi.Environment["TOOLBOX_Ports__Mcp__Port"] = "0";
        psi.Environment["TOOLBOX_Ports__Viewer__Port"] = "0";
        using Process p = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        string err = await p.StandardError.ReadToEndAsync(cts.Token);
        await p.WaitForExitAsync(cts.Token);
        return (p.ExitCode, err);
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["Mode"] = "background",
        ["Instance:Dir"] = _dir,
        ["Ports:Viewer:Port"] = "0",
        ["Ports:Admin:Port"] = "0",
        ["Admin:TokenPath"] = Path.Combine(_dir, "admin-token"),
    };

    [Fact]
    public async Task Background_with_explicit_stdio_exits_2()
    {
        var (exit, err) = await RunHostAsync("--mode", "background", "--transport", "stdio");
        Assert.Equal(2, exit);
        Assert.Contains("cannot be combined", err);
    }

    [Fact]
    public async Task Held_lock_with_no_host_json_exits_4_and_says_so()
    {
        using InstanceLock? held = InstanceLock.TryAcquire(_dir);
        Assert.NotNull(held);
        var (exit, err) = await RunHostAsync("--mode", "background");
        Assert.Equal(4, exit);
        Assert.Contains("already running", err);
        Assert.Contains(HostInfoFile.FileName, err);
    }

    [Fact]
    public async Task Held_lock_with_stale_host_json_reports_pid_and_that_it_did_not_answer()
    {
        using InstanceLock? held = InstanceLock.TryAcquire(_dir);
        HostInfoFile.Write(_dir, new HostInfo(4242, DateTimeOffset.UtcNow, "x", "http://127.0.0.1:1/mcp", "http://127.0.0.1:2"));
        var (exit, err) = await RunHostAsync("--mode", "background");
        Assert.Equal(4, exit);
        Assert.Contains("pid 4242", err);
        Assert.Contains("did not answer", err);
    }

    [Fact]
    public async Task Host_json_is_written_after_bind_answers_health_and_is_removed_on_stop()
    {
        WebApplication app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: Settings());
        await app.StartAsync();
        HostInfo? info = HostInfoFile.TryRead(_dir);
        Assert.NotNull(info);
        Assert.Equal(Environment.ProcessId, info.Pid);
        Assert.DoesNotContain(":0/", info.McpUrl); // the real port, not the requested 0
        Assert.True(await HostProbe.IsRunningAsync(info));
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(_dir, "admin-token")).Trim(), File.ReadAllText(HostInfoFile.PathIn(_dir)));

        await app.StopAsync();
        await app.DisposeAsync();
        Assert.Null(HostInfoFile.TryRead(_dir));
    }

    [Fact]
    public async Task Plain_http_mode_writes_no_host_json()
    {
        var settings = Settings();
        settings.Remove("Mode");
        WebApplication app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
        await app.StartAsync();
        Assert.False(File.Exists(HostInfoFile.PathIn(_dir)));
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> PostShutdownAsync(WebApplication app, string? token)
    {
        string admin = app.Urls.Last().TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, admin + "/api/v1/shutdown");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(request);
    }

    [Fact]
    public async Task Shutdown_needs_the_token_and_is_not_on_the_mcp_port()
    {
        WebApplication app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: Settings());
        await app.StartAsync();
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostShutdownAsync(app, null)).StatusCode);

            var onMcp = await Http.PostAsync(app.Urls.First().TrimEnd('/') + "/api/v1/shutdown", null);
            Assert.NotEqual(HttpStatusCode.Accepted, onMcp.StatusCode);
            Assert.False(app.Lifetime.ApplicationStopping.IsCancellationRequested);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    [Fact]
    public async Task Shutdown_runs_the_real_stop_path_before_the_host_exits()
    {
        // Voxel is the real toolset with a real lifecycle (viewer). Pinned on, it must be "on" while
        // running and "off" after /shutdown, i.e. its StopAsync ran before RunAsync returned.
        var settings = Settings();
        settings["TOOLSETS"] = "voxel";
        settings["Desired:Path"] = Path.Combine(_dir, "desired-state.json");
        WebApplication app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
        var board = app.Services.GetRequiredService<ToolsetStatusBoard>();
        Task run = app.RunAsync();
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (board.Get("voxel").Status != ToolsetStatus.On)
            {
                Assert.True(DateTime.UtcNow < deadline, "voxel never started");
                await Task.Delay(50);
            }

            string token = File.ReadAllText(Path.Combine(_dir, "admin-token")).Trim();
            var response = await PostShutdownAsync(app, token);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(ToolsetStatus.Off, board.Get("voxel").Status);
            Assert.Null(HostInfoFile.TryRead(_dir));
        }
        finally { await app.DisposeAsync(); }
    }
}
