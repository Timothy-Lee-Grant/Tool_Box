using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
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
}
