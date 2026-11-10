namespace ToolBox.Host.Tests;

/// <summary>
/// Real sockets, two listeners: the control plane (/api/v1, admin port) and the data plane
/// (/mcp and /health, MCP port) must not see each other's routes. No routes exist under /api/v1
/// yet (PR 1 step 5), so "reachable" here means "not rejected by the separation middleware" is
/// covered later; for now both planes answering 404 for /api/v1 is the pin, plus the reverse.
/// </summary>
public sealed class PlaneSeparationTests(HttpServerFixture server) : IClassFixture<HttpServerFixture>
{
    private static readonly HttpClient Http = new();

    [Fact]
    public void Admin_and_MCP_listeners_are_on_different_loopback_ports()
    {
        var mcp = new Uri(server.BaseUrl);
        var admin = new Uri(server.AdminBaseUrl);

        Assert.NotEqual(mcp.Port, admin.Port);
        Assert.Equal("127.0.0.1", mcp.Host);
        Assert.Equal("127.0.0.1", admin.Host);
    }

    [Fact]
    public async Task MCP_port_still_serves_health()
    {
        var response = await Http.GetAsync($"{server.BaseUrl}/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/x")]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1")]
    public async Task Admin_paths_are_404_on_the_MCP_port(string path)
    {
        var response = await Http.GetAsync(server.BaseUrl + path);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/mcp")]
    [InlineData("/")]
    public async Task Data_plane_paths_are_404_on_the_admin_port(string path)
    {
        var response = await Http.GetAsync(server.AdminBaseUrl + path);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_Host_header_naming_the_admin_port_does_not_cross_the_boundary()
    {
        // The client controls Host, so it must never decide which plane a request belongs to.
        int adminPort = new Uri(server.AdminBaseUrl).Port;
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/health");
        request.Headers.Host = $"127.0.0.1:{adminPort}";

        var response = await Http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void The_host_created_the_token_file_at_the_configured_path()
    {
        Assert.True(File.Exists(server.AdminTokenPath));
        Assert.Equal(43, server.AdminToken.Length);
    }
}
