namespace CIPHER.Core;

/// <summary>A known application that CIPHER can launch by friendly name.</summary>
public sealed record KnownApplication(
    string DisplayName,
    string ProcessName,
    string LaunchTarget,
    bool IsSensitive,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Small built-in application catalog. User-configured applications will be layered on top of
/// this catalog through persistent memory in a later stage.
/// </summary>
public static class ApplicationCatalog
{
    private static readonly KnownApplication[] Applications =
    {
        new("Notepad", "notepad", "notepad.exe", false, new[] { "note pad", "windows notepad" }),
        new("Calculator", "CalculatorApp", "calc.exe", false, new[] { "windows calculator" }),
        new("Paint", "mspaint", "mspaint.exe", false, new[] { "microsoft paint" }),
        new("File Explorer", "explorer", "explorer.exe", false, new[] { "explorer", "files" }),
        new("Command Prompt", "cmd", "cmd.exe", true, new[] { "cmd", "command line" }),
        new("PowerShell", "powershell", "powershell.exe", true, new[] { "windows powershell" }),
        new("Windows Terminal", "WindowsTerminal", "wt.exe", true, new[] { "terminal" }),
        new("Microsoft Edge", "msedge", "msedge.exe", false, new[] { "edge" }),
        new("Google Chrome", "chrome", "chrome.exe", false, new[] { "chrome" }),
        new("Firefox", "firefox", "firefox.exe", false, new[] { "mozilla firefox" }),
        new("Visual Studio Code", "Code", "Code.exe", false, new[] { "vs code", "vscode", "code" }),
        new("Microsoft Word", "WINWORD", "WINWORD.EXE", false, new[] { "word" }),
        new("Microsoft Excel", "EXCEL", "EXCEL.EXE", false, new[] { "excel" }),
        new("Microsoft PowerPoint", "POWERPNT", "POWERPNT.EXE", false, new[] { "powerpoint" }),
        new("Task Manager", "Taskmgr", "taskmgr.exe", true, new[] { "taskmanager" }),
        new("Settings", "", "ms-settings:", false, new[] { "windows settings" }),
        new("Snipping Tool", "SnippingTool", "SnippingTool.exe", false, Array.Empty<string>()),
        new("Registry Editor", "regedit", "regedit.exe", true, new[] { "regedit" })
    };

    public static IReadOnlyList<KnownApplication> All => Applications;

    public static KnownApplication? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Normalize(value);
        return Applications.FirstOrDefault(application =>
            Normalize(application.DisplayName) == normalized ||
            Normalize(application.ProcessName) == normalized ||
            Normalize(application.LaunchTarget) == normalized ||
            application.Aliases.Any(alias => Normalize(alias) == normalized));
    }

    private static string Normalize(string value)
    {
        var result = value.Trim().Trim('"').ToLowerInvariant();
        return result.EndsWith(".exe", StringComparison.Ordinal)
            ? result[..^4]
            : result;
    }
}
