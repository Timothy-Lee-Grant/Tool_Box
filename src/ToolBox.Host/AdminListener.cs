namespace ToolBox.Host;

/// <summary>
/// Knows which socket port is the admin listener. With <c>Ports:Admin:Port=0</c> the real port exists
/// only after Kestrel starts, so <see cref="Resolve"/> is called once from the ApplicationStarted hook.
/// Compared against <c>HttpContext.Connection.LocalPort</c> — the socket the request actually arrived on.
/// The Host header is NOT used: the client controls it, so it cannot be a boundary.
/// </summary>
internal sealed class AdminListener
{
    private volatile int _port;

    /// <summary>The bound admin port, or 0 until the server has started.</summary>
    public int Port => _port;

    public void Resolve(int port) => _port = port;

    public bool IsAdminPort(int localPort) => _port != 0 && localPort == _port;
}
