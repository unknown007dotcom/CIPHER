using System.Net;
using System.Net.Sockets;
using System.Text;
using CIPHER.Browser;
using CIPHER.Core;

namespace CIPHER.BrowserTest;

/// <summary>
/// Stage 5 browser-automation harness. It serves deterministic test pages from a
/// loopback HTTP listener (no external network, no real accounts) and then drives
/// them through CIPHER's real command path: parser → policy → task engine →
/// BrowserActionExecutor → Playwright Chromium.
///
/// Scenarios: open page, fill fields with read-back verification, submit and read
/// the confirmation, multi-tab handling, verified file download, and policy
/// blocking (credential fields and non-http schemes).
///
/// Exit code 0 means every scored step passed.
/// </summary>
internal static class Program
{
    private const string DownloadContent = "CIPHER browser download verification file.";
    private static readonly List<(string Name, bool Passed)> Steps = new();

    private const string FormPage = """
        <!doctype html>
        <html>
        <head><title>CIPHER Browser Test Form</title></head>
        <body>
          <h1>Registration</h1>
          <form id="regform">
            <label for="name">Name</label>
            <input id="name" name="name" type="text" />
            <label for="email">Email</label>
            <input id="email" name="email" type="email" />
            <label for="nickname">Nickname</label>
            <input id="nickname" name="nickname" type="password" />
            <button id="submit" type="submit">Submit registration</button>
          </form>
          <div id="confirmation" role="status"></div>
          <script>
            document.getElementById('regform').addEventListener('submit', function (e) {
              e.preventDefault();
              var n = document.getElementById('name').value;
              var em = document.getElementById('email').value;
              document.getElementById('confirmation').textContent =
                'Thanks, ' + n + '! Registration recorded for ' + em + '.';
            });
          </script>
        </body>
        </html>
        """;

    private const string DetailsPage = """
        <!doctype html>
        <html>
        <head><title>CIPHER Test Details</title></head>
        <body>
          <h1>Details page</h1>
          <p>This is the second tab used by the CIPHER browser harness.</p>
        </body>
        </html>
        """;

    private const string DownloadPage = """
        <!doctype html>
        <html>
        <head><title>CIPHER Test Download</title></head>
        <body>
          <h1>Download</h1>
          <a id="download-link" href="/hello.txt" download="hello.txt">Download hello.txt</a>
        </body>
        </html>
        """;

    private static async Task<int> Main()
    {
        Console.WriteLine("CIPHER Stage 5 browser automation harness");
        Console.WriteLine();

        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(() => ServeAsync(listener, serverCts.Token));

        var downloadsDirectory = Path.Combine(
            Path.GetTempPath(),
            "CIPHER-BrowserTest-" + Guid.NewGuid().ToString("N"));
        var session = new CipherBrowserSession(new BrowserSessionOptions
        {
            Headless = true,
            DownloadsDirectory = downloadsDirectory
        });
        var browserExecutor = new BrowserActionExecutor(session);
        var engine = new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new IActionExecutor[] { new BuiltInActionExecutor(), browserExecutor });

        try
        {
            // 1. Open the form page.
            var open = await engine.ExecuteTextAsync($"open website {baseUrl}/form");
            Report("Open test form page",
                open.Status == CipherTaskStatus.Succeeded,
                open.ActionResult?.Message ?? open.Policy.Reason);

            // 2-3. Fill fields; the executor verifies by reading values back.
            var fillName = await engine.ExecuteTextAsync("browser fill Name with Ada Lovelace");
            Report("Fill Name field",
                fillName.Status == CipherTaskStatus.Succeeded,
                fillName.ActionResult?.Message ?? fillName.Policy.Reason);

            var fillEmail = await engine.ExecuteTextAsync("browser fill Email with ada@example.com");
            Report("Fill Email field",
                fillEmail.Status == CipherTaskStatus.Succeeded,
                fillEmail.ActionResult?.Message ?? fillEmail.Policy.Reason);

            // 4. Submit the form.
            var submit = await engine.ExecuteTextAsync("browser click Submit registration");
            Report("Click Submit registration",
                submit.ActionResult?.Success == true,
                submit.ActionResult?.Message ?? submit.Policy.Reason);
            await Task.Delay(300);

            // 5. Read the page and confirm the submission text is present.
            var read = await engine.ExecuteTextAsync("read page text");
            var pageText = read.ActionResult?.Data?.TryGetValue("text", out var text) == true ? text : string.Empty;
            Report("Read confirmation text",
                read.Status == CipherTaskStatus.Succeeded &&
                pageText.Contains("Thanks, Ada Lovelace", StringComparison.Ordinal) &&
                pageText.Contains("ada@example.com", StringComparison.Ordinal),
                $"Page text begins: {Truncate(pageText, 160)}");

            // 6. Tabs: open details in a new tab, then switch back.
            await session.NewTabAsync($"{baseUrl}/details");
            var tabs = await session.ListTabsAsync();
            var detailsTitle = await session.GetTitleAsync();
            await session.SwitchToTabAsync(0);
            var backTitle = await session.GetTitleAsync();
            Report("Multi-tab open and switch back",
                tabs.Count == 2 &&
                detailsTitle == "CIPHER Test Details" &&
                backTitle == "CIPHER Browser Test Form",
                $"Tabs: {tabs.Count}; details title: '{detailsTitle}'; back title: '{backTitle}'.");

            // 7. Download a file and verify its contents.
            await session.OpenAsync($"{baseUrl}/download");
            var download = await engine.ExecuteTextAsync("browser download Download hello.txt");
            var downloadPath = download.ActionResult?.Data?.TryGetValue("path", out var path) == true ? path : null;
            var downloadContentOk = downloadPath is not null &&
                                      File.Exists(downloadPath) &&
                                      (await File.ReadAllTextAsync(downloadPath)).Contains(DownloadContent, StringComparison.Ordinal);
            Report("Download file and verify contents",
                download.Status == CipherTaskStatus.Succeeded && downloadContentOk,
                download.ActionResult?.Message ?? download.Policy.Reason);

            // 8. Policy: credential fields and non-http schemes are blocked.
            var fillPassword = await engine.ExecuteTextAsync("browser fill Password with hunter2");
            Report("Policy blocks password fill",
                fillPassword.Status == CipherTaskStatus.Blocked,
                fillPassword.ActionResult?.Message ?? fillPassword.Policy.Reason);

            // The 'Nickname' input is type=password under an innocent label: the name-based
            // policy lets it through, so the executor's DOM-level guard must refuse it.
            await engine.ExecuteTextAsync($"open website {baseUrl}/form");
            var fillNickname = await engine.ExecuteTextAsync("browser fill Nickname with hunter2");
            Report("Executor refuses password-typed field",
                fillNickname.Status == CipherTaskStatus.Failed &&
                (fillNickname.ActionResult?.Message.Contains("password field") ?? false),
                fillNickname.ActionResult?.Message ?? fillNickname.Policy.Reason);

            var openFtp = await engine.ExecuteTextAsync("open website ftp://example.com/file");
            Report("Policy blocks non-http scheme",
                openFtp.Status == CipherTaskStatus.Blocked,
                openFtp.ActionResult?.Message ?? openFtp.Policy.Reason);

            return Finish();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=CIPHER Stage 5 browser automation::Harness exception: {ex.Message}");
            Console.WriteLine($"STAGE5 RESULT: FAIL (exception: {ex.GetType().Name}: {ex.Message})");
            return 1;
        }
        finally
        {
            await browserExecutor.DisposeAsync();
            serverCts.Cancel();
            listener.Stop();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            try
            {
                if (Directory.Exists(downloadsDirectory))
                {
                    Directory.Delete(downloadsDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporary-directory cleanup is best effort.
            }
        }
    }

    private static async Task ServeAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            try
            {
                Respond(context);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException)
            {
                // The client went away mid-response.
            }
        }
    }

    private static void Respond(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        string body;
        string contentType;
        switch (path)
        {
            case "/form":
                body = FormPage;
                contentType = "text/html; charset=utf-8";
                break;
            case "/details":
                body = DetailsPage;
                contentType = "text/html; charset=utf-8";
                break;
            case "/download":
                body = DownloadPage;
                contentType = "text/html; charset=utf-8";
                break;
            case "/hello.txt":
                body = DownloadContent;
                contentType = "text/plain; charset=utf-8";
                context.Response.Headers["Content-Disposition"] = "attachment; filename=\"hello.txt\"";
                break;
            default:
                context.Response.StatusCode = 404;
                body = "Not found";
                contentType = "text/plain; charset=utf-8";
                break;
        }

        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }

    private static void Report(string name, bool passed, string details)
    {
        Steps.Add((name, passed));
        Console.WriteLine($"STEP: {name} => {(passed ? "PASS" : "FAIL")} - {details}");
        if (!passed)
        {
            var escaped = details.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
            Console.WriteLine($"::error title=CIPHER harness step '{name}'::{escaped}");
        }
    }

    private static int Finish()
    {
        var passedCount = Steps.Count(step => step.Passed);
        var allPassed = Steps.Count > 0 && passedCount == Steps.Count;
        Console.WriteLine();
        Console.WriteLine($"STAGE5 RESULT: {(allPassed ? "PASS" : "FAIL")} ({passedCount}/{Steps.Count} steps)");

        var failedNames = string.Join(", ", Steps.Where(step => !step.Passed).Select(step => step.Name));
        Console.WriteLine(allPassed
            ? "::notice title=CIPHER Stage 5 browser automation::PASS - navigation, verified form fill, submit, read-back, tabs, download, and policy blocking all succeeded."
            : $"::error title=CIPHER Stage 5 browser automation::FAIL - failed steps: {failedNames}");

        return allPassed ? 0 : 1;
    }
}
