using System.Text.RegularExpressions;

namespace CIPHER.Core;

/// <summary>Deterministic management commands for aliases and learned application paths.</summary>
public enum MemoryCommandKind
{
    None,
    DefineAlias,
    DeleteAlias,
    ListAliases,
    DefineAppPath,
    DeleteAppPath,
    ListAppPaths
}

/// <summary>A parsed memory-management command (Name/Value are empty for list commands).</summary>
public readonly record struct MemoryCommand(MemoryCommandKind Kind, string Name, string Value);

/// <summary>
/// Grammar for teaching CIPHER: “alias morning = open notepad”, “create alias … for …”,
/// “forget alias …”, “list aliases”, “remember app … at …”, “list apps”. Name matching is
/// case-insensitive and whitespace-insensitive.
/// </summary>
public static class MemoryCommandText
{
    private static readonly Regex DefineAliasPattern = new(
        @"^(?:alias|create\s+alias|remember\s+alias)\s+(?<name>.+?)\s*(?:=|\bfor\b)\s*(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DeleteAliasPattern = new(
        @"^(?:delete|remove|forget)\s+alias\s+(?<name>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ListAliasesPattern = new(
        @"^(?:list|show)\s+aliases$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DefineAppPathPattern = new(
        @"^(?:remember|teach)\s+(?:app|application)\s+(?<name>.+?)\s+at\s+(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DeleteAppPathPattern = new(
        @"^forget\s+(?:app|application)\s+(?<name>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ListAppPathsPattern = new(
        @"^(?:list|show)\s+(?:learned\s+)?(?:apps|applications)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Canonical lookup form: trimmed, single-spaced, lower-invariant.</summary>
    public static string NormalizeName(string? name)
    {
        return WakeWordDetector.NormalizeWhitespace(name).ToLowerInvariant();
    }

    public static bool TryParse(string? input, out MemoryCommand command)
    {
        command = new MemoryCommand(MemoryCommandKind.None, string.Empty, string.Empty);
        var text = WakeWordDetector.NormalizeWhitespace(input).TrimEnd('.', '!');
        if (text.Length == 0)
        {
            return false;
        }

        var match = DefineAliasPattern.Match(text);
        if (match.Success)
        {
            command = new MemoryCommand(
                MemoryCommandKind.DefineAlias,
                match.Groups["name"].Value.Trim(),
                match.Groups["value"].Value.Trim());
            return command.Name.Length > 0 && command.Value.Length > 0;
        }

        match = DefineAppPathPattern.Match(text);
        if (match.Success)
        {
            command = new MemoryCommand(
                MemoryCommandKind.DefineAppPath,
                match.Groups["name"].Value.Trim(),
                PathSafetyUnquote(match.Groups["value"].Value.Trim()));
            return command.Name.Length > 0 && command.Value.Length > 0;
        }

        match = DeleteAliasPattern.Match(text);
        if (match.Success)
        {
            command = new MemoryCommand(MemoryCommandKind.DeleteAlias, match.Groups["name"].Value.Trim(), string.Empty);
            return command.Name.Length > 0;
        }

        match = DeleteAppPathPattern.Match(text);
        if (match.Success)
        {
            command = new MemoryCommand(MemoryCommandKind.DeleteAppPath, match.Groups["name"].Value.Trim(), string.Empty);
            return command.Name.Length > 0;
        }

        if (ListAliasesPattern.IsMatch(text))
        {
            command = new MemoryCommand(MemoryCommandKind.ListAliases, string.Empty, string.Empty);
            return true;
        }

        if (ListAppPathsPattern.IsMatch(text))
        {
            command = new MemoryCommand(MemoryCommandKind.ListAppPaths, string.Empty, string.Empty);
            return true;
        }

        return false;
    }

    private static string PathSafetyUnquote(string value)
    {
        return value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"')
            ? value[1..^1]
            : value;
    }
}
