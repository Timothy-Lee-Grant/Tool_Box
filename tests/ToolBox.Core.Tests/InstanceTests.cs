using System.Diagnostics;
using ToolBox.Core.Instance;

namespace ToolBox.Core.Tests;

public sealed class InstanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-instance-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Second_acquire_fails_while_held_and_succeeds_after_dispose()
    {
        using InstanceLock? first = InstanceLock.TryAcquire(_dir);
        Assert.NotNull(first);
        Assert.Null(InstanceLock.TryAcquire(_dir));

        first!.Dispose();
        using InstanceLock? third = InstanceLock.TryAcquire(_dir);
        Assert.NotNull(third);
    }

    [Fact]
    public void HostInfo_round_trips_and_has_no_token_field()
    {
        var info = new HostInfo(4242, DateTimeOffset.UtcNow, "1.2.3", "http://127.0.0.1:8080/mcp", "http://127.0.0.1:8095");
        HostInfoFile.Write(_dir, info);

        Assert.Equal(info, HostInfoFile.TryRead(_dir));
        Assert.DoesNotContain("token", File.ReadAllText(HostInfoFile.PathIn(_dir)), StringComparison.OrdinalIgnoreCase);

        HostInfoFile.Delete(_dir);
        Assert.Null(HostInfoFile.TryRead(_dir));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{}")]
    [InlineData("")]
    public void Corrupt_host_file_reads_as_unknown_not_a_crash(string content)
    {
        File.WriteAllText(HostInfoFile.PathIn(_dir), content);
        Assert.Null(HostInfoFile.TryRead(_dir));
    }

    [Fact]
    public async Task Probe_reports_stale_when_nothing_listens()
    {
        var info = new HostInfo(1, DateTimeOffset.UtcNow, "x", "http://127.0.0.1:1/mcp", "http://127.0.0.1:2");
        Assert.False(await HostProbe.IsRunningAsync(info, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Lock_is_released_when_the_holder_process_is_killed()
    {
        // A child process holds the lock; killing it (no clean shutdown) must free it.
        string script = $"exec 9<>'{Path.Combine(_dir, InstanceLock.FileName)}'; flock -n 9 && sleep 60";
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/flock")) return; // .NET's own lock is tested above
        // .NET FileShare.None on Unix is an advisory flock, so a child using flock(1) contends with it.
        using Process child = Process.Start(new ProcessStartInfo("bash", ["-c", script]) { UseShellExecute = false })!;
        try
        {
            // Wait until the child really holds the lock.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                using InstanceLock? probe = InstanceLock.TryAcquire(_dir);
                if (probe is null) break;
                probe.Dispose();
                Assert.True(DateTime.UtcNow < deadline, "child never took the lock");
                await Task.Delay(50);
            }

            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            using InstanceLock? after = InstanceLock.TryAcquire(_dir);
            Assert.NotNull(after);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }
}
