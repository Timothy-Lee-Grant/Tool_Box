using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ToolBox.Host;

/// <summary>
/// Tells connected MCP clients that the set of visible tools changed (#34). A seam, because only a
/// transport with a long-lived session can push: stdio can, stateless HTTP cannot (ADR-007; HTTP push
/// is deferred to #77). Core never sees this (ADR-003); it lives in Host.
/// </summary>
internal interface IToolListChangedNotifier
{
    ValueTask NotifyAsync(CancellationToken ct);
}

/// <summary>The default (null object): used by stateless HTTP, where clients simply re-run tools/list.</summary>
internal sealed class NullToolListChangedNotifier : IToolListChangedNotifier
{
    public ValueTask NotifyAsync(CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>
/// stdio: one process, one long-lived session, so the single <see cref="McpServer"/> can send
/// <c>notifications/tools/list_changed</c>. The server is resolved lazily (spike 1.1: it comes from DI)
/// and a missing or not-yet-connected server is tolerated: nobody to tell yet.
/// </summary>
internal sealed class StdioToolListChangedNotifier(
    IServiceProvider services, ILogger<StdioToolListChangedNotifier> logger) : IToolListChangedNotifier
{
    public async ValueTask NotifyAsync(CancellationToken ct)
    {
        try
        {
            var server = services.GetService<McpServer>();
            if (server is null) return;
            await server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send tools/list_changed; clients will see the change on their next tools/list.");
        }
    }
}
