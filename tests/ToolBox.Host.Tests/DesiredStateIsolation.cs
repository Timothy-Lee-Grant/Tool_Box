using System.Runtime.CompilerServices;

namespace ToolBox.Host.Tests;

/// <summary>
/// Runs once when the test assembly loads: points the desired-state file at a path that never
/// exists, so no test ever reads the developer's real app-data file (issue #31). Tests that
/// want a file set <c>Desired:Path</c> explicitly, which outranks this env var.
/// </summary>
internal static class DesiredStateIsolation
{
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("TOOLBOX_Desired__Path",
            Path.Combine(Path.GetTempPath(), "toolbox-tests-no-desired-state", "desired-state.json"));
}
