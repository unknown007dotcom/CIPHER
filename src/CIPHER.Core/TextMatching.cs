using System.Text.RegularExpressions;

namespace CIPHER.Core;

/// <summary>
/// Whitespace- and case-tolerant text comparison used to verify editor contents after
/// simulated typing. Kept in the platform-independent core so the rules are unit-testable.
/// </summary>
public static class TextMatching
{
    public static string NormalizeForComparison(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Trim(), "\\s+", " ");
    }

    public static bool ContainsText(string? haystack, string? needle)
    {
        var normalizedNeedle = NormalizeForComparison(needle);
        if (normalizedNeedle.Length == 0)
        {
            return false;
        }

        return NormalizeForComparison(haystack)
            .Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase);
    }

    public static int CountOccurrences(string? haystack, string? needle)
    {
        var normalizedHaystack = NormalizeForComparison(haystack);
        var normalizedNeedle = NormalizeForComparison(needle);
        if (normalizedNeedle.Length == 0 || normalizedHaystack.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = normalizedHaystack.IndexOf(normalizedNeedle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += normalizedNeedle.Length;
        }

        return count;
    }
}
