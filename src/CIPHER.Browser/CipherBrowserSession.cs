using Microsoft.Playwright;

namespace CIPHER.Browser;

/// <summary>
/// A lazily started Chromium session managed by CIPHER. The browser engine is only
/// launched on first use, so the assistant's idle footprint does not include it.
/// Element interaction prefers accessible roles/labels/text (DOM-level), never
/// screenshots. Downloads are saved locally and never executed.
/// </summary>
public sealed class CipherBrowserSession : IAsyncDisposable
{
    private readonly BrowserSessionOptions _options;
    private readonly List<IPage> _pages = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _activePage;
    private bool _disposed;

    public CipherBrowserSession(BrowserSessionOptions? options = null)
    {
        _options = options ?? new BrowserSessionOptions();
    }

    public BrowserSessionOptions Options => _options;

    public string? CurrentUrl => _activePage?.Url;

    public async Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        await page.GotoAsync(
            BrowserUrl.Normalize(url),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded })
            .ConfigureAwait(false);
    }

    public async Task<string> GetTitleAsync(CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        return await page.TitleAsync().ConfigureAwait(false);
    }

    public async Task<string> GetTextAsync(string? selector = null, CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return await page.Locator("body").InnerTextAsync().ConfigureAwait(false);
        }

        var locator = IsSelectorLike(selector)
            ? page.Locator(selector)
            : page.GetByText(selector).First;
        return await locator.InnerTextAsync().ConfigureAwait(false);
    }

    public async Task<BrowserClickOutcome> ClickAsync(string target, CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        var urlBefore = page.Url;

        var locator = await ResolveClickableAsync(page, target).ConfigureAwait(false)
                      ?? throw new BrowserElementNotFoundException(target);
        await locator.ClickAsync().ConfigureAwait(false);

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 5_000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The click may not trigger a navigation; that is not an error.
        }

        return new BrowserClickOutcome(target, urlBefore, page.Url);
    }

    public async Task<BrowserFillOutcome> FillAsync(string target, string value, CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = await ResolveFieldAsync(page, target).ConfigureAwait(false)
                      ?? throw new BrowserElementNotFoundException(target);

        await locator.FillAsync(value).ConfigureAwait(false);

        string? readBack;
        try
        {
            readBack = await locator.InputValueAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            try
            {
                readBack = await locator.InnerTextAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                readBack = null;
            }
        }

        return new BrowserFillOutcome(target, value, readBack);
    }

    public async Task<string> DownloadAsync(string target, CancellationToken cancellationToken = default)
    {
        var page = await RequirePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = await ResolveClickableAsync(page, target).ConfigureAwait(false)
                      ?? throw new BrowserElementNotFoundException(target);

        var download = await page.RunAndWaitForDownloadAsync(
            async () => await locator.ClickAsync().ConfigureAwait(false))
            .ConfigureAwait(false);

        Directory.CreateDirectory(_options.DownloadsDirectory);
        var fileName = SanitizeFileName(download.SuggestedFilename);
        var destination = GetUniquePath(Path.Combine(_options.DownloadsDirectory, fileName));
        await download.SaveAsAsync(destination).ConfigureAwait(false);
        return destination;
    }

    public async Task<IPage> NewTabAsync(string? url = null, CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var page = await _context!.NewPageAsync().ConfigureAwait(false);
        RegisterPage(page);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await page.GotoAsync(
                BrowserUrl.Normalize(url),
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded })
                .ConfigureAwait(false);
        }

        _activePage = page;
        return page;
    }

    public async Task<IReadOnlyList<BrowserTabInfo>> ListTabsAsync()
    {
        var livePages = _pages.Where(page => !page.IsClosed).ToList();
        var tabs = new List<BrowserTabInfo>();
        for (var index = 0; index < livePages.Count; index++)
        {
            var page = livePages[index];
            string title;
            try
            {
                title = await page.TitleAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                title = string.Empty;
            }

            tabs.Add(new BrowserTabInfo(index, title, page.Url, ReferenceEquals(page, _activePage)));
        }

        return tabs;
    }

    public async Task SwitchToTabAsync(int index, CancellationToken cancellationToken = default)
    {
        var livePages = _pages.Where(page => !page.IsClosed).ToList();
        if (index < 0 || index >= livePages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Only {livePages.Count} tab(s) are open.");
        }

        _activePage = livePages[index];
        await _activePage.BringToFrontAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_context is not null)
            {
                await _context.CloseAsync().ConfigureAwait(false);
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (PlaywrightException)
        {
            // The browser may already have exited.
        }
        finally
        {
            _playwright?.Dispose();
            _context = null;
            _browser = null;
            _playwright = null;
            _pages.Clear();
            _activePage = null;
        }
    }

    private async Task<IPage> RequirePageAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (_activePage is not null && !_activePage.IsClosed)
        {
            return _activePage;
        }

        var page = await _context!.NewPageAsync().ConfigureAwait(false);
        RegisterPage(page);
        _activePage = page;
        return page;
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context is not null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless
        }).ConfigureAwait(false);
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true
        }).ConfigureAwait(false);
    }

    private void RegisterPage(IPage page)
    {
        _pages.Add(page);
        page.SetDefaultTimeout(_options.DefaultTimeoutMilliseconds);
        page.Close += (_, _) =>
        {
            if (ReferenceEquals(_activePage, page))
            {
                _activePage = _pages.LastOrDefault(candidate => !candidate.IsClosed && !ReferenceEquals(candidate, page));
            }
        };
    }

    private async Task<ILocator?> ResolveClickableAsync(IPage page, string target)
    {
        var candidates = IsSelectorLike(target)
            ? new[] { page.Locator(target) }
            : new[]
            {
                page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = target }),
                page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = target }),
                page.GetByText(target, new PageGetByTextOptions { Exact = false }).First,
                page.Locator(target)
            };

        return await FirstUsableAsync(candidates).ConfigureAwait(false);
    }

    private async Task<ILocator?> ResolveFieldAsync(IPage page, string target)
    {
        var candidates = IsSelectorLike(target)
            ? new[] { page.Locator(target) }
            : new[]
            {
                page.GetByLabel(target, new PageGetByLabelOptions { Exact = false }).First,
                page.GetByPlaceholder(target, new PageGetByPlaceholderOptions { Exact = false }).First,
                page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = target }),
                page.Locator(target)
            };

        return await FirstUsableAsync(candidates).ConfigureAwait(false);
    }

    private static async Task<ILocator?> FirstUsableAsync(IEnumerable<ILocator> candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                if (await candidate.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                var first = candidate.First;
                return first;
            }
            catch (PlaywrightException)
            {
                // An invalid selector strategy simply means "try the next one".
            }
        }

        return null;
    }

    private static bool IsSelectorLike(string target)
    {
        var value = target.Trim();
        return value.StartsWith("#", StringComparison.Ordinal) ||
               value.StartsWith(".", StringComparison.Ordinal) ||
               value.StartsWith("[", StringComparison.Ordinal) ||
               value.StartsWith("text=", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("css=", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("//", StringComparison.Ordinal) ||
               value.Contains(">>", StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length == 0 ? "download" : name;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 100; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }
}

/// <summary>Thrown when no locator strategy finds the requested page element.</summary>
public sealed class BrowserElementNotFoundException : Exception
{
    public BrowserElementNotFoundException(string target)
        : base($"No browser element matching '{target}' was found on the current page.")
    {
        Target = target;
    }

    public string Target { get; }
}
