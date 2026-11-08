using System.Text.RegularExpressions;

namespace CIPHER.Core;

/// <summary>
/// Offline deterministic intent parser. It deliberately supports a bounded command grammar so
/// CIPHER v1 can act predictably without a cloud service or generative model.
/// </summary>
public sealed class DeterministicCommandParser : ICommandParser
{
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly WakeWordDetector _wakeWordDetector;

    public DeterministicCommandParser()
        : this(new WakeWordDetector())
    {
    }

    public DeterministicCommandParser(WakeWordDetector wakeWordDetector)
    {
        _wakeWordDetector = wakeWordDetector ?? throw new ArgumentNullException(nameof(wakeWordDetector));
    }

    public ParsedCommand Parse(string input, CommandContext? context = null)
    {
        var original = input?.Trim() ?? string.Empty;
        if (original.Length == 0)
        {
            return ParsedCommand.Unknown(original, string.Empty, "No command was provided.");
        }

        var wake = _wakeWordDetector.Process(original);
        var commandText = WakeWordDetector.NormalizeWhitespace(wake.CommandText);
        if (commandText.Length == 0)
        {
            return ParsedCommand.Unknown(original, commandText, "Wake phrase detected, but no command followed it.");
        }

        var matchText = commandText.Trim().TrimEnd('.', '!', '?');

        if (IsMatch(matchText, "^(help|what can you do|show commands|list commands)$"))
        {
            return Create(original, commandText, IntentKind.Help, summary: "Show supported commands", confidence: 1.0);
        }

        if (IsMatch(matchText, "^(what(?:'s| is) the time|what time is it|time|current time|tell me the time)$"))
        {
            return Create(original, commandText, IntentKind.GetTime, summary: "Get the current time", confidence: 1.0);
        }

        if (IsMatch(matchText, "^(what(?:'s| is) the date|date|current date|today(?:'s)? date|what day is it)$"))
        {
            return Create(original, commandText, IntentKind.GetDate, summary: "Get the current date", confidence: 1.0);
        }

        if (IsMatch(matchText, "^((system|computer|device) info(?:rmation)?|about this (pc|computer))$"))
        {
            return Create(original, commandText, IntentKind.GetSystemInfo, summary: "Get system information", confidence: 0.98);
        }

        Match match;

        match = Match(commandText, "^(?:run|execute)\\s+(?:command|cmd)\\s*[:\\-]?\\s*(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.RunCommand,
                target: CleanTarget(match.Groups["target"].Value),
                risk: RiskLevel.Critical,
                summary: "Run an arbitrary command",
                confidence: 0.99);
        }

        match = Match(commandText, "^click\\s+(?:on\\s+)?(?:the\\s+)?(?<rest>.+)$");
        if (match.Success)
        {
            var (elementName, clickWindow) = SplitWindowSuffix(
                CleanTarget(match.Groups["rest"].Value));
            return Create(
                original,
                commandText,
                IntentKind.ClickUiElement,
                target: elementName,
                secondaryTarget: clickWindow,
                risk: RiskLevel.Medium,
                summary: clickWindow is null
                    ? $"Click '{elementName}'"
                    : $"Click '{elementName}' in {clickWindow}",
                confidence: 0.94);
        }

        match = Match(commandText, "^(?:type|enter|input|write)\\s+(?<text>.+?)\\s+into\\s+(?:the\\s+)?(?<tail>.+)$");
        if (match.Success)
        {
            var (fieldName, fieldWindow) = SplitWindowSuffix(
                CleanTarget(match.Groups["tail"].Value));
            return Create(
                original,
                commandText,
                IntentKind.TypeIntoUiElement,
                target: fieldName,
                argument: match.Groups["text"].Value.Trim(),
                secondaryTarget: fieldWindow,
                risk: RiskLevel.Medium,
                summary: fieldWindow is null
                    ? $"Type text into the '{fieldName}' field"
                    : $"Type text into the '{fieldName}' field in {fieldWindow}",
                confidence: 0.94);
        }

        match = Match(commandText, "^(?:type|write|enter text)\\s*[:\\-]?\\s*(?<text>.+)$");
        if (match.Success)
        {
            var text = match.Groups["text"].Value.Trim();
            return Create(
                original,
                commandText,
                IntentKind.TypeText,
                argument: text,
                risk: RiskLevel.Medium,
                summary: "Type text into the focused control",
                confidence: 0.97);
        }

        match = Match(commandText, "^(?:press|hotkey|keyboard shortcut)\\s+(?<keys>.+)$");
        if (match.Success)
        {
            var keys = CleanTarget(match.Groups["keys"].Value);
            return Create(
                original,
                commandText,
                IntentKind.PressKeys,
                argument: keys,
                risk: RiskLevel.Medium,
                summary: $"Press {keys}",
                confidence: 0.97);
        }

        match = Match(commandText, "^(?:verify|check|confirm)\\s+(?:that\\s+|if\\s+)?(?<target>.+?)\\s+(?:still\\s+)?(?:contains|has)\\s+(?<text>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            var expectedText = PathSafety.Unquote(match.Groups["text"].Value.Trim());
            return Create(
                original,
                commandText,
                IntentKind.VerifyWindowText,
                target: target,
                argument: expectedText,
                summary: $"Verify that {target} contains the expected text",
                confidence: 0.95);
        }

        match = Match(commandText, "^(?:read|get|show)\\s+(?:the\\s+)?(?:text|contents?)\\s+(?:in|from)\\s+(?:the\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.ReadWindowText,
                target: target,
                summary: $"Read the text shown in {target}",
                confidence: 0.95);
        }

        match = Match(commandText, "^what does\\s+(?:the\\s+)?(?<target>.+?)\\s+say$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.ReadWindowText,
                target: target,
                summary: $"Read the text shown in {target}",
                confidence: 0.93);
        }

        match = Match(commandText, "^(?:create|make|new)\\s+(?:a\\s+)?(?:folder|directory)\\s+(?:in|inside)\\s+(?<location>.+?)\\s+(?:called|named)\\s+(?<name>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.CreateFolder,
                target: CleanPathTarget(match.Groups["location"].Value),
                argument: CleanPathTarget(match.Groups["name"].Value),
                summary: "Create a folder",
                confidence: 0.96);
        }

        match = Match(commandText, "^(?:create|make|new)\\s+(?:a\\s+)?(?:folder|directory)(?:\\s+(?:called|named))?\\s+(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.CreateFolder,
                target: CleanPathTarget(match.Groups["target"].Value),
                summary: "Create a folder",
                confidence: 0.95);
        }

        match = Match(commandText, "^(?:create|make|new)\\s+(?:a\\s+)?file(?:\\s+(?:called|named))?\\s+(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.CreateFile,
                target: CleanPathTarget(match.Groups["target"].Value),
                summary: "Create a file",
                confidence: 0.95);
        }

        match = Match(commandText, "^(?:what files are in|show me files in|list files in)\\s+(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.ListDirectory,
                target: CleanPathTarget(match.Groups["target"].Value),
                summary: "List directory contents",
                confidence: 0.97);
        }

        match = Match(commandText, "^(?:list|show|display)(?:\\s+files)?(?:\\s+(?:in|inside|from))?\\s+(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.ListDirectory,
                target: CleanPathTarget(match.Groups["target"].Value),
                summary: "List directory contents",
                confidence: 0.93);
        }

        match = Match(commandText, "^move\\s+(?<source>.+?)\\s+to\\s+(?<destination>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.MovePath,
                target: CleanPathTarget(match.Groups["source"].Value),
                secondaryTarget: CleanPathTarget(match.Groups["destination"].Value),
                risk: RiskLevel.High,
                summary: "Move a file or folder",
                confidence: 0.94);
        }

        match = Match(commandText, "^copy\\s+(?<source>.+?)\\s+to\\s+(?<destination>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.CopyPath,
                target: CleanPathTarget(match.Groups["source"].Value),
                secondaryTarget: CleanPathTarget(match.Groups["destination"].Value),
                risk: RiskLevel.Medium,
                summary: "Copy a file or folder",
                confidence: 0.94);
        }

        match = Match(commandText, "^rename\\s+(?<source>.+?)\\s+to\\s+(?<destination>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.RenamePath,
                target: CleanPathTarget(match.Groups["source"].Value),
                secondaryTarget: CleanPathTarget(match.Groups["destination"].Value),
                risk: RiskLevel.High,
                summary: "Rename a file or folder",
                confidence: 0.94);
        }

        match = Match(commandText, "^(?:delete|remove)\\s+(?:the\\s+)?(?:file|folder|directory)?\\s*(?<target>.+)$");
        if (match.Success)
        {
            return Create(
                original,
                commandText,
                IntentKind.DeletePath,
                target: CleanPathTarget(match.Groups["target"].Value),
                risk: RiskLevel.Critical,
                summary: "Delete a file or folder",
                confidence: 0.96);
        }

        match = Match(commandText, "^(?:switch to|focus|activate|go to)\\s+(?:the\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.SwitchWindow,
                target: target,
                summary: $"Switch to {target}",
                confidence: 0.96);
        }

        match = Match(commandText, "^(?:close|quit|exit)\\s+(?:the\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.CloseApplication,
                target: target,
                risk: RiskLevel.Medium,
                summary: $"Close {target}",
                confidence: 0.95);
        }

        match = Match(commandText, "^(?:open|launch|start)\\s+(?:the\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            if (LooksLikePath(target))
            {
                return Create(
                    original,
                    commandText,
                    IntentKind.OpenPath,
                    target: CleanPathTarget(target),
                    summary: $"Open {target}",
                    confidence: 0.95);
            }

            return Create(
                original,
                commandText,
                IntentKind.OpenApplication,
                target: target,
                summary: $"Open {target}",
                confidence: 0.95);
        }

        return ParsedCommand.Unknown(
            original,
            commandText,
            "I could not map that request to a supported offline command.");
    }

    private static ParsedCommand Create(
        string original,
        string normalized,
        IntentKind intent,
        string? target = null,
        string? argument = null,
        string? secondaryTarget = null,
        RiskLevel risk = RiskLevel.Low,
        string summary = "",
        double confidence = 0.0)
    {
        return new ParsedCommand(
            original,
            normalized,
            intent,
            target,
            argument,
            secondaryTarget,
            confidence,
            risk,
            summary);
    }

    private static Match Match(string input, string pattern)
    {
        return Regex.Match(input, pattern, Options);
    }

    private static bool IsMatch(string input, string pattern)
    {
        return Regex.IsMatch(input, pattern, Options);
    }

    private static string CleanTarget(string value)
    {
        var result = PathSafety.Unquote(WakeWordDetector.NormalizeWhitespace(value));
        if (result.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
        {
            result = result[4..];
        }

        return result.Trim().TrimEnd('.', ',', ';');
    }

    /// <summary>
    /// Splits "element name in Window Title" into its parts. The separator match is greedy so
    /// element names that themselves contain " in " (for example "Sign in button") stay intact
    /// and only the last " in " introduces the window title.
    /// </summary>
    private static (string Name, string? Window) SplitWindowSuffix(string value)
    {
        var windowMatch = Regex.Match(value, "^(?<head>.+)\\s+in\\s+(?<window>.+)$", Options);
        if (!windowMatch.Success)
        {
            return (value.Trim(), null);
        }

        return (
            windowMatch.Groups["head"].Value.Trim(),
            CleanTarget(windowMatch.Groups["window"].Value));
    }

    private static string CleanPathTarget(string value)
    {
        return PathSafety.Unquote(WakeWordDetector.NormalizeWhitespace(value)).Trim();
    }

    private static bool LooksLikePath(string value)
    {
        var result = PathSafety.Unquote(value);
        return Regex.IsMatch(result, "^(?:[a-zA-Z]:\\\\|\\\\\\\\|\\.\\.\\\\|\\.\\\\|%[^%]+%)") ||
               result.Contains('\\') ||
               result.Contains('/') && Regex.IsMatch(result, "\\.[a-zA-Z0-9]{1,10}$");
    }
}
