using Microsoft.Extensions.Logging;

namespace ToolBox.Core.Logging;

/// <summary>
/// The stderr rule (plan 001, Stage 2 point 6 / ADR-004).
///
/// In a stdio MCP server, stdout IS the wire: the client parses it as JSON-RPC.
/// A single log line written to stdout interleaves with protocol frames and the
/// client sees a corrupted stream — the failure shows up as a baffling client-side
/// parse error, nowhere near the log statement that caused it. So: every provider
/// that could touch stdout is cleared, and console logging is pinned to stderr.
/// </summary>
public static class ToolBoxLogging
{
    /// <summary>
    /// Clears default providers and routes ALL console log levels to stderr.
    /// Call this in the Host before adding anything else to the logging pipeline.
    /// </summary>
    public static ILoggingBuilder UseStderrOnly(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.ClearProviders();
        logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        return logging;
    }
}
