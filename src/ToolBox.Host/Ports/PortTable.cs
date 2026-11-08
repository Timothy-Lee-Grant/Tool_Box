using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace ToolBox.Host.Ports;

/// <summary>
/// The ONE place default ports and bind addresses are defined (issue #37). Everything else
/// — Host, Voxel, docs — reads the resolved configuration, which this seeds as the
/// lowest-priority layer. Keys are nested: Ports:Mcp:Port, Ports:Mcp:Address, ...
/// (env form: TOOLBOX_Ports__Mcp__Port).
/// </summary>
internal static class PortTable
{
    public const string Mcp = "Mcp";
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";

    public const string LoopbackAddress = "127.0.0.1";

    public static readonly IReadOnlyDictionary<string, string?> Defaults = new Dictionary<string, string?>
    {
        ["Ports:Mcp:Port"] = "8080",
        ["Ports:Mcp:Address"] = LoopbackAddress,
        ["Ports:Admin:Port"] = "8095",
        ["Ports:Admin:Address"] = LoopbackAddress,
        ["Ports:Viewer:Port"] = "8090",
        ["Ports:Viewer:Address"] = LoopbackAddress,
    };

    public static string PortKey(string service) => $"Ports:{service}:Port";
    public static string AddressKey(string service) => $"Ports:{service}:Address";

    /// <summary>Inserts the defaults as the LOWEST layer, so every other source overrides them.</summary>
    public static void AddDefaults(IConfigurationBuilder builder) =>
        builder.Sources.Insert(0, new MemoryConfigurationSource { InitialData = Defaults });
}
