using ToolBox.Core.Desired;

namespace ToolBox.Core.Tests;

public sealed class DesiredStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolbox-desired-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "desired-state.json");

    public DesiredStateStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private DesiredStateStore Store() => new(FilePath);

    private void WriteRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    private const string Good = """
        { "schemaVersion": 1, "toolsets": { "basics": { "enabled": true }, "ghost": { "enabled": false, "settings": { "x": 1 } } } }
        """;

    // ---- Load ----

    [Fact]
    public void Missing_file_gives_empty_state_and_is_not_created()
    {
        var r = Store().Load();
        Assert.Equal(DesiredStateLoadStatus.Missing, r.Status);
        Assert.Empty(r.State.Toolsets);
        Assert.False(File.Exists(FilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(FilePath)));
    }

    [Fact]
    public void Valid_file_loads()
    {
        WriteRaw(Good);
        var r = Store().Load();
        Assert.Equal(DesiredStateLoadStatus.Ok, r.Status);
        Assert.True(r.State.Toolsets["BASICS"].Enabled);
        Assert.True(r.State.Toolsets.ContainsKey("ghost"));
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, \"toolsets\": { \"basics\": { \"enabled\": tr")]
    [InlineData("{ \"schemaVersion\": 1, \"toolsets\": { \"basics\": { \"enabled\": \"yes\" } } }")]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("[1,2]")]
    [InlineData("{ \"toolsets\": {} }")]
    [InlineData("{ \"schemaVersion\": 1, \"toolsets\": { \"Voxel\": {}, \"voxel\": {} } }")]
    public void Bad_content_is_Corrupt_and_untouched(string content)
    {
        WriteRaw(content);
        var r = Store().Load();
        Assert.Equal(DesiredStateLoadStatus.Corrupt, r.Status);
        Assert.False(string.IsNullOrEmpty(r.Reason));
        Assert.Empty(r.State.Toolsets);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Newer_version_is_Unsupported_and_untouched()
    {
        const string content = "{ \"schemaVersion\": 99, \"toolsets\": {} }";
        WriteRaw(content);
        var r = Store().Load();
        Assert.Equal(DesiredStateLoadStatus.Unsupported, r.Status);
        Assert.Equal(99, r.FoundVersion);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Directory_where_file_should_be_is_Corrupt()
    {
        Directory.CreateDirectory(FilePath);
        Assert.Equal(DesiredStateLoadStatus.Corrupt, Store().Load().Status);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        WriteRaw("""
            {
              // hand edited
              "schemaVersion": 1,
              "toolsets": { "basics": { "enabled": true, }, },
            }
            """);
        Assert.Equal(DesiredStateLoadStatus.Ok, Store().Load().Status);
    }

    [Fact]
    public void Default_path_ends_with_ToolBox_file_under_LocalApplicationData()
    {
        var p = DesiredStateStore.DefaultPath();
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), p);
        Assert.EndsWith(Path.Combine("ToolBox", "desired-state.json"), p);
    }

    // ---- Save ----

    [Fact]
    public void Save_then_Load_round_trips_and_preserves_unknown_ids()
    {
        WriteRaw(Good);
        var store = Store();
        var state = store.Load().State;
        state.Toolsets["voxel"] = new DesiredToolset { Enabled = true };

        Assert.True(store.Save(state).Succeeded);

        var again = store.Load();
        Assert.Equal(DesiredStateLoadStatus.Ok, again.Status);
        Assert.True(again.State.Toolsets["voxel"].Enabled);
        Assert.True(again.State.Toolsets.ContainsKey("ghost"));
        Assert.Equal(1, again.State.SchemaVersion);
    }

    [Fact]
    public void Save_creates_missing_directory_and_stamps_version()
    {
        var result = Store().Save(new DesiredState { SchemaVersion = 0 });
        Assert.True(result.Succeeded);
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_over_corrupt_file_keeps_a_bad_copy()
    {
        WriteRaw("{ not json");
        var result = Store().Save(new DesiredState());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupPath);
        Assert.Equal("{ not json", File.ReadAllText(result.BackupPath!));
        Assert.Equal(DesiredStateLoadStatus.Ok, Store().Load().Status);
    }

    [Fact]
    public void Save_refuses_to_overwrite_newer_version()
    {
        WriteRaw("{ \"schemaVersion\": 99, \"toolsets\": {} }");
        var before = File.ReadAllBytes(FilePath);

        var result = Store().Save(new DesiredState());

        Assert.Equal(DesiredStateSaveOutcome.RefusedUnsupported, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void Save_leaves_no_temp_files()
    {
        var store = Store();
        store.Save(new DesiredState());
        store.Save(new DesiredState());
        Assert.Equal([FilePath], Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void Failed_move_keeps_old_file_and_cleans_temp()
    {
        WriteRaw(Good);
        var before = File.ReadAllText(FilePath);
        var store = new DesiredStateStore(FilePath, (_, _) => throw new IOException("boom"));

        var result = store.Save(new DesiredState());

        Assert.Equal(DesiredStateSaveOutcome.Failed, result.Outcome);
        Assert.Contains("boom", result.Reason);
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void Unwritable_target_returns_Failed_instead_of_throwing()
    {
        // A regular file where the parent directory should be makes CreateDirectory fail on every OS.
        File.WriteAllText(Path.Combine(_dir, "sub"), "i am a file");
        var result = Store().Save(new DesiredState());
        Assert.Equal(DesiredStateSaveOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void Saved_file_is_user_only_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        Store().Save(new DesiredState());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
    }

    [Fact]
    public async Task Concurrent_saves_always_leave_valid_json()
    {
        var store = Store();
        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            var s = new DesiredState();
            s.Toolsets[$"t{i}"] = new DesiredToolset { Enabled = i % 2 == 0 };
            Assert.True(new DesiredStateStore(FilePath).Save(s).Succeeded);
            Assert.Equal(DesiredStateLoadStatus.Ok, store.Load().Status);
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(DesiredStateLoadStatus.Ok, store.Load().Status);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }
}
