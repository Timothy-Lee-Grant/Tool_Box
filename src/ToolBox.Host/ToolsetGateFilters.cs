using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host;

/// <summary>
/// Tool name -> owning toolset id, learned from the service descriptors each catalog entry added
/// (proved in the #32 step 1 spike, <c>SdkFilterSeamTests</c>). Built lazily on first use because
/// tool instances come from the descriptors' factories, which need the built provider.
/// A tool with no owner (none today) is never filtered.
/// </summary>
internal sealed class ToolOwners(IReadOnlyDictionary<string, IReadOnlyList<ServiceDescriptor>> descriptorsByToolset)
{
    private readonly object _lock = new();
    private Dictionary<string, string>? _map;

    public static ToolOwners FromDescriptors(IReadOnlyDictionary<string, IReadOnlyList<ServiceDescriptor>> d) => new(d);

    public bool TryGetOwner(IServiceProvider sp, string tool, out string toolsetId)
    {
        var map = _map;
        if (map is null)
        {
            lock (_lock)
            {
                map = _map ??= Build(sp);
            }
        }

        return map.TryGetValue(tool, out toolsetId!);
    }

    private Dictionary<string, string> Build(IServiceProvider sp)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, descriptors) in descriptorsByToolset)
        {
            foreach (var d in descriptors.Where(d => d.ServiceType == typeof(McpServerTool)))
            {
                var tool = d.ImplementationInstance as McpServerTool
                    ?? (McpServerTool?)d.ImplementationFactory?.Invoke(sp);
                if (tool is not null) map[tool.ProtocolTool.Name] = id;
            }
        }

        return map;
    }
}

/// <summary>
/// THE gate choke point for MCP traffic (issue #32, ADR for runtime gating): one filter per surface,
/// both asking <see cref="ToolsetGate.IsOn"/> at request time. No tool checks anything itself,
/// so a new toolset is gated automatically.
/// </summary>
internal static class ToolsetGateFilters
{
    public static IMcpServerBuilder WithToolsetGateFilters(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (ctx, ct) =>
            {
                var result = await next(ctx, ct);
                var sp = ctx.Services!;
                var gate = sp.GetRequiredService<ToolsetGate>();
                var owners = sp.GetRequiredService<ToolOwners>();
                result.Tools = result.Tools
                    .Where(t => !owners.TryGetOwner(sp, t.Name, out var id) || gate.IsOn(id))
                    .ToList();
                return result;
            });

            filters.AddCallToolFilter(next => async (ctx, ct) =>
            {
                var sp = ctx.Services!;
                var gate = sp.GetRequiredService<ToolsetGate>();
                var owners = sp.GetRequiredService<ToolOwners>();
                if (owners.TryGetOwner(sp, ctx.Params!.Name, out var id) && !gate.IsOn(id))
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = ToolsetGate.OffMessage(id) }],
                    };
                }

                return await next(ctx, ct);
            });
        });
}
