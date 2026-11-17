using System.Net;
using Microsoft.AspNetCore.Builder;

namespace ToolBox.Host.Tests;

/// <summary>
/// The MCP-socket Host/Origin guard (issue #66, ADR-021) on real Kestrel. Each test boots its own app so it can
/// choose the config; Host and Origin are set directly on the request, as a rebinding page's browser would.
/// </summary>
public sealed class McpGuardTests
{
    private static readonly HttpClient Http = new();

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _dir;

        private TestApp(WebApplication app, string dir) { _app = app; _dir = dir; }

        public string Url => _app.Urls.First().TrimEnd('/');
        public int Port => new Uri(Url).Port;

        public static async Task<TestApp> StartAsync(Dictionary<string, string?>? extra = null)
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbox-tests-" + Guid.NewGuid().ToString("N"));
            var settings = new Dictionary<string, string?>
            {
                ["Ports:Viewer:Port"] = "0",
                ["Ports:Admin:Port"] = "0",
                ["Admin:TokenPath"] = Path.Combine(dir, "admin-token"),
            };
            if (extra is not null) foreach (var kv in extra) settings[kv.Key] = kv.Value;

            var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
            await app.StartAsync();
            return new TestApp(app, dir);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<HttpStatusCode> GetAsync(string url, string? host = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (host is not null) request.Headers.Host = host;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await Http.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Forged_Host_gets_421_when_only_the_new_key_is_set()
    {
        // No bare AllowedHosts here, so ASP.NET's HostFiltering is not in play: the 421 is OUR guard's.
        await using var app = await TestApp.StartAsync(new() { [McpGuardOptions.HostsKey + ":0"] = "toolbox" });

        Assert.Equal(HttpStatusCode.MisdirectedRequest, await GetAsync(app.Url + "/health", host: "evil.example"));
        Assert.Equal(HttpStatusCode.MisdirectedRequest, await GetAsync(app.Url + "/health", host: "evil.example:8081"));
    }

    [Fact]
    public async Task Forged_Host_is_rejected_on_mcp_as_well_as_health()
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.MisdirectedRequest, await GetAsync(app.Url + "/mcp", host: "evil.example"));
        Assert.Equal(HttpStatusCode.MisdirectedRequest, await GetAsync(app.Url + "/health", host: "evil.example"));
    }

    [Fact]
    public async Task Normal_requests_and_loopback_hosts_pass_on_any_port()
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: $"localhost:{app.Port}"));
        // The Docker published-port shape: same name, different port than the listener.
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "localhost:8081"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "localhost.:8081"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "[::1]:8081"));
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("https://evil.example:8080")]
    [InlineData("null")]
    [InlineData("http://localhost.evil.example")]
    public async Task A_foreign_Origin_gets_403(string origin)
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(app.Url + "/health", origin: origin));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(app.Url + "/mcp", origin: origin));
    }

    [Theory]
    [InlineData("http://localhost:6274")]
    [InlineData("https://127.0.0.1:9000")]
    [InlineData("http://[::1]:3000")]
    public async Task A_loopback_Origin_on_any_port_passes(string origin)
    {
        await using var app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", origin: origin));
    }

    [Fact]
    public async Task A_configured_Origin_is_allowed_and_others_still_are_not()
    {
        await using var app = await TestApp.StartAsync(new() { [McpGuardOptions.OriginsKey + ":0"] = "https://app.example" });

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", origin: "https://app.example"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(app.Url + "/health", origin: "https://evil.example"));
    }

    [Fact]
    public async Task A_configured_service_name_passes_with_any_port_and_a_forged_one_does_not()
    {
        await using var app = await TestApp.StartAsync(new() { [McpGuardOptions.HostsKey + ":0"] = "toolbox" });

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "toolbox:8080"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "TOOLBOX"));
        Assert.Equal(HttpStatusCode.MisdirectedRequest, await GetAsync(app.Url + "/health", host: "evil.example"));
    }

    [Fact]
    public async Task Legacy_AllowedHosts_still_admits_its_names_but_forged_Hosts_get_400_from_HostFiltering()
    {
        // Documented behaviour (ADR-021): ASP.NET's HostFiltering runs before our guard and reads AllowedHosts,
        // so for fallback users a forged Host is answered 400 there and never reaches our 421. Pinned so it
        // cannot drift unnoticed.
        await using var app = await TestApp.StartAsync(new() { [McpGuardOptions.LegacyHostsKey] = "localhost;127.0.0.1;toolbox" });

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app.Url + "/health", host: "toolbox:8080"));
        Assert.Equal(HttpStatusCode.BadRequest, await GetAsync(app.Url + "/health", host: "evil.example"));
    }
}
