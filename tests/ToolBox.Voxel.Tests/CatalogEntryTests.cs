using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core;
using ToolBox.Voxel;

namespace ToolBox.Voxel.Tests;

public class CatalogEntryTests
{
    [Fact]
    public void Entry_HasStableIdentity() =>
        Assert.Equal("voxel", VoxelToolsetExtensions.CatalogEntry.Id);

    [Fact]
    public void Register_AddsDescriptor_World_AndViewerService_WithoutStartingIt()
    {
        var services = new ServiceCollection();
        VoxelToolsetExtensions.CatalogEntry.Register(services);

        // Inspect registrations only: building/starting would bind the fixed viewer port.
        Assert.Contains(services, d => d.ServiceType == typeof(ToolsetDescriptor));
        Assert.Contains(services, d => d.ServiceType == typeof(VoxelWorld));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(VoxelViewerBroadcastService));
    }
}
