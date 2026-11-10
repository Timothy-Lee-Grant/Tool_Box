using Microsoft.AspNetCore.Builder;
using ToolBox.Host;

namespace ToolBox.Host.Tests;

/// <summary>
/// Boots the real HTTP app (production composition, real Kestrel, real sockets) on an
/// ephemeral port for the duration of a test class. xunit creates one fixture per class
/// (IClassFixture), so the server starts once, all tests share it, and DisposeAsync
/// tears it down deterministically.
/// </summary>
public sealed class HttpServerFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private readonly string _tokenDir = Path.Combine(Path.GetTempPath(), "toolbox-tests-" + Guid.NewGuid().ToString("N"));

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The admin (control-plane) listener, also on an ephemeral port.</summary>
    public string AdminBaseUrl { get; private set; } = string.Empty;

    /// <summary>The token the host created in a temp file; tests send it as a bearer token.</summary>
    public string AdminToken { get; private set; } = string.Empty;

    public string AdminTokenPath => Path.Combine(_tokenDir, "admin-token");

    public async Task InitializeAsync()
    {
        // Port 0 = "OS, give me any free port" — parallel CI runs can never collide.
        _app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0",
            settings: new Dictionary<string, string?>
            {
                ["Ports:Viewer:Port"] = "0",
                ["Ports:Admin:Port"] = "0",
                ["Admin:TokenPath"] = AdminTokenPath,
            });
        await _app.StartAsync();

        // After StartAsync, app.Urls holds the RESOLVED addresses (real ports, not 0).
        // Build appends the admin URL last, so it is the last entry.
        BaseUrl = _app.Urls.First().TrimEnd('/');
        AdminBaseUrl = _app.Urls.Last().TrimEnd('/');
        AdminToken = File.ReadAllText(AdminTokenPath).Trim();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        try { Directory.Delete(_tokenDir, recursive: true); } catch (IOException) { }
    }
}
