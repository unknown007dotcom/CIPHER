using CIPHER.Core;

namespace CIPHER.Brain;

/// <summary>
/// Maps free-form text the deterministic grammar could not handle onto CIPHER's
/// structured intents, using the local brain only as a <em>classifier</em>. The model
/// picks one of six category labels; turning the label plus the user's own words into
/// a <see cref="ParsedCommand"/> (target extraction, risk, summary) is deterministic
/// code. Every mapped command still flows through the normal policy evaluation and
/// confirmation flow, so a misclassification can never bypass a safety check — the
/// worst case is a wrong-but-ordinary command the user can see and cancel.
/// </summary>
public sealed class BrainIntentMapper
{
    private readonly ILocalBrain _brain;

    public BrainIntentMapper(ILocalBrain brain)
    {
        _brain = brain ?? throw new ArgumentNullException(nameof(brain));
    }

    public static string BuildClassificationPrompt(string commandText)
    {
        return "Classify this command into exactly one category: open app, search web, " +
               "type text, click button, answer question, or none.\n\n" +
               $"Command: {commandText}\n\nCategory:";
    }

    /// <summary>
    /// Returns a mapped command, or null when the brain is unavailable/fails or is
    /// confident the text is not a command at all (category "none").
    /// </summary>
    public ParsedCommand? TryMap(string originalText, string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        string label;
        try
        {
            label = _brain.Generate(BuildClassificationPrompt(commandText), maxNewTokens: 8)
                .Trim()
                .ToLowerInvariant();
        }
        catch (Exception)
        {
            // A brain failure must degrade to "not understood", never to a crash.
            return null;
        }

        if (label.Contains("search", StringComparison.Ordinal) ||
            label.Contains("web", StringComparison.Ordinal))
        {
            var query = ExtractSearchQuery(commandText);
            return new ParsedCommand(
                originalText,
                commandText,
                IntentKind.WebSearch,
                Target: query,
                Confidence: 0.55,
                Risk: RiskLevel.Medium,
                Summary: $"Search the web for {query}");
        }

        if (label.Contains("open", StringComparison.Ordinal) ||
            label.Contains("launch", StringComparison.Ordinal))
        {
            var target = ExtractOpenTarget(commandText);
            if (target.Length == 0)
            {
                return null;
            }

            return new ParsedCommand(
                originalText,
                commandText,
                IntentKind.OpenApplication,
                Target: target,
                Confidence: 0.55,
                Risk: RiskLevel.Low,
                Summary: $"Open {target}");
        }

        if (label.Contains("type", StringComparison.Ordinal) ||
            label.Contains("write", StringComparison.Ordinal) ||
            label.Contains("dictate", StringComparison.Ordinal))
        {
            var text = ExtractTypeText(commandText);
            if (text.Length == 0)
            {
                return null;
            }

            return new ParsedCommand(
                originalText,
                commandText,
                IntentKind.TypeText,
                Argument: text,
                Confidence: 0.55,
                Risk: RiskLevel.Medium,
                Summary: "Type text into the focused control");
        }

        if (label.Contains("click", StringComparison.Ordinal) ||
            label.Contains("press", StringComparison.Ordinal) ||
            label.Contains("tap", StringComparison.Ordinal) ||
            label.Contains("button", StringComparison.Ordinal))
        {
            var target = ExtractClickTarget(commandText);
            if (target.Length == 0)
            {
                return null;
            }

            return new ParsedCommand(
                originalText,
                commandText,
                IntentKind.ClickUiElement,
                Target: target,
                Confidence: 0.5,
                Risk: RiskLevel.Medium,
                Summary: $"Click '{target}'");
        }

        if (label.Equals("none", StringComparison.Ordinal) ||
            label.StartsWith("none", StringComparison.Ordinal))
        {
            return null;
        }

        // "answer question", an unrecognized label, or rambling: the safest useful
        // interpretation is to let the brain answer the text as a question. Answering
        // has no side effects, so this fallback can never *do* anything unintended.
        return new ParsedCommand(
            originalText,
            commandText,
            IntentKind.AskLocalBrain,
            Target: commandText,
            Confidence: 0.5,
            Risk: RiskLevel.Low,
            Summary: "Answer with the on-device brain");
    }

    private static readonly string[] LeadingFillers =
    {
        "please", "can you", "could you", "would you", "will you",
        "i want you to", "i want to", "i need you to", "i need to",
        "hey cipher", "cipher", "just", "okay", "ok"
    };

    private static string ExtractOpenTarget(string commandText)
    {
        var result = StripLeadingFillers(commandText);
        result = StripLeadingPhrases(result, "bring up", "fire up", "open", "launch", "start", "run", "load");
        result = StripLeadingPhrases(result, "the", "a", "an");
        result = StripTrailingPhrases(result, "for me", "please", "application", "app");
        return result.Trim().Trim('"');
    }

    private static string ExtractSearchQuery(string commandText)
    {
        var result = StripLeadingFillers(commandText);
        result = StripLeadingPhrases(
            result,
            "search the web for", "search the internet for", "search for", "search",
            "google for", "google", "look up", "look for",
            "find me information about", "find information about", "find information on",
            "find out about", "get me information about", "find me", "find");
        result = StripTrailingPhrases(result, "on the web", "on the internet", "online", "for me", "please");
        result = result.Trim().Trim('"');
        return result.Length == 0 ? commandText : result;
    }

    private static string ExtractTypeText(string commandText)
    {
        var result = StripLeadingFillers(commandText);
        result = StripLeadingPhrases(result, "type", "write", "enter", "input", "put", "insert", "dictate");
        result = StripLeadingPhrases(result, "the words", "the text", "some text", "text", "words");

        var intoIndex = result.IndexOf(" into ", StringComparison.OrdinalIgnoreCase);
        if (intoIndex > 0)
        {
            result = result[..intoIndex];
        }

        return result.Trim().Trim('"');
    }

    private static string ExtractClickTarget(string commandText)
    {
        var result = StripLeadingFillers(commandText);
        result = StripLeadingPhrases(result, "click on", "click", "press", "tap", "hit", "push", "select");
        result = StripLeadingPhrases(result, "the", "a", "an");
        result = StripTrailingPhrases(result, "button", "for me", "please");
        return result.Trim().Trim('"');
    }

    private static string StripLeadingFillers(string text)
    {
        var result = text.Trim();
        var changed = true;
        while (changed && result.Length > 0)
        {
            changed = false;
            foreach (var filler in LeadingFillers)
            {
                if (result.Equals(filler, StringComparison.OrdinalIgnoreCase))
                {
                    result = string.Empty;
                    changed = true;
                    break;
                }

                if (result.StartsWith(filler + " ", StringComparison.OrdinalIgnoreCase) ||
                    result.StartsWith(filler + ", ", StringComparison.OrdinalIgnoreCase))
                {
                    result = result[(filler.Length + 1)..].TrimStart(' ', ',');
                    changed = true;
                    break;
                }
            }
        }

        return result;
    }

    private static string StripLeadingPhrases(string text, params string[] phrases)
    {
        var result = text.Trim();
        foreach (var phrase in phrases.OrderByDescending(phrase => phrase.Length))
        {
            if (result.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (result.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase))
            {
                result = result[(phrase.Length + 1)..].TrimStart();
            }
        }

        return result;
    }

    private static string StripTrailingPhrases(string text, params string[] phrases)
    {
        var result = text.Trim();
        foreach (var phrase in phrases.OrderByDescending(phrase => phrase.Length))
        {
            if (result.EndsWith(" " + phrase, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^(phrase.Length + 1)].TrimEnd();
            }
        }

        return result;
    }
}
