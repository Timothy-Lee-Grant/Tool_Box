using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ToolBox.E2E.Tests;

/// <summary>
/// Launches the built Host as a real child process in background mode, the way the GUI will, and talks to it only
/// through process, files and HTTP. Everything lives in a temp dir and every port is 0, so it cannot collide with a
/// running dev host. Disposal kills the process tree, so a failed assertion never leaves an orphan holding ports.
/// </summary>
public sealed class HostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private readonly bool _ownsDir;

    public string Dir { get; }
    public string TokenPath => Path.Combine(Dir, "admin-token");
    public string HostJsonPath => Path.Combine(Dir, "host.json");

    public string McpUrl { get; private set; } = "";
    public string AdminUrl { get; private set; } = "";
    public string Version { get; private set; } = "";

    public Process Process => _process;
    public string Output { get { lock (_output) { return _output.ToString(); } } }

    private HostProcess(string dir, Process process, bool ownsDir)
    {
        Dir = dir;
        _ownsDir = ownsDir;
        _process = process;
    }

    public static HostProcess Start(string? instanceDir = null)
    {
        string dir = instanceDir ?? Directory.CreateTempSubdirectory("toolbox-e2e-").FullName;
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(FindHostDll());
        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add("background");

        // A clean environment: an inherited TOOLBOX_TOOLSETS (or any other TOOLBOX_*) would pin or redirect the
        // host, e.g. turning the toolset toggle into 409 pinned-by-env. Only what the test sets below survives.
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            string key = (string)e.Key;
            if (key.StartsWith("TOOLBOX_", StringComparison.OrdinalIgnoreCase))
                psi.Environment.Remove(key);
        }
        psi.Environment["TOOLBOX_Instance__Dir"] = dir;
        psi.Environment["TOOLBOX_Desired__Path"] = Path.Combine(dir, "desired-state.json");
        psi.Environment["TOOLBOX_Admin__TokenPath"] = Path.Combine(dir, "admin-token");
        psi.Environment["TOOLBOX_Ports__Mcp__Port"] = "0";
        psi.Environment["TOOLBOX_Ports__Admin__Port"] = "0";
        psi.Environment["TOOLBOX_Ports__Viewer__Port"] = "0";

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var host = new HostProcess(dir, process, ownsDir: instanceDir is null);
        process.OutputDataReceived += (_, e) => host.Append(e.Data);
        process.ErrorDataReceived += (_, e) => host.Append(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (_output) { _output.AppendLine(line); }
    }

    /// <summary>Waits for host.json, then for /health on the MCP URL, then reads the token file.</summary>
    public async Task<string> WaitUntilReadyAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var http = new HttpClient();
        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException($"host exited early with code {_process.ExitCode}");

                if (File.Exists(HostJsonPath) && TryReadHostJson())
                {
                    try
                    {
                        string healthUrl = new Uri(new Uri(McpUrl), "/health").ToString();
                        using var resp = await http.GetAsync(healthUrl, cts.Token);
                        if (resp.IsSuccessStatusCode)
                        {
                            string? token = File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : null;
                            if (!string.IsNullOrEmpty(token)) return token;
                        }
                    }
                    catch (HttpRequestException) { /* not listening yet */ }
                }
                await Task.Delay(100, cts.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            throw new InvalidOperationException($"host did not become ready in {timeout.TotalSeconds:0}s ({ex.Message}). Output:\n{Output}", ex);
        }
    }

    private bool TryReadHostJson()
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(HostJsonPath));
            McpUrl = doc.RootElement.GetProperty("mcpUrl").GetString() ?? "";
            AdminUrl = doc.RootElement.GetProperty("adminUrl").GetString() ?? "";
            Version = doc.RootElement.GetProperty("version").GetString() ?? "";
            return McpUrl.Length > 0 && AdminUrl.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            return false;
        }
    }

    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"host did not exit within {timeout.TotalSeconds:0}s. Output:\n{Output}");
        }
        _process.WaitForExit(); // flush the redirected output handlers
        return _process.ExitCode;
    }

    public ValueTask DisposeAsync()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        try { _process.WaitForExit(10_000); } catch (Exception) { }
        _process.Dispose();
        if (_ownsDir) { try { Directory.Delete(Dir, recursive: true); } catch (Exception) { } }
        return ValueTask.CompletedTask;
    }

    /// <summary>The built Host, found beside this test's own build configuration (Debug/Release).</summary>
    public static string FindHostDll()
    {
        string config = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (!File.Exists(Path.Combine(d.FullName, "ToolBox.slnx"))) continue;
            string candidate = Path.Combine(d.FullName, "src", "ToolBox.Host", "bin", config, "net10.0", "ToolBox.Host.dll");
            if (File.Exists(candidate)) return candidate;
            throw new FileNotFoundException($"Host not built for configuration '{config}': {candidate}");
        }
        throw new FileNotFoundException("Could not find the repo root (ToolBox.slnx) above " + AppContext.BaseDirectory);
    }
}
