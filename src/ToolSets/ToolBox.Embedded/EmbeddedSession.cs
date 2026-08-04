namespace ToolBox.Embedded;

/// <summary>
/// Session-scoped state for the Embedded toolset (plan 006) — one process, one
/// active build/flash/debug/serial session, same "singleton, no locking,
/// documented v1 limitation" shape as <c>VoxelWorld</c> (ADR-009). Only
/// build-related state exists so far (plan 006 Step 3); flash/debug/serial
/// state gets added in the steps that actually need it, not pre-built now —
/// the same "separate early, abstract late" discipline ADR-003 established.
/// </summary>
public sealed class EmbeddedSession
{
    public BuildResult? LastBuild { get; set; }
}

/// <param name="Succeeded">True iff the build process exited 0.</param>
/// <param name="ExitCode">The build process's raw exit code.</param>
/// <param name="Output">
/// Combined stdout+stderr, unbounded. Callers route this through
/// <see cref="ToolBox.Core.OutputLimiter"/> before returning it as tool
/// output — this record stores the full transcript so a later
/// <c>get_build_log()</c> call still has it, even after a summary was
/// already returned from <c>build_firmware()</c>.
/// </param>
public sealed record BuildResult(bool Succeeded, int ExitCode, string Output);
