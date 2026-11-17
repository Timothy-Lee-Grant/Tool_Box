using System.Text.Json;

namespace ToolBox.Core.Instance;

/// <summary>What <c>host.json</c> says about the running host. Never contains the admin token (ADR-019).</summary>
public sealed record HostInfo(int Pid, DateTimeOffset StartedUtc, string Version, string McpUrl, string AdminUrl);

/// <summary>
/// The discovery file for background mode (issue #38). It is a HINT: a client reads it, then verifies with
/// <see cref="HostProbe"/>. A crash leaves it stale, and the failed verification is how that is noticed; the
/// <see cref="InstanceLock"/> is the real authority.
/// </summary>
public static class HostInfoFile
{
    public const string FileName = "host.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PathIn(string directory) => Path.Combine(directory, FileName);

    /// <summary>Temp file then move, so a reader never sees a half-written file.</summary>
    public static void Write(string directory, HostInfo info)
    {
        Directory.CreateDirectory(directory);
        string path = PathIn(directory);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(info, Json));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(tmp); } catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>The recorded info, or null if the file is missing, unreadable or corrupt (never throws for those).</summary>
    public static HostInfo? TryRead(string directory)
    {
        try
        {
            string path = PathIn(directory);
            if (!File.Exists(path)) return null;
            HostInfo? info = JsonSerializer.Deserialize<HostInfo>(File.ReadAllText(path), Json);
            return info is { McpUrl.Length: > 0, AdminUrl.Length: > 0 } ? info : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Delete(string directory)
    {
        try { File.Delete(PathIn(directory)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
