using Microsoft.Extensions.DependencyInjection;

namespace ToolBox.Core;

/// <summary>
/// One toolset's listing in the catalog: what it is, and how to switch it on.
/// This is data plus a delegate — no plugin loading (ADR-003). <paramref name="Register"/>
/// takes a plain <see cref="IServiceCollection"/> so Core stays free of any MCP package.
/// </summary>
/// <param name="Id">Short stable lowercase id used in configuration, e.g. "basics".</param>
/// <param name="Name">Human-readable name, e.g. "Basics".</param>
/// <param name="Description">One sentence: what the toolset provides.</param>
/// <param name="Register">Adds the toolset's tools, services and hosted services. Never called for a disabled toolset.</param>
public sealed record ToolsetCatalogEntry(
    string Id,
    string Name,
    string Description,
    Action<IServiceCollection> Register);
