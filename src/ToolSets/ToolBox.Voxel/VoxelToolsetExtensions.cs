using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Voxel;

/// <summary>
/// The toolset's single public doorway (ADR-005). The Host composes this toolset with
/// exactly one line and learns nothing about <see cref="VoxelWorld"/> or the tool types
/// inside it.
/// </summary>
public static class VoxelToolsetExtensions
{
    public static IMcpServerBuilder AddVoxelToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Toolset-local singleton — no Core change needed for this (plan 003, Stage 3
        // "note on Core"). One world, shared by whatever is currently connected; see
        // ADR-009 for why that's a documented v1 limitation, not an oversight.
        builder.Services.AddSingleton<VoxelWorld>();

        // Companion infrastructure, not a tool (plan 003, Step 4): broadcasts world
        // changes over a raw loopback WebSocket for a browser viewer. Works identically
        // whichever transport the Host itself runs, because both Program.cs paths
        // (Generic Host for stdio, WebApplication for HTTP) build on the same
        // Microsoft.Extensions.Hosting IHost.
        builder.Services.AddHostedService<VoxelViewerBroadcastService>();

        builder.Services.AddToolsetDescriptor(
            name: "Voxel",
            description: "A live-buildable voxel world: place/remove/mirror cubes with shape " +
                         "primitives (box, cylinder, cone, sphere, tube), inspect the build.");

        return builder.WithTools<VoxelTools>();
    }
}
