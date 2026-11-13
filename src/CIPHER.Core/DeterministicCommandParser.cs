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

        // --- Jarvis essentials (Stage 11): dictation, volume, lock, power, web search.
        // These verbs are unambiguous, so they stay deterministic; free-form phrasing
        // the grammar cannot pin down is classified by the on-device brain instead.
        if (IsMatch(matchText, "^((start|begin|take|enable)( dictation| dictation mode)?|dictation( mode)? (on|start))$"))
        {
            return Create(original, commandText, IntentKind.StartDictation, summary: "Start dictation (type what you say)", confidence: 0.97);
        }

        if (IsMatch(matchText, "^((stop|end|finish|disable)( dictation| dictation mode)?|dictation( mode)? (off|stop))$"))
        {
            return Create(original, commandText, IntentKind.StopDictation, summary: "Stop dictation", confidence: 0.97);
        }

        if (IsMatch(matchText, "^(volume up|turn( the)? volume up|turn up( the)? volume|raise( the)? volume|increase( the)? volume|sound up)$"))
        {
            return Create(original, commandText, IntentKind.SetVolume, argument: "up", summary: "Turn the volume up", confidence: 0.97);
        }

        if (IsMatch(matchText, "^(volume down|turn( the)? volume down|turn down( the)? volume|lower( the)? volume|decrease( the)? volume|sound down)$"))
        {
            return Create(original, commandText, IntentKind.SetVolume, argument: "down", summary: "Turn the volume down", confidence: 0.97);
        }

        if (IsMatch(matchText, "^mute( the)?( sound| volume| audio)?$"))
        {
            return Create(original, commandText, IntentKind.SetVolume, argument: "mute", summary: "Mute the sound", confidence: 0.97);
        }

        if (IsMatch(matchText, "^unmute( the)?( sound| volume| audio)?$"))
        {
            return Create(original, commandText, IntentKind.SetVolume, argument: "unmute", summary: "Unmute the sound", confidence: 0.97);
        }

        if (IsMatch(matchText, "^lock( the)?( computer| pc| workstation| screen| device)?$"))
        {
            return Create(original, commandText, IntentKind.LockWorkstation, risk: RiskLevel.Medium, summary: "Lock the computer", confidence: 0.97);
        }

        if (IsMatch(matchText, "^(shut ?down|power off)( the)?( computer| pc)?$"))
        {
            return Create(original, commandText, IntentKind.ShutdownComputer, risk: RiskLevel.Critical, summary: "Shut down the computer", confidence: 0.97);
        }

        if (IsMatch(matchText, "^(restart|reboot)( the)?( computer| pc)?$"))
        {
            return Create(original, commandText, IntentKind.RestartComputer, risk: RiskLevel.Critical, summary: "Restart the computer", confidence: 0.97);
        }

        Match match;

        match = Match(commandText, "^(?:search(?:\\s+the\\s+(?:web|internet))?\\s+for|search|google(?:\\s+for)?|look\\s+up|find\\s+(?:out\\s+)?about|find\\s+information\\s+(?:on|about))\\s+(?<query>.+)$");
        if (match.Success)
        {
            var query = CleanTarget(match.Groups["query"].Value);
            return Create(
                original,
                commandText,
                IntentKind.WebSearch,
                target: query,
                risk: RiskLevel.Medium,
                summary: $"Search the web for {query}",
                confidence: 0.96);
        }

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

        if (IsMatch(matchText, "^(verify|check) audit log$"))
        {
            return Create(
                original,
                commandText,
                IntentKind.VerifyAuditLog,
                summary: "Verify the local audit log",
                confidence: 0.97);
        }

        if (IsMatch(matchText, "^(list|show) quarantine$"))
        {
            return Create(
                original,
                commandText,
                IntentKind.ListQuarantine,
                summary: "List items in CIPHER quarantine",
                confidence: 0.97);
        }

        match = Match(commandText, "^restore\\s+(?:from\\s+quarantine\\s+|quarantined\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.RestoreQuarantineItem,
                target: target,
                risk: RiskLevel.Medium,
                summary: $"Restore '{target}' from quarantine",
                confidence: 0.93);
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

        match = Match(commandText, "^(?:ask\\s+(?:the\\s+)?ai(?:\\s+to)?|delegate(?:\\s+to\\s+(?:the\\s+)?ai)?)[:,]?\\s+(?<prompt>.+)$");
        if (match.Success)
        {
            var prompt = match.Groups["prompt"].Value.Trim();
            return Create(
                original,
                commandText,
                IntentKind.AskExternalAi,
                target: prompt,
                risk: RiskLevel.High,
                summary: $"Ask external AI: {TruncateForSummary(prompt)}",
                confidence: 0.95);
        }

        match = Match(commandText, "^ask\\s+(?<question>.+)$");
        if (match.Success)
        {
            var question = match.Groups["question"].Value.Trim();
            return Create(
                original,
                commandText,
                IntentKind.AskLocalBrain,
                target: question,
                summary: "Answer with the on-device brain",
                confidence: 0.95);
        }

        match = Match(commandText, "^(?:open website|browse to|go to|navigate to|visit)\\s+(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.OpenWebsite,
                target: target,
                risk: RiskLevel.Medium,
                summary: $"Open website {target}",
                confidence: 0.96);
        }

        if (IsMatch(matchText, "^(read|get|show)( the)?( web ?page| page)( title| text| content)?$") ||
            IsMatch(matchText, "^what is the page( title| text)?$"))
        {
            var wantsTitle = matchText.Contains("title", StringComparison.OrdinalIgnoreCase);
            return Create(
                original,
                commandText,
                IntentKind.ReadWebPage,
                argument: wantsTitle ? "title" : "text",
                summary: wantsTitle ? "Read the current page title" : "Read the current page text",
                confidence: 0.95);
        }

        match = Match(commandText, "^browser click\\s+(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.BrowserClickElement,
                target: target,
                risk: RiskLevel.Medium,
                summary: $"Click '{target}' in the browser",
                confidence: 0.94);
        }

        match = Match(commandText, "^browser fill\\s+(?<target>.+?)\\s+with\\s+(?<text>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.BrowserFillField,
                target: target,
                argument: match.Groups["text"].Value.Trim(),
                risk: RiskLevel.Medium,
                summary: $"Fill the '{target}' field in the browser",
                confidence: 0.94);
        }

        match = Match(commandText, "^browser download\\s+(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            return Create(
                original,
                commandText,
                IntentKind.BrowserDownload,
                target: target,
                risk: RiskLevel.Medium,
                summary: $"Download via '{target}' in the browser",
                confidence: 0.94);
        }

        // Free-standing questions go to the on-device brain. This catch-all sits after
        // every specific "what …" command above (time, date, files, window text, page
        // title), so it only sees genuine questions. Deliberately excluded: "can/could
        // you …" phrasing, which is usually a command in disguise — those fall through
        // to Unknown here and are classified by the brain parser instead.
        if (IsMatch(matchText, "^(what|who|whom|whose|when|where|why|which|how|tell me about|explain|define|describe)\\b.*$"))
        {
            return Create(
                original,
                commandText,
                IntentKind.AskLocalBrain,
                target: commandText,
                summary: "Answer with the on-device brain",
                confidence: 0.9);
        }

        match = Match(commandText, "^(?:open|launch|start)\\s+(?:the\\s+)?(?<target>.+)$");
        if (match.Success)
        {
            var target = CleanTarget(match.Groups["target"].Value);
            if (LooksLikeUrl(target))
            {
                return Create(
                    original,
                    commandText,
                    IntentKind.OpenWebsite,
                    target: target,
                    risk: RiskLevel.Medium,
                    summary: $"Open website {target}",
                    confidence: 0.94);
            }

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

            var folderPath = KnownFolderPath(target);
            if (folderPath is not null)
            {
                return Create(
                    original,
                    commandText,
                    IntentKind.OpenPath,
                    target: folderPath,
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

    private static string TruncateForSummary(string value, int maxLength = 60)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
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

    private static readonly HashSet<string> WebTopLevelDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "io", "dev", "ai", "app", "co", "edu", "gov",
        "info", "me", "us", "uk", "de", "jp", "in", "au", "ca", "xyz", "online", "site"
    };

    private static bool LooksLikeUrl(string value)
    {
        var result = PathSafety.Unquote(value.Trim());
        if (result.Contains(' '))
        {
            return false;
        }

        if (result.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            result.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            result.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Bare domain such as example.com or learn.microsoft.com/docs — but not a
        // local file name like notes.txt (txt is not a web TLD in the list above).
        var host = result.Split('/', '?', '#')[0];
        var lastDot = host.LastIndexOf('.');
        return lastDot > 0 &&
               lastDot < host.Length - 1 &&
               WebTopLevelDomains.Contains(host[(lastDot + 1)..]);
    }

    /// <summary>
    /// Friendly folder names ("open downloads", "open my documents") resolve to the
    /// user's real shell folders so they open in File Explorer instead of being
    /// treated as application names.
    /// </summary>
    private static string? KnownFolderPath(string target)
    {
        var name = WakeWordDetector.NormalizeWhitespace(target).Trim().ToLowerInvariant();
        if (name.EndsWith(" folder", StringComparison.Ordinal))
        {
            name = name[..^" folder".Length].Trim();
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = name switch
        {
            "documents" or "my documents" =>
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => string.IsNullOrWhiteSpace(userProfile)
                ? string.Empty
                : Path.Combine(userProfile, "Downloads"),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "pictures" or "my pictures" or "photos" =>
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" or "my music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" or "my videos" or "movies" =>
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "home" or "user folder" or "my folder" => userProfile,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static bool LooksLikePath(string value)
    {
        var result = PathSafety.Unquote(value);
        return Regex.IsMatch(result, "^(?:[a-zA-Z]:\\\\|\\\\\\\\|\\.\\.\\\\|\\.\\\\|%[^%]+%)") ||
               result.Contains('\\') ||
               result.Contains('/') && Regex.IsMatch(result, "\\.[a-zA-Z0-9]{1,10}$");
    }
}
