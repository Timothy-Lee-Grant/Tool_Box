using System.Globalization;

namespace ToolBox.Core;

/// <summary>
/// Enforces the platform's core discipline: no tool ever returns unbounded output.
/// An LLM consumer has a context window, not a scrollbar — a 50 MB log dump doesn't
/// inform the model, it evicts everything else it knew.
/// </summary>
public static class OutputLimiter
{
    /// <summary>Default budget, in characters. Roughly a few thousand tokens.</summary>
    public const int DefaultMaxChars = 20_000;

    /// <summary>
    /// Returns <paramref name="text"/> unchanged if it fits the budget; otherwise cuts it
    /// and appends an honest marker stating exactly how much was omitted. Never lies by
    /// silently dropping content.
    /// </summary>
    public static string Limit(string text, int maxChars = DefaultMaxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);

        if (text.Length <= maxChars)
        {
            return text;
        }

        // Don't cut in the middle of a surrogate pair (emoji, CJK extensions, …) —
        // half a character is worse than one character fewer.
        int cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        string omitted = (text.Length - cut).ToString("N0", CultureInfo.InvariantCulture);
        return $"{text[..cut]}\n…[output truncated: {omitted} more characters. Narrow the request to see the rest.]";
    }
}
