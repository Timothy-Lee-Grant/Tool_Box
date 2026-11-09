using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using ToolBox.Core.Toolsets;
using ToolBox.Embedded;

namespace ToolBox.Embedded.Tests;

public class CatalogEntryTests
{
    [Fact]
    public void Entry_HasStableIdentity() =>
        Assert.Equal("embedded", EmbeddedToolsetExtensions.CatalogEntry.Id);

    [Fact]
    public void Register_AddsDescriptor_AndSession()
    {
        var services = new ServiceCollection();
        EmbeddedToolsetExtensions.CatalogEntry.Register(services);

        Assert.Contains(services, d => d.ServiceType == typeof(ToolsetDescriptor));
        Assert.Contains(services, d => d.ServiceType == typeof(EmbeddedSession));
    }

    [Fact]
    public void Register_IsSideEffectFree_FirmwareLookupIsLazy()
    {
        var services = new ServiceCollection();
        EmbeddedToolsetExtensions.CatalogEntry.Register(services);

        // A factory (not a pre-computed instance) means the repo-root walk has not run yet.
        ServiceDescriptor firmware = Assert.Single(services, d => d.ServiceType == typeof(FirmwareDirectory));
        Assert.NotNull(firmware.ImplementationFactory);
        Assert.Null(firmware.ImplementationInstance);
        Assert.Contains(services, d => d.ServiceType == typeof(IToolsetLifecycle)
            && d.ImplementationType == typeof(EmbeddedLifecycle));
    }

    [Fact]
    public async Task Lifecycle_Start_ResolvesFirmwareDirectory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new FirmwareDirectory("/somewhere"));
        using var provider = services.BuildServiceProvider();

        var lifecycle = new EmbeddedLifecycle(provider);

        Assert.Equal("embedded", lifecycle.ToolsetId);
        await lifecycle.StartAsync(CancellationToken.None);
        await lifecycle.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Lifecycle_Start_FailsWithReason_WhenFirmwareLookupFails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FirmwareDirectory>(_ =>
            throw new InvalidOperationException("could not locate the repo root (ToolBox.slnx)"));
        using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EmbeddedLifecycle(provider).StartAsync(CancellationToken.None));

        Assert.Contains("could not locate the repo root", ex.Message);
    }
}
