using System.Diagnostics;
using System.Text;

namespace ToolBox.E2E.Tests;

/// <summary>
/// The real container, driven only through the <c>docker compose</c> CLI: project <c>toolbox-e2e</c>, the repo's
/// compose file plus <c>docker-compose.e2e.yml</c> (random published ports), <c>up --wait</c> on the image's own
/// healthcheck, and <c>down -v</c> before and after so a crashed earlier run cannot leak into this one.
/// </summary>
public sealed class ComposeStack : IAsyncDisposable
{
    private const string Project = "toolbox-e2e";
    private readonly string _root;
    private readonly string _overridePath;

    public string McpBaseUrl { get; private set; } = "";
    public string ViewerBaseUrl { get; private set; } = "";

    private ComposeStack(string root)
    {
        _root = root;
        _overridePath = Path.Combine(root, "tests", "ToolBox.E2E.Tests", "docker-compose.e2e.yml");
    }

    public static async Task<ComposeStack> StartAsync()
    {
        var stack = new ComposeStack(FindRepoRoot());
        try
        {
            await stack.RunAsync(TimeSpan.FromMinutes(2), "down", "-v", "--remove-orphans");
            await stack.RunAsync(TimeSpan.FromMinutes(10), "up", "-d", "--build", "--wait", "--wait-timeout", "120");
            stack.McpBaseUrl = await stack.PublishedAsync(8080);
            stack.ViewerBaseUrl = await stack.PublishedAsync(8090);
            return stack;
        }
        catch
        {
            string logs = await stack.TryAsync("logs", "--no-color", "--tail", "200");
            await stack.DisposeAsync();
            throw new InvalidOperationException("compose stack failed to start. Container logs:\n" + logs);
        }
    }

    /// <summary>Reads the random host port Docker chose for a container port (output like <c>0.0.0.0:49153</c>).</summary>
    private async Task<string> PublishedAsync(int containerPort)
    {
        string line = (await RunAsync(TimeSpan.FromSeconds(30), "port", "toolbox", containerPort.ToString()))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        int port = int.Parse(line[(line.LastIndexOf(':') + 1)..]);
        return $"http://localhost:{port}";
    }

    public async ValueTask DisposeAsync() => await TryAsync("down", "-v", "--remove-orphans");

    private async Task<string> TryAsync(params string[] args)
    {
        try { return await RunAsync(TimeSpan.FromMinutes(2), args); }
        catch (Exception e) { return e.Message; }
    }

    private async Task<string> RunAsync(TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in new[] { "compose", "-p", Project, "-f", "docker-compose.yml", "-f", _overridePath }.Concat(args))
            psi.ArgumentList.Add(a);

        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("could not start docker");
        using var cts = new CancellationTokenSource(timeout);
        Task<string> stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> stderr = p.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            string o = await stdout, e = await stderr;
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"docker compose {string.Join(' ', args)} exited {p.ExitCode}\n{o}\n{e}");
            return o + e;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"docker compose {string.Join(' ', args)} timed out after {timeout}");
        }
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "ToolBox.slnx"))) return d.FullName;
        throw new FileNotFoundException("Could not find the repo root (ToolBox.slnx) above " + AppContext.BaseDirectory);
    }

    /// <summary><c>&lt;Version&gt;</c> from Directory.Build.props, the single source of the shipped version.</summary>
    public static string ExpectedVersion()
    {
        string props = File.ReadAllText(Path.Combine(FindRepoRoot(), "Directory.Build.props"));
        var m = System.Text.RegularExpressions.Regex.Match(props, @"<Version>\s*([^<\s]+)\s*</Version>");
        return m.Success ? m.Groups[1].Value : throw new InvalidOperationException("no <Version> in Directory.Build.props");
    }
}
