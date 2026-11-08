using CIPHER.Core;
using Microsoft.Playwright;

namespace CIPHER.Browser;

/// <summary>
/// Executes CIPHER's browser commands against a lazily started session.
/// The browser is never launched at application startup; it starts on the first
/// browser command and is closed when the executor is disposed.
/// </summary>
public sealed class BrowserActionExecutor : IActionExecutor, IAsyncDisposable
{
    private readonly Func<CipherBrowserSession> _sessionFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CipherBrowserSession? _session;
    private bool _disposed;

    public BrowserActionExecutor(BrowserSessionOptions? options = null)
    {
        var effectiveOptions = options ?? new BrowserSessionOptions();
        _sessionFactory = () => new CipherBrowserSession(effectiveOptions);
    }

    public BrowserActionExecutor(CipherBrowserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _sessionFactory = () => session;
    }

    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent is
            IntentKind.OpenWebsite or
            IntentKind.ReadWebPage or
            IntentKind.BrowserClickElement or
            IntentKind.BrowserFillField or
            IntentKind.BrowserDownload;
    }

    public async Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = GetSession();
            return command.Intent switch
            {
                IntentKind.OpenWebsite => await OpenWebsiteAsync(session, command, cancellationToken).ConfigureAwait(false),
                IntentKind.ReadWebPage => await ReadWebPageAsync(session, command, cancellationToken).ConfigureAwait(false),
                IntentKind.BrowserClickElement => await ClickAsync(session, command, cancellationToken).ConfigureAwait(false),
                IntentKind.BrowserFillField => await FillAsync(session, command, cancellationToken).ConfigureAwait(false),
                IntentKind.BrowserDownload => await DownloadAsync(session, command, cancellationToken).ConfigureAwait(false),
                _ => ActionResult.Fail($"The browser executor does not handle '{command.Intent}'.", "UNSUPPORTED_INTENT")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BrowserElementNotFoundException ex)
        {
            return ActionResult.Fail(ex.Message, "ELEMENT_NOT_FOUND");
        }
        catch (BrowserCredentialFieldException ex)
        {
            return ActionResult.Fail(ex.Message, "CREDENTIAL_FIELD");
        }
        catch (PlaywrightException ex)
        {
            return ActionResult.Fail(DescribePlaywrightError(ex), "BROWSER_ERROR");
        }
        catch (TimeoutException ex)
        {
            return ActionResult.Fail($"The browser operation timed out: {ex.Message}", "BROWSER_TIMEOUT");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }

        _gate.Dispose();
    }

    private CipherBrowserSession GetSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _session ??= _sessionFactory();
    }

    private static async Task<ActionResult> OpenWebsiteAsync(
        CipherBrowserSession session,
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No website address was specified.", "URL_NOT_SPECIFIED");
        }

        var url = BrowserUrl.Normalize(command.Target);
        await session.OpenAsync(url, cancellationToken).ConfigureAwait(false);
        var title = await session.GetTitleAsync(cancellationToken).ConfigureAwait(false);
        var currentUrl = session.CurrentUrl ?? url;
        var verified = currentUrl.StartsWith(url, StringComparison.OrdinalIgnoreCase) ||
                       Uri.TryCreate(currentUrl, UriKind.Absolute, out var current) &&
                       Uri.TryCreate(url, UriKind.Absolute, out var requested) &&
                       string.Equals(current.Host, requested.Host, StringComparison.OrdinalIgnoreCase);

        return ActionResult.Ok(
            $"Opened {currentUrl} (title: {title}).",
            verified: verified,
            data: new Dictionary<string, string>
            {
                ["url"] = currentUrl,
                ["title"] = title
            });
    }

    private static async Task<ActionResult> ReadWebPageAsync(
        CipherBrowserSession session,
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (session.CurrentUrl is null)
        {
            return ActionResult.Fail("No web page is open yet. Open a website first.", "NO_PAGE_OPEN");
        }

        if (string.Equals(command.Argument, "title", StringComparison.OrdinalIgnoreCase))
        {
            var title = await session.GetTitleAsync(cancellationToken).ConfigureAwait(false);
            return ActionResult.Ok(
                $"The page title is: {title}",
                verified: true,
                data: new Dictionary<string, string> { ["title"] = title, ["url"] = session.CurrentUrl });
        }

        var text = await session.GetTextAsync(null, cancellationToken).ConfigureAwait(false);
        var normalized = TextMatching.NormalizeForComparison(text);
        var preview = normalized.Length <= 800 ? normalized : normalized[..800] + "…";
        return ActionResult.Ok(
            $"Page text: {preview}",
            verified: true,
            data: new Dictionary<string, string>
            {
                ["url"] = session.CurrentUrl,
                ["text"] = normalized.Length <= 4_000 ? normalized : normalized[..4_000],
                ["characterCount"] = normalized.Length.ToString()
            });
    }

    private static async Task<ActionResult> ClickAsync(
        CipherBrowserSession session,
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No browser element was specified.", "ELEMENT_NOT_SPECIFIED");
        }

        if (session.CurrentUrl is null)
        {
            return ActionResult.Fail("No web page is open yet. Open a website first.", "NO_PAGE_OPEN");
        }

        var outcome = await session.ClickAsync(command.Target, cancellationToken).ConfigureAwait(false);
        return ActionResult.Ok(
            outcome.Navigated
                ? $"Clicked '{command.Target}' and navigated to {outcome.UrlAfter}."
                : $"Clicked '{command.Target}'.",
            verified: outcome.Navigated,
            data: new Dictionary<string, string>
            {
                ["urlBefore"] = outcome.UrlBefore,
                ["urlAfter"] = outcome.UrlAfter
            });
    }

    private static async Task<ActionResult> FillAsync(
        CipherBrowserSession session,
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No browser field was specified.", "ELEMENT_NOT_SPECIFIED");
        }

        if (command.Argument is null)
        {
            return ActionResult.Fail("No text was specified.", "TEXT_NOT_SPECIFIED");
        }

        if (session.CurrentUrl is null)
        {
            return ActionResult.Fail("No web page is open yet. Open a website first.", "NO_PAGE_OPEN");
        }

        var outcome = await session.FillAsync(command.Target, command.Argument, cancellationToken).ConfigureAwait(false);
        if (outcome.Verified)
        {
            return ActionResult.Ok(
                $"Filled '{command.Target}' and verified the entered value.",
                verified: true,
                data: new Dictionary<string, string> { ["field"] = command.Target });
        }

        return ActionResult.Ok(
            outcome.ReadBack is null
                ? $"Filled '{command.Target}', but the value could not be read back for verification."
                : $"Filled '{command.Target}', but the read-back value differs from the requested text.",
            verified: false,
            data: new Dictionary<string, string> { ["field"] = command.Target });
    }

    private static async Task<ActionResult> DownloadAsync(
        CipherBrowserSession session,
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No download link was specified.", "ELEMENT_NOT_SPECIFIED");
        }

        if (session.CurrentUrl is null)
        {
            return ActionResult.Fail("No web page is open yet. Open a website first.", "NO_PAGE_OPEN");
        }

        var path = await session.DownloadAsync(command.Target, cancellationToken).ConfigureAwait(false);
        var exists = File.Exists(path);
        var length = exists ? new FileInfo(path).Length : 0;
        var extension = Path.GetExtension(path);
        var caution = extension is ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1"
            ? " This is an executable file; CIPHER saved it but will not run it."
            : string.Empty;

        return ActionResult.Ok(
            $"Downloaded to {path} ({length} bytes).{caution}",
            verified: exists,
            data: new Dictionary<string, string>
            {
                ["path"] = path,
                ["bytes"] = length.ToString()
            });
    }

    private static string DescribePlaywrightError(PlaywrightException exception)
    {
        if (exception.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Failed to launch", StringComparison.OrdinalIgnoreCase))
        {
            return "The browser engine is not installed for CIPHER yet. " +
                   "Run the Playwright Chromium install step from the development setup (the offline installer will bundle it in Stage 10). " +
                   $"Details: {exception.Message}";
        }

        return $"The browser operation failed: {exception.Message}";
    }
}
