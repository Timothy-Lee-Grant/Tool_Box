using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using ToolBox.Core.DependencyInjection;
using ToolBox.Core.Toolsets;

namespace ToolBox.Voxel;

/// <summary>
/// The toolset's single public doorway (ADR-005). The Host composes this toolset with
/// exactly one line and learns nothing about <see cref="VoxelWorld"/> or the tool types
/// inside it.
/// </summary>
public static class VoxelToolsetExtensions
{
    /// <summary>
    /// This toolset's catalog listing (S0, #24). <c>Register</c> adds the MCP server
    /// builder (safe to call repeatedly) and then this toolset's doorway below.
    /// The Host decides whether it is ever invoked.
    /// </summary>
    public static ToolsetCatalogEntry CatalogEntry { get; } = new(
        Id: "voxel",
        Name: "Voxel",
        Description: "A live-buildable voxel world with shape primitives and a browser viewer.",
        Register: services => services.AddMcpServer().AddVoxelToolset());

    public static IMcpServerBuilder AddVoxelToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Toolset-local singleton — no Core change needed for this (plan 003, Stage 3
        // "note on Core"). One world, shared by whatever is currently connected; see
        // ADR-009 for why that's a documented v1 limitation, not an oversight.
        builder.Services.AddSingleton<VoxelWorld>();

        // Companion infrastructure, not a tool (plan 003, Step 4): broadcasts world
        // changes over a raw loopback WebSocket for a browser viewer. Registration is
        // side-effect-free (#32): the viewer is a plain singleton and VoxelLifecycle
        // (started/stopped by the reconciler) binds and releases the port. Deliberately
        // NOT AddHostedService: a container-started service cannot be turned off.
        builder.Services.AddSingleton<VoxelViewerBroadcastService>();
        builder.Services.AddSingleton<IToolsetLifecycle, VoxelLifecycle>();

        builder.Services.AddToolsetDescriptor(
            name: "Voxel",
            description: "A live-buildable voxel world: place/remove/mirror cubes with shape " +
                         "primitives (box, cylinder, cone, sphere, tube), inspect the build.");

        return builder.WithTools<VoxelTools>();
    }
}
