using System.Text;

namespace ToolBox.Core.Instance;

/// <summary>
/// Single-instance guarantee for background mode (issue #38). The lock is an OS-held exclusive open of
/// <c>instance.lock</c> in the per-user data folder: the OS releases it when the process exits, however it
/// exits (even <c>kill -9</c>), so there is never a stale lock to clean up. A pid file cannot promise that.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    public const string FileName = "instance.lock";

    private FileStream? _stream;

    private InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>Same per-user <c>ToolBox</c> folder as the admin token and desired-state file.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox");

    /// <summary>
    /// Takes the lock, or returns null when another process holds it. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the directory cannot be used at all, so callers can
    /// say so instead of pretending another instance exists.
    /// </summary>
    public static InstanceLock? TryAcquire(string directory)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string path = Path.Combine(directory, FileName);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Sharing violation: somebody else holds it open.
            return null;
        }

        stream.SetLength(0);
        stream.Write(Encoding.ASCII.GetBytes(Environment.ProcessId.ToString()));
        stream.Flush();
        return new InstanceLock(stream);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
