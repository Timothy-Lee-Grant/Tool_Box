namespace ToolBox.Core.Instance;

/// <summary>Checks that what <c>host.json</c> describes is really answering.</summary>
public static class HostProbe
{
    private static readonly HttpClient Http = new();

    /// <summary>True if an unauthenticated GET of the MCP port's <c>/health</c> succeeds within the timeout.</summary>
    public static async Task<bool> IsRunningAsync(HostInfo info, TimeSpan? timeout = null)
    {
        if (!Uri.TryCreate(info.McpUrl, UriKind.Absolute, out Uri? mcp)) return false;
        var health = new Uri(mcp.GetLeftPart(UriPartial.Authority) + "/health");
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(health, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
