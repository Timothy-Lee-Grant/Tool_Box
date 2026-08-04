using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Embedded;

/// <summary>
/// The toolset's single public doorway (ADR-005), same shape as
/// <c>VoxelToolsetExtensions.AddVoxelToolset()</c>. Only <see cref="BuildTools"/>
/// is registered so far (plan 006 Step 3) — Steps 4-7 add the gated
/// <c>physical</c>-tier tools (flash/debug/serial) behind
/// <c>TOOLBOX_ALLOW_HARDWARE_ACTIONS</c>, and Step 8 is what actually calls
/// this from <c>ToolBoxServerComposition</c>.
/// </summary>
public static class EmbeddedToolsetExtensions
{
    public static IMcpServerBuilder AddEmbeddedToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<EmbeddedSession>();
        builder.Services.AddSingleton(new FirmwareDirectory(ResolveDefaultFirmwareDirectory()));

        builder.Services.AddToolsetDescriptor(
            name: "Embedded",
            description: "Firmware troubleshooting for a NUCLEO-F401RE: compile the bundled " +
                         "reference firmware (build-only so far; flash/GDB/serial land in later steps).");

        return builder.WithTools<BuildTools>();
    }

    /// <summary>
    /// Walks up from the running assembly to the repo root (identified by
    /// <c>ToolBox.slnx</c>) and returns <c>firmware/nucleo-blink</c> beneath
    /// it. This assumes a repo checkout is present next to the running
    /// binary — true for every way this toolset can currently be run (plan
    /// 006 §3.1: native/stdio-only on the dev machine, not a container),
    /// and a real limitation worth revisiting if that scoping ever changes.
    /// </summary>
    private static string ResolveDefaultFirmwareDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ToolBox.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"could not locate the repo root (ToolBox.slnx) walking up from {AppContext.BaseDirectory} " +
                "— the Embedded toolset expects to run from within a Tool_Box checkout (plan 006 §3.1).");
        }

        return Path.Combine(dir.FullName, "firmware", "nucleo-blink");
    }
}
