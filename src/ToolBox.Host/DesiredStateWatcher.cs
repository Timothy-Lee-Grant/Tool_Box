using Microsoft.Extensions.Logging;

namespace ToolBox.Host;

/// <summary>
/// Watches the desired-state file's directory and calls <c>onChange</c> for events on that one file.
/// Watching the directory (not the file) is what catches an atomic save, which writes a temp file and
/// renames it over the target (DesiredStateStore.Save). Several events per save are expected; the
/// reconciler service debounces them. If the directory does not exist or the watcher fails, the host
/// carries on with manual requests only and says so in the log.
/// </summary>
internal sealed class DesiredStateWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public DesiredStateWatcher(string filePath, Action onChange, ILogger logger)
    {
        var full = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(full)!;
        var name = Path.GetFileName(full);

        if (!Directory.Exists(dir))
        {
            logger.LogInformation(
                "Not watching {Path}: its directory does not exist yet. Changes apply on a manual reconcile request.", full);
            return;
        }

        try
        {
            var w = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            void Handle(string? changed)
            {
                if (string.Equals(changed, name, StringComparison.Ordinal)) onChange();
            }
            w.Changed += (_, e) => Handle(e.Name);
            w.Created += (_, e) => Handle(e.Name);
            w.Deleted += (_, e) => Handle(e.Name);
            w.Renamed += (_, e) => { Handle(e.Name); Handle(e.OldName); };
            w.Error += (_, e) => logger.LogWarning(e.GetException(),
                "The desired-state file watcher failed; use a manual reconcile request.");
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException
                                       or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not watch {Path}; changes apply on a manual reconcile request.", full);
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
