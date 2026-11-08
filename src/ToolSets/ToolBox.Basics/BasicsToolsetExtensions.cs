using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Basics;

/// <summary>
/// The toolset's single public doorway (plan 001, Step 2.4 convention).
/// The Host composes this toolset with exactly one line and learns nothing
/// about the tool types inside.
/// </summary>
public static class BasicsToolsetExtensions
{
    /// <summary>
    /// This toolset's catalog listing (S0, #24). <c>Register</c> adds the MCP server
    /// builder (safe to call repeatedly) and then this toolset's doorway below.
    /// The Host decides whether it is ever invoked.
    /// </summary>
    public static ToolsetCatalogEntry CatalogEntry { get; } = new(
        Id: "basics",
        Name: "Basics",
        Description: "Trivial connectivity and identity tools: ping, server_info, current_time.",
        Register: services => services.AddMcpServer().AddBasicsToolset());

    public static IMcpServerBuilder AddBasicsToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddToolsetDescriptor(
            name: "Basics",
            description: "Trivial connectivity and identity tools: ping, server_info, current_time.");

        return builder.WithTools<BasicsTools>();
    }
}
