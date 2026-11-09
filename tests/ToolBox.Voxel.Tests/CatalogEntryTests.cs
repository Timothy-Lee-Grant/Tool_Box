using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core;
using ToolBox.Core.Toolsets;
using ToolBox.Voxel;

namespace ToolBox.Voxel.Tests;

public class CatalogEntryTests
{
    [Fact]
    public void Entry_HasStableIdentity() =>
        Assert.Equal("voxel", VoxelToolsetExtensions.CatalogEntry.Id);

    [Fact]
    public void Register_AddsDescriptor_World_ViewerAndLifecycle_WithoutStartingIt()
    {
        var services = new ServiceCollection();
        VoxelToolsetExtensions.CatalogEntry.Register(services);

        // Inspect registrations only: building/starting would bind the fixed viewer port.
        Assert.Contains(services, d => d.ServiceType == typeof(ToolsetDescriptor));
        Assert.Contains(services, d => d.ServiceType == typeof(VoxelWorld));
        Assert.Contains(services, d => d.ServiceType == typeof(VoxelViewerBroadcastService));
        Assert.Contains(services, d => d.ServiceType == typeof(IToolsetLifecycle)
            && d.ImplementationType == typeof(VoxelLifecycle));
        // Off must be possible: a container-started hosted service could never be stopped.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }
}
