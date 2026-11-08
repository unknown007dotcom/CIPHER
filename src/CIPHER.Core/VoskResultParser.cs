using System.Text.Json;

namespace CIPHER.Core;

/// <summary>
/// Extracts transcript text from the JSON documents produced by the Vosk recognizer
/// (<c>{"text": "..."}</c> for final results, <c>{"partial": "..."}</c> for partials).
/// Malformed input yields <see langword="null"/> instead of throwing, because recognizer
/// output must never crash the voice pipeline.
/// </summary>
public static class VoskResultParser
{
    public static string? GetText(string? recognizerJson) => GetProperty(recognizerJson, "text");

    public static string? GetPartialText(string? recognizerJson) => GetProperty(recognizerJson, "partial");

    private static string? GetProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(propertyName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
