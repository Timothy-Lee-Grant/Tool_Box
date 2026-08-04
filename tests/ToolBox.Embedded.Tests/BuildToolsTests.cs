using ToolBox.Embedded;

namespace ToolBox.Embedded.Tests;

/// <summary>
/// Real shell-outs to a real <c>arm-none-eabi-gcc</c> against small fixture
/// projects — not mocked. Same instinct as Voxel's real-socket WebSocket
/// test (plan 003, Story 3): a mock of "the compiler succeeded/failed"
/// would hide exactly the kind of process-plumbing bug (wrong working
/// directory, stdout/stderr not both captured, exit code misread) this
/// toolset is actually at risk of. Requires arm-none-eabi-gcc and make on
/// PATH — installed locally via Homebrew during plan 006 Step 2, and via
/// ci.yml's toolchain-install step in CI.
/// </summary>
public class BuildToolsTests
{
    private static FirmwareDirectory Fixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void BuildFirmware_ValidSource_ReportsSuccess()
    {
        var tools = new BuildTools(new EmbeddedSession(), Fixture("valid-firmware"));

        string result = tools.BuildFirmware();

        Assert.Contains("succeeded", result);
    }

    [Fact]
    public void BuildFirmware_BrokenSource_ReportsFailure()
    {
        var tools = new BuildTools(new EmbeddedSession(), Fixture("broken-firmware"));

        string result = tools.BuildFirmware();

        Assert.Contains("FAILED", result);
    }

    [Fact]
    public void GetBuildLog_BeforeAnyBuild_SaysSoRatherThanReturningEmpty()
    {
        var tools = new BuildTools(new EmbeddedSession(), Fixture("valid-firmware"));

        Assert.Contains("no build has been run yet", tools.GetBuildLog());
    }

    [Fact]
    public void GetBuildLog_AfterFailedBuild_ContainsTheRealCompilerError()
    {
        var tools = new BuildTools(new EmbeddedSession(), Fixture("broken-firmware"));
        tools.BuildFirmware();

        string log = tools.GetBuildLog();

        Assert.Contains("undefined_symbol_that_does_not_exist", log);
    }

    [Fact]
    public void CleanBuild_RemovesTheBuildDirectory()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-firmware");
        var tools = new BuildTools(new EmbeddedSession(), new FirmwareDirectory(dir));
        tools.BuildFirmware();
        Assert.True(Directory.Exists(Path.Combine(dir, "build")));

        tools.CleanBuild();

        Assert.False(Directory.Exists(Path.Combine(dir, "build")));
    }
}
