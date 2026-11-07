using ToolBox.Embedded;

namespace ToolBox.Embedded.Tests;

/// <summary>
/// Plan 006 Step 4: the safety design from §2.3 (opt-in registration + a
/// single-use, short-lived confirm token) proven correct before it's ever
/// pointed at a real board — everything here runs with zero hardware
/// attached, by construction.
///
/// The other half of Step 4.4 ("env var unset/set -> tool absent/present
/// from a test IMcpServerBuilder") is deliberately deferred to Step 5: there
/// is no `physical`-tagged tool type yet to gate (BuildTools has none), and
/// asserting presence/absence of nothing would be a vacuous test. Step 5's
/// FlashTools is what that assertion actually needs.
/// </summary>
public class PhysicalActionGateTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("yes", false)] // deliberately not accepted — only "true"/"1", no synonym guessing
    [InlineData("true", true)]
    [InlineData("TRUE", true)] // case-insensitive
    [InlineData("1", true)]
    public void HardwareActionsAllowed_ParsesTheEnvironmentValue(string? rawValue, bool expected)
    {
        var gate = new PhysicalActionGate(rawValue, new TestClock());

        Assert.Equal(expected, gate.HardwareActionsAllowed);
    }

    [Fact]
    public void IssueToken_ThenConsume_ReturnsValidAndTheOriginalSummary()
    {
        var gate = new PhysicalActionGate(null, new TestClock());

        string token = gate.IssueToken("flash build/nucleo-blink.elf to the attached Nucleo");
        PhysicalActionTokenStatus status = gate.TryConsumeToken(token, out string? summary);

        Assert.Equal(PhysicalActionTokenStatus.Valid, status);
        Assert.Equal("flash build/nucleo-blink.elf to the attached Nucleo", summary);
    }

    [Fact]
    public void TryConsumeToken_UnknownToken_ReturnsUnknown()
    {
        var gate = new PhysicalActionGate(null, new TestClock());

        PhysicalActionTokenStatus status = gate.TryConsumeToken("not-a-real-token", out string? summary);

        Assert.Equal(PhysicalActionTokenStatus.Unknown, status);
        Assert.Null(summary);
    }

    [Fact]
    public void TryConsumeToken_IsSingleUse_SecondAttemptIsUnknown()
    {
        var gate = new PhysicalActionGate(null, new TestClock());
        string token = gate.IssueToken("flash it");

        gate.TryConsumeToken(token, out _);
        PhysicalActionTokenStatus replay = gate.TryConsumeToken(token, out string? summary);

        Assert.Equal(PhysicalActionTokenStatus.Unknown, replay);
        Assert.Null(summary);
    }

    [Fact]
    public void TryConsumeToken_AfterExpiry_ReturnsExpired()
    {
        var clock = new TestClock();
        var gate = new PhysicalActionGate(null, clock);
        string token = gate.IssueToken("flash it");

        clock.Now += TimeSpan.FromMinutes(3); // token lifetime is 2 minutes

        PhysicalActionTokenStatus status = gate.TryConsumeToken(token, out string? summary);

        Assert.Equal(PhysicalActionTokenStatus.Expired, status);
        Assert.Null(summary);
    }

    [Fact]
    public void TryConsumeToken_ExpiredTokenIsAlsoConsumed_CannotBeRetriedAfterSomehowBecomingValidAgain()
    {
        // Not a realistic scenario (time doesn't run backwards) — this proves
        // the removal-on-any-outcome contract directly: an expired token is
        // gone after one lookup, not left sitting in the dictionary.
        var clock = new TestClock();
        var gate = new PhysicalActionGate(null, clock);
        string token = gate.IssueToken("flash it");
        clock.Now += TimeSpan.FromMinutes(3);

        gate.TryConsumeToken(token, out _);
        clock.Now -= TimeSpan.FromMinutes(3); // hypothetically "valid" again by expiry math alone
        PhysicalActionTokenStatus secondAttempt = gate.TryConsumeToken(token, out string? summary);

        Assert.Equal(PhysicalActionTokenStatus.Unknown, secondAttempt);
        Assert.Null(summary);
    }

    [Fact]
    public void IssueToken_RejectsEmptySummary()
    {
        var gate = new PhysicalActionGate(null, new TestClock());

        Assert.Throws<ArgumentException>(() => gate.IssueToken(""));
    }
}
