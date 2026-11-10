using System.Net;
using System.Net.Http.Headers;

namespace ToolBox.Host.Tests;

/// <summary>
/// Token rotation against the live host. Its own class (so its own fixture and token file): rotating
/// would invalidate the token other test classes share. Tests run in order inside one class, so each
/// re-reads the file for the current token.
/// </summary>
public sealed class AdminRotationTests(HttpServerFixture server) : IClassFixture<HttpServerFixture>
{
    private static readonly HttpClient Http = new();

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, server.AdminBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Http.SendAsync(request);
    }

    private string CurrentToken() => File.ReadAllText(server.AdminTokenPath).Trim();

    [Fact]
    public async Task Rotate_returns_204_with_no_body_and_the_old_token_stops_working()
    {
        string old = CurrentToken();

        var rotate = await SendAsync(HttpMethod.Post, "/api/v1/token/rotate", old);
        Assert.Equal(HttpStatusCode.NoContent, rotate.StatusCode);
        Assert.Equal(string.Empty, await rotate.Content.ReadAsStringAsync());

        string fresh = CurrentToken();
        Assert.NotEqual(old, fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/health", old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/health", fresh)).StatusCode);
    }

    [Fact]
    public async Task Replacing_the_file_by_hand_takes_effect_without_a_restart()
    {
        string old = CurrentToken();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/health", old)).StatusCode);

        string handMade = "hand-made-token-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(server.AdminTokenPath, handMade);
        // Explicit later mtime instead of sleeping: the host re-reads when the stamp changes.
        File.SetLastWriteTimeUtc(server.AdminTokenPath, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/health", old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/health", handMade)).StatusCode);
    }
}
