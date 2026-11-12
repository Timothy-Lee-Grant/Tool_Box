using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ToolBox.Host.Tests;

/// <summary>
/// The admin listener's guards against real Kestrel sockets: Host/Origin (DNS rebinding), bearer token,
/// content type, and the "every endpoint is guarded" enumeration.
/// </summary>
public sealed class AdminAuthTests(HttpServerFixture server) : IClassFixture<HttpServerFixture>
{
    private static readonly HttpClient Http = new();

    private HttpRequestMessage Request(HttpMethod method, string path, string? token = "default")
    {
        var request = new HttpRequestMessage(method, server.AdminBaseUrl + path);
        if (token == "default") token = server.AdminToken;
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    // ---- bearer token ----

    [Fact]
    public async Task No_token_is_401_with_challenge_and_empty_body()
    {
        var response = await Http.SendAsync(Request(HttpMethod.Get, "/api/v1/health", token: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("a")]
    [InlineData("this-is-a-much-longer-wrong-token-than-the-real-one-0123456789")]
    public async Task Wrong_token_is_401(string token)
    {
        var response = await Http.SendAsync(Request(HttpMethod.Get, "/api/v1/health", token));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_scheme_is_401()
    {
        var request = Request(HttpMethod.Get, "/api/v1/health", token: null);
        request.Headers.TryAddWithoutValidation("Authorization", "Basic " + server.AdminToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Token_in_the_query_string_is_not_accepted()
    {
        var response = await Http.SendAsync(
            Request(HttpMethod.Get, "/api/v1/health?token=" + server.AdminToken, token: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Right_token_gets_health_with_version_and_uptime()
    {
        var response = await Http.SendAsync(Request(HttpMethod.Get, "/api/v1/health"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"version\"", body);
        Assert.Contains("\"uptime\"", body);
        Assert.DoesNotContain(server.AdminToken, body);
    }

    [Fact]
    public async Task Unknown_admin_path_without_token_is_401_not_404()
    {
        // The guard runs before routing, so an unauthenticated caller cannot map the route table.
        var response = await Http.SendAsync(Request(HttpMethod.Get, "/api/v1/nope", token: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Every_endpoint_on_the_admin_listener_requires_the_token()
    {
        var source = server.Services.GetRequiredService<EndpointDataSource>();
        var adminEndpoints = source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(adminEndpoints);

        foreach (RouteEndpoint endpoint in adminEndpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            // Route parameters are replaced by a dummy value so the URL is well-formed.
            string path = System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, "\\{[^}]+\\}", "x");
            foreach (string method in methods)
            {
                var response = await Http.SendAsync(
                    Request(new HttpMethod(method), path, token: null));
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                    $"{method} {path} returned {(int)response.StatusCode} without a token");
            }
        }
    }

    // ---- Host allowlist and Origin ----

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:PORT")]
    [InlineData("127.0.0.1:1")]
    [InlineData("localhost.evil.example:PORT")]
    public async Task Foreign_or_wrong_port_Host_is_421_even_with_a_valid_token(string host)
    {
        int port = new Uri(server.AdminBaseUrl).Port;
        var request = Request(HttpMethod.Get, "/api/v1/health");
        request.Headers.Host = host.Replace("PORT", port.ToString());
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Host_localhost_with_the_admin_port_is_accepted()
    {
        int port = new Uri(server.AdminBaseUrl).Port;
        var request = Request(HttpMethod.Get, "/api/v1/health");
        request.Headers.Host = $"localhost:{port}";
        Assert.Equal(HttpStatusCode.OK, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Host_check_runs_before_auth()
    {
        int port = new Uri(server.AdminBaseUrl).Port;
        var request = Request(HttpMethod.Get, "/api/v1/health", token: null);
        request.Headers.Host = "evil.example:" + port;
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Foreign_Origin_is_403_even_with_a_valid_token()
    {
        var request = Request(HttpMethod.Get, "/api/v1/health");
        request.Headers.Add("Origin", "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Loopback_Origin_on_the_wrong_port_is_403()
    {
        var request = Request(HttpMethod.Get, "/api/v1/health");
        request.Headers.Add("Origin", "http://127.0.0.1:1");
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Loopback_Origin_on_the_admin_port_passes()
    {
        var request = Request(HttpMethod.Get, "/api/v1/health");
        request.Headers.Add("Origin", server.AdminBaseUrl);
        Assert.Equal(HttpStatusCode.OK, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task No_CORS_headers_are_ever_sent_including_on_OPTIONS()
    {
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Options })
        {
            var request = Request(method, "/api/v1/health");
            request.Headers.Add("Access-Control-Request-Method", "GET");
            var response = await Http.SendAsync(request);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"), method.Method);
        }
    }

    // ---- content type on writes ----

    [Fact]
    public async Task Write_with_a_body_but_no_json_content_type_is_415()
    {
        var request = Request(HttpMethod.Post, "/api/v1/token/rotate");
        request.Content = new StringContent("x=1", Encoding.UTF8, "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Failed_auth_never_leaks_the_token_in_any_response()
    {
        var response = await Http.SendAsync(Request(HttpMethod.Get, "/api/v1/health", token: "wrong"));
        string all = response.ToString() + await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(server.AdminToken, all);
    }
}
