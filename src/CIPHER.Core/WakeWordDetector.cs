using System.Text.RegularExpressions;

namespace CIPHER.Core;

/// <summary>Result of wake-phrase detection.</summary>
public readonly record struct WakeWordResult(
    bool Detected,
    string CommandText,
    string OriginalText);

/// <summary>
/// Lightweight text wake-phrase handling. Microphone wake-word detection belongs to the voice
/// module; this class only normalizes typed or transcribed text.
/// </summary>
public sealed class WakeWordDetector
{
    public WakeWordDetector(string phrase = "Hey CIPHER")
    {
        Phrase = string.IsNullOrWhiteSpace(phrase) ? "Hey CIPHER" : phrase.Trim();
    }

    public string Phrase { get; }

    public WakeWordResult Process(string? input)
    {
        var original = input?.Trim() ?? string.Empty;
        if (original.Length == 0)
        {
            return new WakeWordResult(false, string.Empty, original);
        }

        var escapedPhrase = Regex.Escape(Phrase);
        var match = Regex.Match(
            original,
            $"^\\s*{escapedPhrase}(?:\\s*[:,\\-]\\s*|\\s+|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return new WakeWordResult(false, original, original);
        }

        var command = original[match.Length..].Trim();
        return new WakeWordResult(true, command, original);
    }

    public static string NormalizeWhitespace(string? value)
    {
        return Regex.Replace(value?.Trim() ?? string.Empty, "\\s+", " ");
    }
}
