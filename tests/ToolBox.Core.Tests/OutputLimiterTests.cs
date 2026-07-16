using ToolBox.Core;

namespace ToolBox.Core.Tests;

public class OutputLimiterTests
{
    [Fact]
    public void ReturnsTextUnchanged_WhenUnderBudget()
    {
        const string text = "hello";
        Assert.Equal(text, OutputLimiter.Limit(text, maxChars: 10));
    }

    [Fact]
    public void ReturnsTextUnchanged_WhenExactlyAtBudget()
    {
        string text = new('a', 10);
        Assert.Equal(text, OutputLimiter.Limit(text, maxChars: 10));
    }

    [Fact]
    public void Truncates_WhenOverBudget()
    {
        string text = new('a', 150);

        string result = OutputLimiter.Limit(text, maxChars: 100);

        Assert.StartsWith(new string('a', 100), result, StringComparison.Ordinal);
        Assert.Contains("truncated", result, StringComparison.OrdinalIgnoreCase);

        // The actual contract: KEPT CONTENT is cut to exactly the budget.
        // (Not "result is shorter than the original" — when the omitted tail is
        // smaller than the honesty marker, the result can legitimately be longer.
        // CI caught that mistaken assertion; see plan 001 Stage 4, 2026-07-16.)
        Assert.Equal(100, result.IndexOf('\n'));
    }

    [Fact]
    public void Result_IsAlwaysBounded_EvenForHugeInput()
    {
        // The discipline the limiter actually guarantees: total output is at most
        // the budget plus a small constant marker overhead — never proportional
        // to the input. This is the assertion that protects the context window.
        string text = new('a', 500_000);

        string result = OutputLimiter.Limit(text, maxChars: 100);

        const int markerAllowance = 100; // generous; real marker is ~80 chars
        Assert.InRange(result.Length, 100, 100 + markerAllowance);
    }

    [Fact]
    public void Marker_ReportsExactOmittedCount()
    {
        // 1234 chars, budget 1000 → exactly 234 omitted. The marker must not lie.
        string text = new('a', 1234);

        string result = OutputLimiter.Limit(text, maxChars: 1000);

        Assert.Contains("234 more characters", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverCutsASurrogatePairInHalf()
    {
        // Index 99 is the emoji's high surrogate; a naive cut at 100 would keep half of it.
        string text = new string('x', 99) + "\U0001F600" + new string('y', 50);

        string result = OutputLimiter.Limit(text, maxChars: 100);

        int markerStart = result.IndexOf('\n');
        string kept = result[..markerStart];
        Assert.Equal(new string('x', 99), kept); // the emoji was dropped whole, not halved
    }

    [Fact]
    public void Throws_OnNullText()
        => Assert.Throws<ArgumentNullException>(() => OutputLimiter.Limit(null!));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Throws_OnNonPositiveBudget(int maxChars)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OutputLimiter.Limit("x", maxChars));
}
