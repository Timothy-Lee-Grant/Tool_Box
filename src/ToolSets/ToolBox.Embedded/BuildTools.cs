using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ToolBox.Core;

namespace ToolBox.Embedded;

/// <summary>
/// The Translator (plan 006 §2.2): shells out to <c>make</c>, never touches
/// the board. Lowest-risk area of this toolset — no <c>physical</c>
/// classification anywhere here (contrast <see cref="FlashTools"/>, plan 006
/// Step 5, once it exists).
/// </summary>
[McpServerToolType]
public sealed class BuildTools
{
    private readonly EmbeddedSession _session;
    private readonly string _firmwareDirectory;

    public BuildTools(EmbeddedSession session, FirmwareDirectory firmwareDirectory)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(firmwareDirectory);
        _session = session;
        _firmwareDirectory = firmwareDirectory.Path;
    }

    [McpServerTool(Name = "build_firmware")]
    [Description("Compiles the bundled reference firmware via its Makefile (`make` in " +
                 "firmware/nucleo-blink/). Requires arm-none-eabi-gcc on PATH. Returns a short " +
                 "pass/fail summary; call get_build_log() for the full captured output.")]
    public string BuildFirmware()
    {
        BuildResult result = RunMake("all");
        _session.LastBuild = result;
        return result.Succeeded
            ? "build succeeded (exit 0)"
            : $"build FAILED (exit {result.ExitCode}) — call get_build_log() for details";
    }

    [McpServerTool(Name = "get_build_log")]
    [Description("Returns the captured stdout/stderr from the most recent build_firmware() " +
                 "call in this session, bounded to the platform's output budget.")]
    public string GetBuildLog()
    {
        if (_session.LastBuild is null)
        {
            return "no build has been run yet in this session — call build_firmware() first";
        }
        return OutputLimiter.Limit(_session.LastBuild.Output);
    }

    [McpServerTool(Name = "clean_build")]
    [Description("Removes the build/ directory under firmware/nucleo-blink/ (`make clean`). " +
                 "Does not affect anything already flashed to a board.")]
    public string CleanBuild()
    {
        BuildResult result = RunMake("clean");
        return result.Succeeded
            ? "build directory cleaned"
            : $"clean FAILED (exit {result.ExitCode}): {OutputLimiter.Limit(result.Output)}";
    }

    private BuildResult RunMake(string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "make",
            Arguments = target,
            WorkingDirectory = _firmwareDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start 'make' — is it on PATH?");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string combined = stdout.Length > 0 && stderr.Length > 0
            ? $"{stdout}\n--- stderr ---\n{stderr}"
            : stdout + stderr;

        return new BuildResult(process.ExitCode == 0, process.ExitCode, combined);
    }
}
