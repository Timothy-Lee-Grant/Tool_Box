namespace ToolBox.Embedded.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that shell out to a real
/// <c>arm-none-eabi-gcc</c> and <c>make</c>. It skips when either is missing from PATH
/// (macOS/Windows CI legs, the Claude Actions runner), unless
/// <c>TOOLBOX_REQUIRE_ARM_GCC=1</c> is set. The Ubuntu CI leg sets that, so a broken
/// toolchain install fails loudly instead of silently skipping (#104).
/// </summary>
public sealed class ArmGccFactAttribute : FactAttribute
{
    public const string RequireEnvVar = "TOOLBOX_REQUIRE_ARM_GCC";

    public ArmGccFactAttribute()
    {
        if (OnPath("arm-none-eabi-gcc") && OnPath("make")) return;
        if (Environment.GetEnvironmentVariable(RequireEnvVar) == "1") return; // run, and fail loudly

        Skip = $"arm-none-eabi-gcc/make not on PATH (set {RequireEnvVar}=1 to make this a failure).";
    }

    private static bool OnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string candidate in new[] { name, name + ".exe" })
            {
                if (File.Exists(Path.Combine(dir, candidate))) return true;
            }
        }
        return false;
    }
}
