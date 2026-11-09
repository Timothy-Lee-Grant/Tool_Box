using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>
/// Step 8 of #32: /health reports per-toolset desired / effective / actual (+ reason).
/// "off" is normal; only "failed" makes the host degraded (still HTTP 200).
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class HealthToolsetDetailsTests
{
    private static async Task<WebApplication> StartAsync(string? pin = null)
    {
        var settings = new Dictionary<string, string?> { ["Ports:Viewer:Port"] = "0" };
        if (pin is not null) settings[ToolBoxCatalog.EnvKey] = pin;
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement> HealthAsync(WebApplication app)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{app.Urls.First().TrimEnd('/')}/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Health_OnToolset_ReportsDesiredEffectiveActual()
    {
        await using var app = await StartAsync();
        var health = await HealthAsync(app);

        var basics = health.GetProperty("toolsetDetails").GetProperty("basics");
        Assert.True(basics.GetProperty("desired").GetBoolean());
        Assert.True(basics.GetProperty("effective").GetBoolean());
        Assert.Equal("on", basics.GetProperty("actual").GetString());
        Assert.False(basics.TryGetProperty("reason", out _), "reason appears only for failed toolsets");
        Assert.Equal("ok", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_OffToolset_IsNormalNotDegraded()
    {
        await using var app = await StartAsync(pin: "basics");
        var health = await HealthAsync(app);

        var voxel = health.GetProperty("toolsetDetails").GetProperty("voxel");
        Assert.False(voxel.GetProperty("effective").GetBoolean());
        Assert.Equal("off", voxel.GetProperty("actual").GetString());
        Assert.False(voxel.TryGetProperty("reason", out _), "reason appears only for failed toolsets");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.DoesNotContain(health.GetProperty("toolsets").EnumerateArray(),
            t => string.Equals(t.GetString(), "voxel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Health_FailedToolset_ShowsReasonAndDegrades()
    {
        await using var app = await StartAsync(pin: "basics");
        // Force a failure the way the reconciler records one (Embedded can't easily fail from a checkout).
        app.Services.GetRequiredService<ToolsetStatusBoard>().Set("embedded",
            new ToolsetState(ToolsetStatus.Failed, "could not locate the repo root", DateTimeOffset.UtcNow));

        var health = await HealthAsync(app);

        var embedded = health.GetProperty("toolsetDetails").GetProperty("embedded");
        Assert.Equal("failed", embedded.GetProperty("actual").GetString());
        Assert.Equal("could not locate the repo root", embedded.GetProperty("reason").GetString());
        Assert.Equal("degraded", health.GetProperty("status").GetString());
        // The other toolsets keep working.
        Assert.Equal("on", health.GetProperty("toolsetDetails").GetProperty("basics").GetProperty("actual").GetString());
    }
}
