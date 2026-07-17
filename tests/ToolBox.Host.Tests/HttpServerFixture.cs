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

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Port 0 = "OS, give me any free port" — parallel CI runs can never collide.
        _app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0");
        await _app.StartAsync();

        // After StartAsync, app.Urls holds the RESOLVED address (real port, not 0).
        BaseUrl = _app.Urls.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
