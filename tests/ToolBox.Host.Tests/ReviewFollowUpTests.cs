using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ToolBox.Core;

namespace ToolBox.Host.Tests;

/// <summary>Follow-ups from the #32 review: ownership coverage, descriptor/id coupling, uptime start.</summary>
[Collection(VoxelViewerCollection.Name)]
public class ReviewFollowUpTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0",
            settings: new Dictionary<string, string?> { ["Ports:Viewer:Port"] = "0" });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task EveryRegisteredTool_HasAnOwningToolset()
    {
        await using var app = await StartAsync();
        var owners = app.Services.GetRequiredService<ToolOwners>();

        var tools = app.Services.GetServices<McpServerTool>().ToArray();
        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            Assert.True(owners.TryGetOwner(app.Services, tool.ProtocolTool.Name, out _),
                $"tool '{tool.ProtocolTool.Name}' has no owning toolset, so the gate would not filter it");
        }
    }

    [Fact]
    public async Task EveryCatalogId_MatchesADescriptorName()
    {
        await using var app = await StartAsync();
        var names = app.Services.GetServices<ToolsetDescriptor>().Select(d => d.Name).ToArray();

        foreach (var entry in ToolBoxCatalog.Create().Entries)
        {
            Assert.Contains(names, n => string.Equals(n, entry.Id, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Uptime_StartsWithTheHost_NotWithTheFirstHealthCall()
    {
        await using var app = await StartAsync();
        await Task.Delay(400);

        using var http = new HttpClient();
        var json = await http.GetStringAsync($"{app.Urls.First().TrimEnd('/')}/health");
        var uptime = TimeSpan.Parse(JsonDocument.Parse(json).RootElement.GetProperty("uptime").GetString()!);

        Assert.True(uptime >= TimeSpan.FromMilliseconds(300), $"uptime was {uptime}");
    }
}
