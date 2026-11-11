using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;

namespace ToolBox.Host.Tests;

/// <summary>
/// PR 2 steps 1-3 of #35: the toolset read endpoints and PUT /toolsets/{id}/enabled, against real Kestrel.
/// Each test boots its own host with its own desired-state path, because the tests write that file.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public sealed class AdminToolsetTests : IDisposable
{
    private static readonly string[] States = ["off", "starting", "on", "stopping", "failed"];

    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-admin-toolsets-").FullName;
    private readonly HttpClient _http = new();
    private WebApplication? _app;
    private string _adminUrl = string.Empty;
    private string _token = string.Empty;

    private string DesiredPath => Path.Combine(_dir, "state", "desired-state.json");

    public void Dispose()
    {
        _http.Dispose();
        if (_app is not null)
        {
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task StartAsync(string? desiredFile = null, string? desiredPath = null, string? pin = null)
    {
        if (desiredFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesiredPath)!);
            File.WriteAllText(DesiredPath, desiredFile);
        }

        var settings = new Dictionary<string, string?>
        {
            ["Desired:Path"] = desiredPath ?? DesiredPath,
            ["Ports:Viewer:Port"] = "0",
            ["Ports:Admin:Port"] = "0",
            ["Admin:TokenPath"] = Path.Combine(_dir, "admin-token"),
        };
        if (pin is not null) settings["TOOLSETS"] = pin;

        _app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
        await _app.StartAsync();
        _adminUrl = _app.Urls.Last().TrimEnd('/');
        _token = File.ReadAllText(Path.Combine(_dir, "admin-token")).Trim();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? body = null, string? contentType = "application/json")
    {
        var request = new HttpRequestMessage(method, _adminUrl + "/api/v1" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            if (contentType is null) request.Content.Headers.ContentType = null;
            else request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        return await _http.SendAsync(request);
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private Task<HttpResponseMessage> PutEnabled(string id, string body) =>
        SendAsync(HttpMethod.Put, $"/toolsets/{id}/enabled", body);

    private JsonNode FileJson() => JsonNode.Parse(File.ReadAllText(DesiredPath))!;

    private async Task<string> ActualAsync(string id) =>
        (string)(await JsonAsync(await SendAsync(HttpMethod.Get, $"/toolsets/{id}")))["actual"]!;

    private async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(50);
        }
    }

    // ---- reads ----

    [Fact]
    public async Task Status_has_the_documented_shape_and_health_vocabulary()
    {
        await StartAsync();
        var response = await SendAsync(HttpMethod.Get, "/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonAsync(response);
        Assert.NotNull(json["version"]);
        Assert.NotNull(json["uptime"]);
        Assert.NotNull(json["components"]);
        Assert.Equal("ok", (string)json["desiredState"]!["status"]!);
        Assert.Equal(DesiredPath, (string)json["desiredState"]!["path"]!);

        var toolsets = json["toolsets"]!.AsObject();
        Assert.Contains("voxel", toolsets.Select(t => t.Key));
        foreach (var (_, value) in toolsets)
            Assert.Contains((string)value!["actual"]!, States);
    }

    [Fact]
    public async Task Toolsets_lists_the_same_map_and_one_toolset_can_be_read()
    {
        await StartAsync();
        var list = await JsonAsync(await SendAsync(HttpMethod.Get, "/toolsets"));
        Assert.NotNull(list["toolsets"]!["voxel"]);

        var one = await SendAsync(HttpMethod.Get, "/toolsets/VOXEL");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.True((bool)(await JsonAsync(one))["desired"]!);
    }

    [Fact]
    public async Task Unknown_toolset_is_404_problem_json_with_a_stable_code()
    {
        await StartAsync();
        var response = await SendAsync(HttpMethod.Get, "/toolsets/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("unknown-toolset", (string)(await JsonAsync(response))["code"]!);
    }

    [Fact]
    public async Task Settings_from_the_file_appear_in_the_admin_view_but_never_in_health()
    {
        await StartAsync("""{"schemaVersion":1,"toolsets":{"voxel":{"settings":{"dataDir":"/secret/path"}}}}""");

        var one = await JsonAsync(await SendAsync(HttpMethod.Get, "/toolsets/voxel"));
        Assert.Equal("/secret/path", (string)one["settings"]!["dataDir"]!);

        string mcpUrl = _app!.Urls.First().TrimEnd('/');
        string health = await _http.GetStringAsync(mcpUrl + "/health");
        Assert.DoesNotContain("/secret/path", health);
        Assert.DoesNotContain("settings", health);
        Assert.DoesNotContain("pinnedBy", health);
    }

    // ---- PUT enabled ----

    [Fact]
    public async Task Put_enabled_returns_202_describes_the_saved_state_and_converges()
    {
        await StartAsync();
        Assert.Equal("on", await ActualAsyncEventually("voxel", "on"));

        var response = await PutEnabled("voxel", """{"enabled":false}""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // `desired` is right immediately: computed from the state just saved, not the roster's stale cache.
        var body = await JsonAsync(response);
        Assert.True((bool)body["saved"]!);
        Assert.Equal("requested", (string)body["reconcile"]!);
        Assert.False((bool)body["toolset"]!["desired"]!);
        Assert.False((bool)body["toolset"]!["effective"]!);
        Assert.Contains((string)body["toolset"]!["actual"]!, States);

        Assert.False((bool)FileJson()["toolsets"]!["voxel"]!["enabled"]!);

        // The effect becomes visible in /status once the reconciler has run.
        await Eventually(async () => await ActualAsync("voxel") == "off");
        Assert.False((bool)(await JsonAsync(await SendAsync(HttpMethod.Get, "/toolsets/voxel")))["desired"]!);
    }

    private async Task<string> ActualAsyncEventually(string id, string expected)
    {
        await Eventually(async () => await ActualAsync(id) == expected);
        return expected;
    }

    [Fact]
    public async Task Put_enabled_creates_the_directory_when_it_is_missing()
    {
        await StartAsync();
        Assert.False(Directory.Exists(Path.GetDirectoryName(DesiredPath)));

        var response = await PutEnabled("embedded", """{"enabled":true}""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(File.Exists(DesiredPath));
    }

    [Fact]
    public async Task Put_enabled_changes_only_that_toolset_and_keeps_unknown_fields()
    {
        await StartAsync("""{"schemaVersion":1,"toolsets":{"basics":{"enabled":true,"settings":{"a":1},"future":"x"}},"newer":42}""");

        var response = await PutEnabled("voxel", """{"enabled":false}""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var file = FileJson();
        Assert.False((bool)file["toolsets"]!["voxel"]!["enabled"]!);
        Assert.True((bool)file["toolsets"]!["basics"]!["enabled"]!);
        Assert.Equal(1, (int)file["toolsets"]!["basics"]!["settings"]!["a"]!);
        Assert.Equal("x", (string)file["toolsets"]!["basics"]!["future"]!);
        Assert.Equal(42, (int)file["newer"]!);
    }

    [Fact]
    public async Task Put_enabled_for_an_unknown_toolset_is_404_and_writes_nothing()
    {
        await StartAsync();
        var response = await PutEnabled("nope", """{"enabled":true}""");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown-toolset", (string)(await JsonAsync(response))["code"]!);
        Assert.False(File.Exists(DesiredPath));
    }

    [Fact]
    public async Task Put_enabled_on_a_toolset_pinned_by_the_environment_is_409_and_writes_nothing()
    {
        await StartAsync(pin: "basics");
        var response = await PutEnabled("voxel", """{"enabled":true}""");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await JsonAsync(response);
        Assert.Equal("pinned-by-env", (string)body["code"]!);
        Assert.Equal("TOOLBOX_TOOLSETS", (string)body["pinnedBy"]!);
        Assert.False(File.Exists(DesiredPath));

        // The pin is reported on the toolset so the GUI can disable the switch.
        var one = await JsonAsync(await SendAsync(HttpMethod.Get, "/toolsets/voxel"));
        Assert.Equal("TOOLBOX_TOOLSETS", (string)one["pinnedBy"]!);
    }

    [Theory]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"enabled":null}""")]
    [InlineData("""{"other":true}""")]
    [InlineData("""[true]""")]
    [InlineData("""true""")]
    [InlineData("""{ not json""")]
    [InlineData("")]
    public async Task Put_enabled_with_a_bad_body_is_400_invalid_body(string body)
    {
        await StartAsync();
        var response = await PutEnabled("voxel", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid-body", (string)(await JsonAsync(response))["code"]!);
        Assert.False(File.Exists(DesiredPath));
    }

    [Fact]
    public async Task Put_enabled_without_a_json_content_type_is_415()
    {
        await StartAsync();
        var response = await SendAsync(HttpMethod.Put, "/toolsets/voxel/enabled", """{"enabled":false}""", contentType: null);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(File.Exists(DesiredPath));
    }

    [Fact]
    public async Task Put_enabled_refuses_a_file_from_a_newer_build_and_leaves_it_byte_identical()
    {
        const string newer = """{"schemaVersion":99,"toolsets":{"voxel":{"enabled":true}}}""";
        await StartAsync(newer);
        byte[] before = File.ReadAllBytes(DesiredPath);

        var response = await PutEnabled("voxel", """{"enabled":false}""");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await JsonAsync(response);
        Assert.Equal("desired-state-unsupported", (string)body["code"]!);
        Assert.Equal(99, (int)body["foundVersion"]!);
        Assert.Equal(1, (int)body["supportedVersion"]!);
        Assert.Equal(before, File.ReadAllBytes(DesiredPath));
    }

    [Fact]
    public async Task Put_enabled_on_a_corrupt_file_succeeds_and_reports_the_backup()
    {
        await StartAsync("{ not json");

        var response = await PutEnabled("voxel", """{"enabled":false}""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string? backup = (string?)(await JsonAsync(response))["backupPath"];
        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.Equal("{ not json", File.ReadAllText(backup));
        Assert.False((bool)FileJson()["toolsets"]!["voxel"]!["enabled"]!);
    }

    [Fact]
    public async Task Put_enabled_with_an_unwritable_path_is_500_desired_state_write_failed()
    {
        // The "directory" is really a file, so the store cannot create or write beneath it.
        string blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "i am a file");
        await StartAsync(desiredPath: Path.Combine(blocker, "desired-state.json"));

        var response = await PutEnabled("voxel", """{"enabled":false}""");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("desired-state-write-failed", (string)(await JsonAsync(response))["code"]!);
    }

    [Fact]
    public async Task Concurrent_puts_to_different_toolsets_lose_no_update()
    {
        await StartAsync();
        string[] ids = ["basics", "voxel", "embedded"];

        // Each round races one PUT per toolset (all values flip every round). Without the admin write
        // lock two of them Load the same file and the later Save drops the other's change.
        for (int round = 0; round < 20; round++)
        {
            bool value = round % 2 == 0;
            var responses = await Task.WhenAll(ids.Select(id =>
                PutEnabled(id, value ? """{"enabled":true}""" : """{"enabled":false}""")));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));

            var toolsets = FileJson()["toolsets"]!;
            foreach (string id in ids)
                Assert.Equal(value, (bool)toolsets[id]!["enabled"]!);
        }
    }
}
