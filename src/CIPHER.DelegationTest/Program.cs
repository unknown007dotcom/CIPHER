using System.Net;
using System.Net.Sockets;
using System.Text;
using CIPHER.Core;
using CIPHER.Delegation;

namespace CIPHER.DelegationTest;

/// <summary>
/// Stage 8 acceptance harness. A loopback fake AI endpoint (OpenAI-compatible, canned
/// answers — no external network, no real key) verifies the full delegation path through
/// the real parser → policy → confirmation → executor pipeline:
///
/// 1. An approved “ask ai” returns the canned answer, saves a read-back-verified artifact.
/// 2. A reply containing “COMMAND: …” surfaces the suggestion WITHOUT executing it.
/// 3. Denying the confirmation dialog means the endpoint is never called.
/// 4. Delegation disabled / missing key fail closed before any HTTP happens.
///
/// Exit codes: 0 = PASS, 1 = FAIL.
/// </summary>
internal static class Program
{
    private const string CannedAnswer = "Delegation harness answer: the capital of France is Paris.";

    private static int _requestCount;
    private static string _lastPath = string.Empty;

    private static async Task<int> Main()
    {
        var failures = 0;
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"cipher-stage8-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        using var cancellation = new CancellationTokenSource();
        var port = GetFreePort();
        var serverTask = RunFakeAiServerAsync(port, cancellation.Token);

        try
        {
            var baseUrl = $"http://127.0.0.1:{port}/v1";
            var options = new AiDelegationOptions(
                Enabled: true,
                BaseUrl: baseUrl,
                Model: "harness-model",
                ApiKey: null,
                OutputDirectory: outputDirectory);
            var executor = new DelegationActionExecutor(() => options);
            var approveEngine = CreateEngine(executor, approved: true);

            // 1) Approved delegation: answer + verified artifact.
            await RunStepAsync("Approved ask-ai returns the answer and a verified artifact", async () =>
            {
                var result = await approveEngine.ExecuteTextAsync(
                    "ask ai what is the capital of France", CommandContext.Current);
                Require(result.Status == CipherTaskStatus.Succeeded, $"Expected Succeeded, got {result.Status}.");
                Require(result.ActionResult?.Message.Contains(CannedAnswer) == true, "Answer text missing from result.");
                var artifactPath = result.ActionResult?.Data?["artifactPath"];
                Require(!string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath), "Artifact file was not saved.");
                var artifact = await File.ReadAllTextAsync(artifactPath!);
                Require(artifact.Contains(CannedAnswer), "Artifact does not contain the answer.");
                Require(_requestCount == 1 && _lastPath == "/v1/chat/completions",
                    $"Endpoint was not called as expected (count={_requestCount}, path={_lastPath}).");
                return $"artifact {Path.GetFileName(artifactPath)} verified";
            }).ConfigureAwait(false);

            // 2) Suggestion is surfaced, never executed.
            await RunStepAsync("Suggested command is surfaced but not executed", async () =>
            {
                var result = await approveEngine.ExecuteTextAsync(
                    "ask ai suggest a command to check the time", CommandContext.Current);
                Require(result.Status == CipherTaskStatus.Succeeded, $"Expected Succeeded, got {result.Status}.");
                Require(result.ActionResult?.Data?["suggestedCommand"] == "what time is it",
                    "Suggested command was not extracted into the result data.");
                Require(result.ActionResult?.Message.Contains("NOT executed") == true,
                    "Result does not state that the suggestion was not executed.");
                Require(_requestCount == 2, $"Expected 2 endpoint calls, saw {_requestCount}.");
                return "suggestion extracted into result data only";
            }).ConfigureAwait(false);

            // 3) Denied confirmation: the endpoint must never be called.
            await RunStepAsync("Denied confirmation sends nothing", async () =>
            {
                var denyEngine = CreateEngine(executor, approved: false);
                var result = await denyEngine.ExecuteTextAsync("ask ai hello", CommandContext.Current);
                Require(result.Status == CipherTaskStatus.Cancelled, $"Expected Cancelled, got {result.Status}.");
                Require(_requestCount == 2, $"Endpoint was called despite denial (count={_requestCount}).");
                return "no HTTP request was made";
            }).ConfigureAwait(false);

            // 4) Fail-closed configuration checks happen before any HTTP.
            await RunStepAsync("Disabled delegation and missing key fail closed", async () =>
            {
                var disabled = new DelegationActionExecutor(() => options with { Enabled = false });
                var disabledResult = await disabled.ExecuteAsync(Parse("ask ai hello"), CommandContext.Current, CancellationToken.None);
                Require(disabledResult.ErrorCode == "AI_DELEGATION_DISABLED", $"Expected AI_DELEGATION_DISABLED, got {disabledResult.ErrorCode}.");

                var keyless = new DelegationActionExecutor(() => options with
                {
                    BaseUrl = "https://api.example.invalid/v1",
                    ApiKey = null
                });
                var keylessResult = await keyless.ExecuteAsync(Parse("ask ai hello"), CommandContext.Current, CancellationToken.None);
                Require(keylessResult.ErrorCode == "AI_KEY_MISSING", $"Expected AI_KEY_MISSING, got {keylessResult.ErrorCode}.");
                Require(_requestCount == 2, $"Unexpected endpoint call (count={_requestCount}).");
                return "fail-closed before HTTP";
            }).ConfigureAwait(false);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected shutdown path.
            }

            try
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary harness data.
            }
        }

        if (failures > 0)
        {
            Console.WriteLine($"::error title=Stage 8 delegation harness::STAGE8-FAIL {failures} step(s) failed.");
            return 1;
        }

        Console.WriteLine("::notice title=Stage 8 delegation harness::STAGE8-PASS approved delegation answered with a verified artifact; suggestions surfaced without execution; denial/disabled/missing-key all failed closed before HTTP.");
        return 0;

        async Task RunStepAsync(string name, Func<Task<string>> step)
        {
            try
            {
                var detail = await step().ConfigureAwait(false);
                Console.WriteLine($"[PASS] {name}: {detail}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"::error title=Stage 8 delegation harness::{name} failed: {ex.Message}");
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }
    }

    private static TaskEngine CreateEngine(DelegationActionExecutor executor, bool approved)
    {
        return new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new IActionExecutor[] { executor },
            new StaticConfirmationService(approved));
    }

    private static ParsedCommand Parse(string text) => new DeterministicCommandParser().Parse(text);

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task RunFakeAiServerAsync(int port, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _lastPath = context.Request.Url?.AbsolutePath ?? string.Empty;
                Interlocked.Increment(ref _requestCount);

                string requestBody;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                var content = requestBody.Contains("suggest", StringComparison.OrdinalIgnoreCase)
                    ? "Here is a command you can run.\nCOMMAND: what time is it"
                    : CannedAnswer;
                var payload = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" +
                              System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";
                var buffer = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                context.Response.Close();
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            // Listener stopped during shutdown.
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StaticConfirmationService : IConfirmationService
    {
        private readonly bool _approved;

        public StaticConfirmationService(bool approved) => _approved = approved;

        public Task<bool> ConfirmAsync(ParsedCommand command, PolicyDecision decision, CancellationToken cancellationToken)
        {
            return Task.FromResult(_approved);
        }
    }
}
