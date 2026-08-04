using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Embedded;

/// <summary>
/// The toolset's single public doorway (ADR-005), same shape as
/// <c>VoxelToolsetExtensions.AddVoxelToolset()</c>. Only <see cref="BuildTools"/>
/// is registered so far (plan 006 Step 3) — Steps 5-7 add the `physical`-tier
/// tool types (flash/debug/serial), each gated behind
/// <see cref="PhysicalActionGate.HardwareActionsAllowed"/> (Step 4), and Step 8
/// is what actually calls this from <c>ToolBoxServerComposition</c>.
/// </summary>
public static class EmbeddedToolsetExtensions
{
    public static IMcpServerBuilder AddEmbeddedToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<EmbeddedSession>();
        builder.Services.AddSingleton(new FirmwareDirectory(ResolveDefaultFirmwareDirectory()));

        // TimeProvider.System is already registered by AddToolBoxCore() (Core's
        // ServiceCollectionExtensions), same clock ServerInfoProvider resolves —
        // constructed directly here (not via DI-resolved builder.Services later)
        // only because HardwareActionsAllowed is needed immediately below, to
        // decide the toolset descriptor text.
        var gate = new PhysicalActionGate(TimeProvider.System);
        builder.Services.AddSingleton(gate);

        builder.Services.AddToolsetDescriptor(
            name: "Embedded",
            description: gate.HardwareActionsAllowed
                ? "Firmware troubleshooting for a NUCLEO-F401RE: compile, flash, GDB-debug, and " +
                  "talk over serial. Hardware actions are enabled (TOOLBOX_ALLOW_HARDWARE_ACTIONS=true)."
                : "Firmware troubleshooting for a NUCLEO-F401RE: compile-only in this session. " +
                  "Hardware actions (flash/debug/serial) are disabled — set " +
                  $"{PhysicalActionGate.EnvironmentVariableName}=true to enable them.");

        builder.WithTools<BuildTools>();

        // Steps 5-7 add their `physical`-tier tool types here, each behind
        // `if (gate.HardwareActionsAllowed) { builder.WithTools<...>(); }` —
        // per plan 006 §2.3, absent from the catalog entirely when hardware
        // actions aren't opted into, not merely refusing at call time.
        // Nothing to gate yet: BuildTools has no `physical` tools, so there's
        // no conditional branch to write until FlashTools exists (Step 5).

        return builder;
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
