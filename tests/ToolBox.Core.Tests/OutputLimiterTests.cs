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
        Assert.True(result.Length < text.Length, "truncated result should be shorter than the original");
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
