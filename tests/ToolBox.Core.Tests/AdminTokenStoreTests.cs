using ToolBox.Core.Admin;

namespace ToolBox.Core.Tests;

public sealed class AdminTokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolbox-token-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "admin-token");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private AdminTokenStore Store() => new(FilePath);

    [Fact]
    public void EnsureExists_creates_directory_and_a_43_char_base64url_token()
    {
        var store = Store();
        store.EnsureExists();

        string token = store.Read()!;
        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void EnsureExists_twice_and_a_second_instance_keep_the_same_token()
    {
        var first = Store();
        first.EnsureExists();
        string token = first.Read()!;

        first.EnsureExists();
        Store().EnsureExists();

        Assert.Equal(token, Store().Read());
    }

    [Fact]
    public void Concurrent_EnsureExists_results_in_one_token_everyone_agrees_on()
    {
        Parallel.For(0, 16, _ => Store().EnsureExists());

        string token = Store().Read()!;
        Assert.True(Store().Validate(token));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp"));
    }

    [Fact]
    public void Read_is_null_when_missing()
    {
        Assert.Null(Store().Read());
    }

    [Fact]
    public void Validate_accepts_the_right_token_and_rejects_everything_else()
    {
        var store = Store();
        store.EnsureExists();
        string token = store.Read()!;

        Assert.True(store.Validate(token));
        Assert.False(store.Validate(null));
        Assert.False(store.Validate(""));
        Assert.False(store.Validate("wrong"));
        Assert.False(store.Validate(token + "x"));
        Assert.False(store.Validate(token[..^1]));
    }

    [Fact]
    public void Validate_is_false_when_there_is_no_file()
    {
        Assert.False(Store().Validate("anything"));
    }

    [Fact]
    public void Rotate_invalidates_the_old_token_immediately()
    {
        var store = Store();
        store.EnsureExists();
        string old = store.Read()!;
        Assert.True(store.Validate(old));

        store.Rotate();

        string fresh = store.Read()!;
        Assert.NotEqual(old, fresh);
        Assert.False(store.Validate(old));
        Assert.True(store.Validate(fresh));
    }

    [Fact]
    public void Replacing_the_file_by_hand_is_picked_up_when_the_mtime_changes()
    {
        var store = Store();
        store.EnsureExists();
        string old = store.Read()!;
        Assert.True(store.Validate(old)); // primes the cache

        File.WriteAllText(FilePath, "hand-written-token");
        // Explicit later timestamp instead of sleeping.
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow.AddMinutes(5));

        Assert.False(store.Validate(old));
        Assert.True(store.Validate("hand-written-token"));
    }

    [Fact]
    public void Creates_owner_only_file_and_directory_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;

        Store().EnsureExists();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void Tightens_a_loose_existing_file_without_changing_the_token()
    {
        if (OperatingSystem.IsWindows()) return;

        var store = Store();
        store.EnsureExists();
        string token = store.Read()!;
        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        store.EnsureExists();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        Assert.Equal(token, store.Read());
    }
}
