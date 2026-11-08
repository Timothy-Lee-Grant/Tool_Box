using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToolBox.Core.Desired;

public enum DesiredStateLoadStatus
{
    /// <summary>No file. State is empty ("no opinion"); the file is NOT created.</summary>
    Missing,
    Ok,
    /// <summary>Unreadable (bad JSON, wrong types, IO error). State is empty; the file is left untouched.</summary>
    Corrupt,
    /// <summary>Written by a newer build. State is empty; the file is read-only to us and never overwritten.</summary>
    Unsupported,
}

public sealed record DesiredStateLoadResult(
    DesiredState State,
    DesiredStateLoadStatus Status,
    string Path,
    string? Reason = null,
    int? FoundVersion = null);

public enum DesiredStateSaveOutcome
{
    Saved,
    /// <summary>The existing file is from a newer build; nothing was written.</summary>
    RefusedUnsupported,
    /// <summary>IO failed (e.g. unwritable directory). The previous file, if any, is intact.</summary>
    Failed,
}

/// <param name="BackupPath">Set when a corrupt file was copied aside before writing.</param>
public sealed record DesiredStateSaveResult(
    DesiredStateSaveOutcome Outcome, string Path, string? Reason = null, string? BackupPath = null)
{
    public bool Succeeded => Outcome == DesiredStateSaveOutcome.Saved;
}

/// <summary>
/// Loads and saves the desired-state file. <see cref="Load"/> never throws and never writes;
/// <see cref="Save"/> is the only writer API (the admin API calls it; the host never writes on its own).
/// Stateless apart from the path: Save re-checks the file under a lock instead of trusting an earlier Load.
/// </summary>
public sealed class DesiredStateStore
{
    // One lock for all stores in the process: two instances pointing at the same file must not interleave.
    private static readonly object SaveLock = new();

    private readonly Action<string, string> _move;
    private readonly DesiredStateMigrator _migrator = new();

    public string Path { get; }

    public DesiredStateStore(string? path = null) : this(path, null) { }

    internal DesiredStateStore(string? path, Action<string, string>? move)
    {
        Path = path ?? DefaultPath();
        _move = move ?? ((from, to) => File.Move(from, to, overwrite: true));
    }

    /// <summary>
    /// Per-user, per-machine location resolved by .NET: %LOCALAPPDATA% on Windows,
    /// ~/Library/Application Support on macOS, $XDG_DATA_HOME or ~/.local/share on Linux.
    /// </summary>
    public static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox", "desired-state.json");

    public DesiredStateLoadResult Load() => LoadCore();

    private DesiredStateLoadResult LoadCore()
    {
        DesiredStateLoadResult Fail(DesiredStateLoadStatus status, string reason, int? version = null) =>
            new(new DesiredState(), status, Path, reason, version);

        try
        {
            if (Directory.Exists(Path))
            {
                return Fail(DesiredStateLoadStatus.Corrupt, "the path is a directory, not a file.");
            }
            if (!File.Exists(Path))
            {
                return new(new DesiredState(), DesiredStateLoadStatus.Missing, Path);
            }

            var text = File.ReadAllText(Path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Fail(DesiredStateLoadStatus.Corrupt, "the file is empty.");
            }

            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var migration = _migrator.Migrate(node);
            switch (migration.Outcome)
            {
                case MigrationOutcome.Unsupported:
                    return Fail(DesiredStateLoadStatus.Unsupported, migration.Reason!, migration.FoundVersion);
                case MigrationOutcome.Corrupt:
                    return Fail(DesiredStateLoadStatus.Corrupt, migration.Reason!, migration.FoundVersion);
            }

            var state = migration.Document.Deserialize<DesiredState>(DesiredStateJson.Options);
            return state is null
                ? Fail(DesiredStateLoadStatus.Corrupt, "the file's root is null.")
                : new(state, DesiredStateLoadStatus.Ok, Path, null, state.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException
                                       or NotSupportedException or FormatException
                                       or IOException or UnauthorizedAccessException)
        {
            // ArgumentException also covers duplicate or case-colliding toolset ids from a hand edit.
            return Fail(DesiredStateLoadStatus.Corrupt, ex.Message);
        }
    }

    /// <summary>
    /// Atomically replaces the file: write a temp file in the same directory, flush, then rename over the target.
    /// A crash leaves the old file or the new one, never half a file. Never throws for IO problems.
    /// </summary>
    public DesiredStateSaveResult Save(DesiredState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (SaveLock)
        {
            string? backup = null;
            var existing = LoadCore();

            if (existing.Status == DesiredStateLoadStatus.Unsupported)
            {
                return new(DesiredStateSaveOutcome.RefusedUnsupported, Path, existing.Reason);
            }

            string? tmp = null;
            try
            {
                if (existing.Status == DesiredStateLoadStatus.Corrupt && File.Exists(Path))
                {
                    // The only moment a bad file is set aside: we are about to replace it.
                    backup = $"{Path}.bad-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
                    File.Copy(Path, backup, overwrite: false);
                }

                var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
                Directory.CreateDirectory(dir);

                var node = JsonSerializer.SerializeToNode(state, DesiredStateJson.Options)!.AsObject();
                node["schemaVersion"] = DesiredState.CurrentSchemaVersion; // always stamp
                var json = node.ToJsonString(DesiredStateJson.Options);

                tmp = System.IO.Path.Combine(dir, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // 0600
                }

                using (var fs = new FileStream(tmp, options))
                {
                    using var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
                    writer.Write(json);
                    writer.Flush();
                    fs.Flush(flushToDisk: true);
                }

                _move(tmp, Path);
                tmp = null; // moved: nothing to clean up
                return new(DesiredStateSaveOutcome.Saved, Path, null, backup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new(DesiredStateSaveOutcome.Failed, Path, ex.Message, backup);
            }
            finally
            {
                if (tmp is not null)
                {
                    try { File.Delete(tmp); } catch (Exception) { /* best effort */ }
                }
            }
        }
    }
}
