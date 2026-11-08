using System.Globalization;
using CIPHER.Core;

namespace CIPHER.Delegation;

/// <summary>
/// Executes <see cref="IntentKind.AskExternalAi"/> commands. Guardrails, in order:
///
/// 1. Delegation must be enabled in settings (off by default) — the policy layer has
///    already shown the exact prompt and received explicit approval before this runs.
/// 2. Remote endpoints require an API key (loaded from the DPAPI store by the app);
///    loopback endpoints (local model servers) may run keyless.
/// 3. The reply is saved to a local artifact file and read back; only a read-back match
///    is reported as verified.
/// 4. If the reply proposes a CIPHER command (a line starting with “COMMAND:”), it is
///    surfaced in the result data for the user to review and send manually. It is NEVER
///    executed automatically.
/// </summary>
public sealed class DelegationActionExecutor : IActionExecutor
{
    internal const string SystemPrompt =
        "You are assisting CIPHER, an offline-first desktop assistant for Windows. " +
        "Answer concisely in plain text. If the user asks for a desktop action, reply with one " +
        "suggested command on its own line prefixed with 'COMMAND: ' using CIPHER command syntax " +
        "(for example 'COMMAND: open notepad'). CIPHER shows suggestions to the user for approval " +
        "and never runs them automatically. Never claim you executed anything yourself.";

    private readonly Func<AiDelegationOptions> _optionsProvider;
    private readonly OpenAiChatClient _client;

    public DelegationActionExecutor(Func<AiDelegationOptions> optionsProvider, OpenAiChatClient? client = null)
    {
        _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
        _client = client ?? new OpenAiChatClient();
    }

    public bool CanExecute(ParsedCommand command) => command.Intent == IntentKind.AskExternalAi;

    public async Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var prompt = command.Target?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            return ActionResult.Fail("No question or task was provided for the external AI.", "AI_PROMPT_MISSING");
        }

        var options = _optionsProvider();
        if (!options.Enabled)
        {
            return ActionResult.Fail(
                "External AI delegation is turned off. CIPHER's core works fully offline; " +
                "to delegate, enable 'External AI delegation' in Settings and save an API key there.",
                "AI_DELEGATION_DISABLED");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return ActionResult.Fail(
                $"The configured AI endpoint '{options.BaseUrl}' is not a valid http(s) URL. Fix it in Settings.",
                "AI_ENDPOINT_INVALID");
        }

        if (!options.IsLoopbackEndpoint && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ActionResult.Fail(
                "No API key is saved for the external AI service. Open Settings and save your key — " +
                "it is stored encrypted with Windows DPAPI and never in plain text. " +
                "(Tip: never type a key into the command box; typed commands are journaled.)",
                "AI_KEY_MISSING");
        }

        string answer;
        try
        {
            answer = await _client.GetChatCompletionAsync(
                options.BaseUrl,
                options.Model,
                options.ApiKey,
                SystemPrompt,
                prompt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AiDelegationException ex)
        {
            return ActionResult.Fail($"The external AI request failed: {ex.Message}", "AI_REQUEST_FAILED");
        }

        var suggestion = ExtractSuggestedCommand(answer);
        var artifactPath = await SaveArtifactAsync(options, endpointUri, prompt, answer, cancellationToken)
            .ConfigureAwait(false);

        var data = new Dictionary<string, string>();
        var message = answer;
        if (artifactPath is not null)
        {
            data["artifactPath"] = artifactPath;
            message += $"\n\nSaved to {artifactPath} (read-back verified).";
        }
        else
        {
            message += "\n\n(Note: the answer could not be saved to a local artifact file.)";
        }

        if (suggestion is not null)
        {
            data["suggestedCommand"] = suggestion;
            message += $"\nSuggested command placed in the input box for your review — it was NOT executed: {suggestion}";
        }

        return ActionResult.Ok(message, verified: artifactPath is not null, data: data);
    }

    /// <summary>Extracts a “COMMAND: …” proposal line, if the reply contains one.</summary>
    internal static string? ExtractSuggestedCommand(string answer)
    {
        foreach (var rawLine in answer.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('-', '*', '>');
            if (line.StartsWith("COMMAND:", StringComparison.OrdinalIgnoreCase))
            {
                var suggestion = line["COMMAND:".Length..].Trim().Trim('`', '"');
                return suggestion.Length == 0 ? null : suggestion;
            }
        }

        return null;
    }

    private static async Task<string?> SaveArtifactAsync(
        AiDelegationOptions options,
        Uri endpointUri,
        string prompt,
        string answer,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(Path.GetTempPath(), "CIPHER", "Results")
                : options.OutputDirectory;
            Directory.CreateDirectory(directory);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"ai-delegation-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.txt";
            var path = Path.Combine(directory, fileName);
            var content =
                "CIPHER external AI delegation\n" +
                $"Endpoint host: {endpointUri.Host}\n" +
                $"Model: {options.Model}\n" +
                $"Time (UTC): {DateTimeOffset.UtcNow:O}\n\n" +
                $"Prompt:\n{prompt}\n\nAnswer:\n{answer}\n";

            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            var readBack = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return readBack.Contains(answer, StringComparison.Ordinal) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
