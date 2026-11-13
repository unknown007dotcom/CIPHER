using CIPHER.Core;
using Microsoft.Playwright;

namespace CIPHER.Browser;

/// <summary>
/// Executes <see cref="IntentKind.WebSearch"/>: runs the query against a web search
/// engine in a headless bundled-Chromium session, then speaks/shows either a short
/// summary produced by the on-device brain (when a summarizer is supplied) or the top
/// result verbatim. Search is read-only — result links are never clicked and nothing
/// is downloaded by this executor.
/// </summary>
public sealed class WebSearchExecutor : IActionExecutor, IAsyncDisposable
{
    private readonly Func<CipherBrowserSession> _sessionFactory;
    private readonly Func<string, IReadOnlyList<WebSearchResult>, CancellationToken, Task<string?>>? _summarizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CipherBrowserSession? _session;
    private bool _disposed;

    public WebSearchExecutor(
        BrowserSessionOptions? options = null,
        Func<string, IReadOnlyList<WebSearchResult>, CancellationToken, Task<string?>>? summarizer = null)
    {
        // Search always runs headless: the user asked a question, not for a window.
        var effectiveOptions = (options ?? new BrowserSessionOptions()) with { Headless = true };
        _sessionFactory = () => new CipherBrowserSession(effectiveOptions);
        _summarizer = summarizer;
    }

    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent == IntentKind.WebSearch;
    }

    public async Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var query = command.Target?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ActionResult.Fail("No search query was specified.", "QUERY_NOT_SPECIFIED");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = _session ??= _sessionFactory();

            IReadOnlyList<WebSearchResult> results;
            try
            {
                results = await session.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                return ActionResult.Fail($"The web search failed: {ex.Message}", "BROWSER_ERROR");
            }
            catch (TimeoutException ex)
            {
                return ActionResult.Fail($"The web search timed out: {ex.Message}", "BROWSER_TIMEOUT");
            }

            if (results.Count == 0)
            {
                return ActionResult.Fail(
                    $"I searched for '{query}' but found no readable results. " +
                    "Check the internet connection and try again.",
                    "WEB_SEARCH_NO_RESULTS");
            }

            string? summary = null;
            if (_summarizer is not null)
            {
                summary = await _summarizer(query, results, cancellationToken).ConfigureAwait(false);
            }

            var top = results[0];
            var message = !string.IsNullOrWhiteSpace(summary)
                ? summary!
                : $"Top result for '{query}': {top.Title}. {top.Snippet}".Trim();

            return ActionResult.Ok(
                message,
                verified: true,
                data: new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["resultCount"] = results.Count.ToString(),
                    ["topUrl"] = top.Url,
                    ["results"] = string.Join(
                        "\n",
                        results.Select(result => $"{result.Title} | {result.Url} | {result.Snippet}"))
                });
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
}
