using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ToolBox.Core.Admin;

/// <summary>
/// The admin API's shared secret, kept in a per-user file (issue #36, PR 1). It lives in Core because
/// both the host (validates) and the future GUI (reads the token to send it) use it, and nothing here
/// is toolset-specific (ADR-003).
/// <para>
/// The file is the single source of truth: the host re-reads it whenever its modification time changes,
/// so replacing the file by hand or by <see cref="Rotate"/> takes effect without a restart. The token is
/// never logged and never put in an exception message.
/// </para>
/// </summary>
public sealed class AdminTokenStore
{
    private readonly object _gate = new();
    private readonly ILogger? _logger;

    // Cache of the SHA-256 of the token, keyed by the file's mtime + length. We keep only the hash.
    private byte[]? _cachedHash;
    private DateTime _cachedStamp;
    private long _cachedLength = -1;

    public string Path { get; }

    public AdminTokenStore(string? path = null, ILogger? logger = null)
    {
        Path = path ?? DefaultPath();
        _logger = logger;
    }

    /// <summary>Next to desired-state.json: per-user app data, resolved by .NET for each OS.</summary>
    public static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox", "admin-token");

    /// <summary>The current token, or null if the file is missing or empty. For the GUI / tooling, not for logs.</summary>
    public string? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            string text = File.ReadAllText(Path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the token file if it does not exist; never replaces an existing one. The file is written
    /// to a temp name and then moved into place WITHOUT overwrite, so concurrent callers (two hosts
    /// starting together) end up sharing the winner's token and no reader ever sees a half-written file.
    /// </summary>
    public void EnsureExists()
    {
        if (!File.Exists(Path))
        {
            WriteNew(overwrite: false);
        }
        TightenIfLoose();
    }

    /// <summary>Replaces the token with a fresh one. The old token stops working immediately.</summary>
    public void Rotate()
    {
        WriteNew(overwrite: true);
        lock (_gate) { _cachedHash = null; _cachedLength = -1; }
    }

    /// <summary>
    /// True when <paramref name="presented"/> equals the token in the file. Both sides are hashed with
    /// SHA-256 first, so the constant-time compare sees equal-length inputs and the token's length does not leak.
    /// </summary>
    public bool Validate(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;

        byte[]? expected = CurrentHash();
        if (expected is null) return false;

        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private byte[]? CurrentHash()
    {
        lock (_gate)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(Path);
                if (!info.Exists) { _cachedHash = null; _cachedLength = -1; return null; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            DateTime stamp = info.LastWriteTimeUtc;
            if (_cachedHash is not null && stamp == _cachedStamp && info.Length == _cachedLength)
                return _cachedHash;

            string? token = Read();
            if (token is null) { _cachedHash = null; _cachedLength = -1; return null; }

            _cachedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            _cachedStamp = stamp;
            _cachedLength = info.Length;
            return _cachedHash;
        }
    }

    private void WriteNew(bool overwrite)
    {
        string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(dir);
        }
        else
        {
            // 0700 applies only to directories this call creates; an existing shared dir is left alone.
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string tmp = System.IO.Path.Combine(dir, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // 0600

            using (var fs = new FileStream(tmp, options))
            {
                // 32 random bytes from the OS CSPRNG, base64url (43 chars, no padding).
                byte[] bytes = RandomNumberGenerator.GetBytes(32);
                string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                fs.Write(Encoding.ASCII.GetBytes(token));
                fs.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tmp, Path, overwrite);
                tmp = string.Empty;
            }
            catch (IOException) when (!overwrite && File.Exists(Path))
            {
                // Lost the create race: keep the winner's token. Our temp file is deleted below.
            }
        }
        finally
        {
            if (tmp.Length > 0)
            {
                try { File.Delete(tmp); } catch (Exception) { /* best effort */ }
            }
        }
    }

    private void TightenIfLoose()
    {
        if (OperatingSystem.IsWindows() || !File.Exists(Path)) return;

        UnixFileMode loose = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        UnixFileMode mode = File.GetUnixFileMode(Path);
        if ((mode & loose) == 0) return;

        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // Names the path, never the token.
        _logger?.LogWarning("Admin token file {Path} was readable by other users; permissions tightened to owner-only.", Path);
    }
}
