using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #32 step 1 SPIKE (characterization): can the pinned ModelContextProtocol 1.4.1 SDK
/// hide tools from tools/list AND short-circuit tools/call through ONE seam
/// (<c>WithRequestFilters</c>), reading a gate from DI at request time?
/// Also probes how to derive "tool name -> owning toolset" without touching Core's catalog record.
/// Uses two throwaway tool classes on a minimal stateless-HTTP app, not the production Host,
/// because the production composition has no filters yet (that is step 5).
/// </summary>
public class SdkFilterSeamTests
{
    // ---- throwaway toolsets ---------------------------------------------------------------
    [McpServerToolType]
    private sealed class AlphaTools
    {
        [McpServerTool(Name = "alpha_ping"), Description("alpha")]
        public static string AlphaPing() => "alpha-ok";
    }

    [McpServerToolType]
    private sealed class BetaTools
    {
        [McpServerTool(Name = "beta_ping"), Description("beta")]
        public static string BetaPing() => "beta-ok";
    }

    /// <summary>Stand-in for the future Core ToolsetGate: a mutable set of open toolset ids.</summary>
    private sealed class FakeGate
    {
        public HashSet<string> Open { get; } = new(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" };
    }

    /// <summary>Tool name -> toolset id, filled by diffing service descriptors per "entry".</summary>
    private sealed class ToolOwners(Dictionary<string, string> map)
    {
        public bool TryGet(string tool, out string toolset) => map.TryGetValue(tool, out toolset!);
    }

    private static async Task<(WebApplication App, string Url, FakeGate Gate)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var gate = new FakeGate();
        builder.Services.AddSingleton(gate);

        // Same trick the real catalog could use: register each "toolset" and record which
        // McpServerTool descriptors it added, by diffing the collection before/after.
        var mcp = builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true);
        var descriptorsByToolset = new Dictionary<string, List<ServiceDescriptor>>();
        foreach (var (id, register) in new (string, Action)[]
        {
            ("alpha", () => mcp.WithTools<AlphaTools>()),
            ("beta", () => mcp.WithTools<BetaTools>()),
        })
        {
            int before = builder.Services.Count;
            register();
            descriptorsByToolset[id] = builder.Services.Skip(before).ToList();
        }

        // Tool instances are created by the descriptors' factories; resolve them once the
        // provider exists and record name -> toolset id.
        builder.Services.AddSingleton(sp =>
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, descriptors) in descriptorsByToolset)
            {
                foreach (var d in descriptors.Where(d => d.ServiceType == typeof(McpServerTool)))
                {
                    var tool = (McpServerTool)d.ImplementationFactory!(sp);
                    map[tool.ProtocolTool.Name] = id;
                }
            }
            return new ToolOwners(map);
        });

        // THE seam under test: one registration per surface, gate read from DI per request.
        mcp.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (ctx, ct) =>
            {
                var result = await next(ctx, ct);
                var services = ctx.Services!;
                var gate = services.GetRequiredService<FakeGate>();
                var owners = services.GetRequiredService<ToolOwners>();
                result.Tools = result.Tools
                    .Where(t => !owners.TryGet(t.Name, out var ts) || gate.Open.Contains(ts))
                    .ToList();
                return result;
            });

            filters.AddCallToolFilter(next => async (ctx, ct) =>
            {
                var services = ctx.Services!;
                var gate = services.GetRequiredService<FakeGate>();
                var owners = services.GetRequiredService<ToolOwners>();
                string name = ctx.Params!.Name;
                if (owners.TryGet(name, out var ts) && !gate.Open.Contains(ts))
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = $"toolset '{ts}' is off" }],
                    };
                }
                return await next(ctx, ct);
            });
        });

        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        return (app, app.Urls.First().TrimEnd('/'), gate);
    }

    private static Task<McpClient> ConnectAsync(string url) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{url}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    private static string Text(CallToolResult r) =>
        string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    public async Task ListFilter_HidesToolsOfAClosedToolset_AndReadsTheGatePerRequest()
    {
        var (app, url, gate) = await StartAsync();
        await using var _ = app;
        await using var client = await ConnectAsync(url);

        Assert.Equal(["alpha_ping", "beta_ping"],
            (await client.ListToolsAsync()).Select(t => t.Name).Order().ToArray());

        gate.Open.Remove("beta"); // flipped at runtime, no restart, same connection
        Assert.Equal(["alpha_ping"],
            (await client.ListToolsAsync()).Select(t => t.Name).ToArray());

        gate.Open.Add("beta");
        Assert.Equal(2, (await client.ListToolsAsync()).Count);
    }

    [Fact]
    public async Task CallFilter_ShortCircuitsAClosedToolset_WithOurMessage()
    {
        var (app, url, gate) = await StartAsync();
        await using var _ = app;
        await using var client = await ConnectAsync(url);

        gate.Open.Remove("beta");
        CallToolResult off = await client.CallToolAsync("beta_ping");
        Assert.True(off.IsError);
        Assert.Equal("toolset 'beta' is off", Text(off));

        // An open toolset is untouched, and the real tool still runs when re-opened.
        Assert.Equal("alpha-ok", Text(await client.CallToolAsync("alpha_ping")));
        gate.Open.Add("beta");
        Assert.Equal("beta-ok", Text(await client.CallToolAsync("beta_ping")));
    }
}
