using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core;
using ToolBox.Core.Desired;
using ToolBox.Voxel;

namespace ToolBox.Host.Tests;

/// <summary>Layered merge (pure) and startup wiring of the desired-state file (issue #31, steps 6-7).</summary>
[Collection(VoxelViewerCollection.Name)]
public sealed class DesiredStateWiringTests : IDisposable
{
    private static readonly string[] Known = ["basics", "voxel", "embedded"];
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-wiring-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "desired-state.json");

    private static IConfiguration Config(params (string Key, string? Value)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.ToDictionary(x => x.Key, x => x.Value))
            .Build();

    private static DesiredState State(string json) =>
        JsonSerializer.Deserialize<DesiredState>(json, DesiredStateJson.Options)!;

    // ---- pure merge ----

    [Fact]
    public void NoFile_MatchesToday()
    {
        var r = EnabledResolution.Resolve(Config(), new DesiredState(), Known);

        Assert.Equal(ToolBoxCatalog.DefaultEnabled, r.Effective);
        Assert.Empty(r.PinnedBy);
    }

    [Fact]
    public void File_OverridesAppsettings_PerId()
    {
        var cfg = Config(("Toolsets:Enabled:0", "basics"));
        var st = State("""{"schemaVersion":1,"toolsets":{"embedded":{"enabled":true},"basics":{"enabled":false}}}""");

        var r = EnabledResolution.Resolve(cfg, st, Known);

        Assert.Equal(new[] { "embedded" }, r.Effective);
        Assert.Equal(r.Desired, r.Effective);
    }

    [Fact]
    public void File_WithNoOpinion_FallsThrough()
    {
        var st = State("""{"schemaVersion":1,"toolsets":{"voxel":{"settings":{"a":1}}}}""");

        var r = EnabledResolution.Resolve(Config(), st, Known);

        Assert.Equal(ToolBoxCatalog.DefaultEnabled, r.Effective);
        Assert.True(r.Settings.ContainsKey("voxel"));
    }

    [Fact]
    public void EnvVar_PinsEveryKnownId_ButFileSettingsStillHonoured()
    {
        var cfg = Config((ToolBoxCatalog.EnvKey, "basics"));
        var st = State("""{"schemaVersion":1,"toolsets":{"voxel":{"enabled":true,"settings":{"autoOpen":false}}}}""");

        var r = EnabledResolution.Resolve(cfg, st, Known);

        Assert.Equal(new[] { "basics" }, r.Effective);
        Assert.Contains("voxel", r.Desired);
        Assert.Equal(Known.Length, r.PinnedBy.Count);
        Assert.All(r.PinnedBy.Values, v => Assert.Equal("TOOLBOX_TOOLSETS", v));
        Assert.True(r.Settings["voxel"].ContainsKey("autoOpen"));
    }

    [Fact]
    public void EmptyEnvVar_StillEnablesNothing()
    {
        var st = State("""{"schemaVersion":1,"toolsets":{"voxel":{"enabled":true}}}""");

        var r = EnabledResolution.Resolve(Config((ToolBoxCatalog.EnvKey, "")), st, Known);

        Assert.Empty(r.Effective);
    }

    [Fact]
    public void UnknownFileId_IsReported_NotThrown()
    {
        var st = State("""{"schemaVersion":1,"toolsets":{"ghost":{"enabled":true}}}""");

        var r = EnabledResolution.Resolve(Config(), st, Known);

        Assert.Equal(new[] { "ghost" }, r.UnknownIds);
        Assert.Equal(ToolBoxCatalog.DefaultEnabled, r.Effective);
    }

    // ---- startup wiring ----

    private WebApplication Build(string? fileJson, string? envToolsets = null)
    {
        if (fileJson is not null)
        {
            File.WriteAllText(FilePath, fileJson);
        }

        var settings = new Dictionary<string, string?> { ["Desired:Path"] = FilePath, ["Ports:Viewer:Port"] = "0" };
        if (envToolsets is not null)
        {
            settings[ToolBoxCatalog.EnvKey] = envToolsets;
        }

        return ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
    }

    private static bool HasViewer(WebApplication app) =>
        app.Services.GetServices<VoxelViewerBroadcastService>().Any();

    [Fact]
    public async Task FileTurnsVoxelOff()
    {
        await using var app = Build("""{"schemaVersion":1,"toolsets":{"voxel":{"enabled":false}}}""");

        Assert.False(HasViewer(app));
        Assert.Equal(new[] { "Basics" }, app.Services.GetServices<ToolsetDescriptor>().Select(d => d.Name).ToArray());
    }

    [Fact]
    public async Task EnvVar_OverridesFile()
    {
        await using var app = Build("""{"schemaVersion":1,"toolsets":{"voxel":{"enabled":false}}}""", envToolsets: "basics,voxel");

        Assert.True(HasViewer(app));
    }

    [Fact]
    public async Task CorruptFile_Http200_Degraded_AndFileUntouched()
    {
        await using var app = Build("{ not json");
        await app.StartAsync();
        using var http = new HttpClient();

        var resp = await http.GetAsync($"{app.Urls.First().TrimEnd('/')}/health");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
        Assert.StartsWith("corrupt", doc.RootElement.GetProperty("components").GetProperty("desiredState").GetString());
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
        await app.StopAsync();
    }

    [Fact]
    public async Task NewerVersionFile_IsUnsupported_AndUntouched()
    {
        const string json = """{"schemaVersion":99,"toolsets":{}}""";
        await using var app = Build(json);
        var registry = app.Services.GetRequiredService<ToolBox.Core.Health.ComponentStatusRegistry>();

        Assert.StartsWith("unsupported: schemaVersion 99", registry.Snapshot()["desiredState"]);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task MissingFile_NoComponent_AndNotCreated()
    {
        await using var app = Build(null, envToolsets: "basics");
        var registry = app.Services.GetRequiredService<ToolBox.Core.Health.ComponentStatusRegistry>();

        Assert.DoesNotContain("desiredState", registry.Snapshot().Keys);
        Assert.False(File.Exists(FilePath));
    }
}
