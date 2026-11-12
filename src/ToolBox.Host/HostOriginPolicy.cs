namespace ToolBox.Host;

/// <summary>
/// The Host/Origin rules that defend against DNS rebinding, shared by every guard (issue #66).
/// A rebinding page makes the browser send requests to 127.0.0.1 carrying <c>Host: evil.example</c> and
/// <c>Origin: http://evil.example</c>. Both checks are needed: Host is what a rebinding page cannot forge,
/// Origin is what the MCP spec requires. Pure logic, no HTTP types, so it is unit-testable as a table.
/// </summary>
/// <param name="allowedHosts">Extra host names allowed on top of the built-in loopback names.</param>
/// <param name="strictPort">True: the Host header must carry the listener's own port (admin). False: hostname only (MCP behind a published port).</param>
internal sealed class HostOriginPolicy(IEnumerable<string>? allowedHosts = null, bool strictPort = true)
{
    private readonly HashSet<string> _extraHosts = new(
        (allowedHosts ?? []).Select(NormalizeHost).Where(h => h.Length > 0),
        StringComparer.Ordinal);

    public bool StrictPort { get; } = strictPort;

    /// <summary>Lower-case, strip a trailing dot (<c>localhost.</c>), keep IPv6 in brackets.</summary>
    public static string NormalizeHost(string host)
    {
        string h = host.Trim().ToLowerInvariant();
        if (h.Length > 0 && h[0] != '[' && h.Contains(':')) h = "[" + h + "]"; // bare ::1
        return h.TrimEnd('.');
    }

    public static bool IsLoopbackName(string host)
    {
        string h = NormalizeHost(host);
        return h is "127.0.0.1" or "localhost" or "[::1]";
    }

    /// <summary>Is this Host header acceptable for a listener on <paramref name="localPort"/>?</summary>
    public bool IsAllowedHost(string host, int? hostPort, int localPort)
    {
        if (StrictPort && hostPort != localPort) return false;
        return IsLoopbackName(host) || _extraHosts.Contains(NormalizeHost(host));
    }

    /// <summary>Strict form: an http loopback origin on exactly <paramref name="port"/> (the admin UI's own origin).</summary>
    public static bool IsExactLoopbackOrigin(string origin, int port) =>
        TryParseLoopbackOrigin(origin, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp && uri.Port == port;

    /// <summary>Loose form: http or https loopback origin on any port, or one of the configured full origins.</summary>
    public static bool IsAllowedOrigin(string origin, IEnumerable<string>? configuredOrigins = null)
    {
        if (TryParseLoopbackOrigin(origin, out _)) return true;
        return configuredOrigins is not null
            && configuredOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseLoopbackOrigin(string origin, out Uri uri)
    {
        // "null" (sandboxed iframes, file://) does not parse as an absolute URI, so it is rejected.
        if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && IsLoopbackName(parsed.Host.Equals("::1", StringComparison.Ordinal) ? "[::1]" : parsed.Host))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }
}
