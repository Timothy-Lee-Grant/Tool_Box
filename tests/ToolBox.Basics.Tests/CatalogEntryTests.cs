using Microsoft.Extensions.DependencyInjection;
using ToolBox.Basics;
using ToolBox.Core;

namespace ToolBox.Basics.Tests;

public class CatalogEntryTests
{
    [Fact]
    public void Entry_HasStableIdentity()
    {
        Assert.Equal("basics", BasicsToolsetExtensions.CatalogEntry.Id);
        Assert.Equal("Basics", BasicsToolsetExtensions.CatalogEntry.Name);
    }

    [Fact]
    public void Register_YieldsDescriptor()
    {
        var services = new ServiceCollection();
        BasicsToolsetExtensions.CatalogEntry.Register(services);

        using var sp = services.BuildServiceProvider();
        Assert.Contains(sp.GetServices<ToolsetDescriptor>(), d => d.Name == "Basics");
    }

    // Risk 1 from the plan: Register calls AddMcpServer() itself, so it must be safe to
    // call more than once (once per toolset when several are enabled).
    [Fact]
    public void AddMcpServer_CalledTwice_IsSafe()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();
        services.AddMcpServer();
        BasicsToolsetExtensions.CatalogEntry.Register(services);

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<ToolsetDescriptor>());
    }
}
