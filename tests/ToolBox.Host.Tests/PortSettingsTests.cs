using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using ToolBox.Host;
using ToolBox.Host.Ports;

namespace ToolBox.Host.Tests;

public class PortSettingsTests
{
    private static PortSettings Resolve(params IReadOnlyDictionary<string, string?>[] layersLowToHigh)
    {
        var builder = new ConfigurationBuilder();
        PortTable.AddDefaults(builder);
        foreach (var layer in layersLowToHigh)
            builder.AddInMemoryCollection(layer);
        return PortSettings.Resolve(builder.Build());
    }

    private static Dictionary<string, string?> Layer(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => (string?)p.Value);

    [Fact]
    public void Defaults_AreTheDocumentedTable()
    {
        var s = Resolve();

        Assert.Equal((8080, IPAddress.Loopback), (s.Mcp.Port, s.Mcp.Address));
        Assert.Equal((8095, IPAddress.Loopback), (s.Admin.Port, s.Admin.Address));
        Assert.Equal((8090, IPAddress.Loopback), (s.Viewer.Port, s.Viewer.Address));
        Assert.Equal("http://127.0.0.1:8080", s.Mcp.Url);
    }

    [Fact]
    public void HigherLayerOverridesLowerLayer_AndUntouchedKeysKeepDefaults()
    {
        var s = Resolve(
            Layer(("Ports:Mcp:Port", "9000"), ("Ports:Viewer:Port", "9100")), // e.g. env
            Layer(("Ports:Mcp:Port", "9001")));                                // e.g. command line

        Assert.Equal(9001, s.Mcp.Port);
        Assert.Equal(9100, s.Viewer.Port);
        Assert.Equal(8095, s.Admin.Port);
    }

    [Fact]
    public void ViewerAddress_CanBeOpenedToAllInterfaces()
    {
        var s = Resolve(Layer(("Ports:Viewer:Address", "0.0.0.0")));

        Assert.Equal(IPAddress.Any, s.Viewer.Address);
    }

    [Theory]
    [InlineData("Ports:Mcp:Port", "abc", "Ports:Mcp:Port")]
    [InlineData("Ports:Mcp:Port", "70000", "Ports:Mcp:Port")]
    [InlineData("Ports:Mcp:Port", "-1", "Ports:Mcp:Port")]
    [InlineData("Ports:Viewer:Address", "not-an-ip", "Ports:Viewer:Address")]
    public void BadValues_FailNamingTheKey(string key, string value, string expectedInMessage)
    {
        var ex = Assert.Throws<PortConfigurationException>(() => Resolve(Layer((key, value))));

        Assert.Contains(expectedInMessage, ex.Message);
    }

    [Fact]
    public void TwoServicesOnTheSamePort_Fail()
    {
        var ex = Assert.Throws<PortConfigurationException>(
            () => Resolve(Layer(("Ports:Viewer:Port", "8080"))));

        Assert.Contains("MCP", ex.Message);
        Assert.Contains("viewer", ex.Message);
        Assert.Contains("8080", ex.Message);
    }

    [Fact]
    public void PortZero_IsNotADuplicate()
    {
        var s = Resolve(Layer(("Ports:Mcp:Port", "0"), ("Ports:Viewer:Port", "0")));

        Assert.Equal(0, s.Mcp.Port);
    }

    [Fact]
    public void AdminOnNonLoopback_IsRefused()
    {
        var ex = Assert.Throws<PortConfigurationException>(
            () => Resolve(Layer(("Ports:Admin:Address", "0.0.0.0"))));

        Assert.Contains("Ports:Admin:Address", ex.Message);
        Assert.Contains("loopback", ex.Message);
    }

    [Fact]
    public async Task HttpApp_BindsMcpToConfiguredAddressAndPort()
    {
        // No overrideUrl: the Ports section decides. Port 0 keeps CI collision-free.
        var app = ToolBoxHttpApp.Build([], settings: new Dictionary<string, string?>
        {
            ["Ports:Mcp:Port"] = "0",
            ["Ports:Mcp:Address"] = "127.0.0.1",
            ["Ports:Viewer:Port"] = "0",
        });
        try
        {
            await app.StartAsync();

            var uri = new Uri(app.Urls.First());
            Assert.Equal("127.0.0.1", uri.Host);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void HttpApp_RejectsInvalidPortConfigBeforeStarting()
    {
        Assert.Throws<PortConfigurationException>(() => ToolBoxHttpApp.Build([], settings: new Dictionary<string, string?>
        {
            ["Ports:Admin:Address"] = "0.0.0.0",
        }));
    }
}
