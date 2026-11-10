using System.Runtime.CompilerServices;

namespace ToolBox.Host.Tests;

/// <summary>
/// Runs once when the test assembly loads. Every test that builds the HTTP app would otherwise
/// create an admin token in the developer's real app-data folder, and bind the fixed admin port 8095
/// (parallel tests would collide). Tests that care set these keys explicitly, which outranks env vars.
/// </summary>
internal static class AdminTokenIsolation
{
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("TOOLBOX_Admin__TokenPath",
            Path.Combine(Path.GetTempPath(), "toolbox-tests-admin-token", "admin-token"));
        Environment.SetEnvironmentVariable("TOOLBOX_Ports__Admin__Port", "0");
    }
}
