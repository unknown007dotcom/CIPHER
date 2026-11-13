namespace CIPHER.Browser;

/// <summary>Options for a CIPHER-managed browser session.</summary>
public sealed record BrowserSessionOptions
{
    /// <summary>Headless for automated harnesses; headed when the user is watching.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>Directory browser downloads are saved into. Created on first download.</summary>
    public string DownloadsDirectory { get; init; } = CreateDefaultDownloadsDirectory();

    /// <summary>Default timeout for element waits, in milliseconds.</summary>
    public float DefaultTimeoutMilliseconds { get; init; } = 15_000;

    /// <summary>Optional explicit path to a Chromium executable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Directory containing bundled Playwright browsers. Defaults to the
    /// <c>playwright\browsers</c> folder next to the application (the Stage 10 installer
    /// layout) when it exists, so an offline install never depends on
    /// <c>%LocalAppData%\ms-playwright</c>.
    /// </summary>
    public string? BrowsersPath { get; init; } = ResolveDefaultBrowsersPath();

    private static string? ResolveDefaultBrowsersPath()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "playwright", "browsers");
        if (Directory.Exists(bundled))
        {
            return bundled;
        }

        var legacy = Path.Combine(AppContext.BaseDirectory, "browsers");
        return Directory.Exists(legacy) ? legacy : null;
    }

    private static string CreateDefaultDownloadsDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(root, "CIPHER", "Downloads");
    }
}

/// <summary>Information about one open browser tab.</summary>
public sealed record BrowserTabInfo(int Index, string Title, string Url, bool IsActive);

/// <summary>One web search result: title, destination URL, and text snippet.</summary>
public sealed record WebSearchResult(string Title, string Url, string Snippet);

/// <summary>Outcome of a browser click, including whether it navigated.</summary>
public sealed record BrowserClickOutcome(string Target, string UrlBefore, string UrlAfter)
{
    public bool Navigated => !string.Equals(UrlBefore, UrlAfter, StringComparison.Ordinal);
}

/// <summary>Outcome of filling a field, with the value read back from the page.</summary>
public sealed record BrowserFillOutcome(string Target, string Value, string? ReadBack)
{
    public bool Verified =>
        ReadBack is not null &&
        string.Equals(ReadBack, Value, StringComparison.Ordinal);
}

/// <summary>URL normalization shared by commands and the session.</summary>
public static class BrowserUrl
{
    public static string Normalize(string target)
    {
        var value = target.Trim().Trim('"');
        // Only http(s) is supported; local files and other schemes are never navigated
        // to (the policy layer blocks them first — this is defense in depth).
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value.StartsWith("//", StringComparison.Ordinal)
            ? "https:" + value
            : "https://" + value;
    }
}
