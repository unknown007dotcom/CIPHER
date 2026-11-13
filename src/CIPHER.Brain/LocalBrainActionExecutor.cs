using CIPHER.Core;

namespace CIPHER.Brain;

/// <summary>
/// Answers questions with the on-device brain (<see cref="IntentKind.AskLocalBrain"/>)
/// and summarizes web-search results for the browser executor. Generation runs on a
/// thread-pool thread; the brain itself serializes calls. When the brain is missing
/// the executor says so plainly instead of failing silently — and suggests the
/// deterministic commands that still work.
/// </summary>
public sealed class LocalBrainActionExecutor : IActionExecutor
{
    private const int MaxAnswerTokens = 96;
    private const int MaxContextCharacters = 1600;

    private readonly Func<ILocalBrain?> _brainProvider;

    public LocalBrainActionExecutor(Func<ILocalBrain?> brainProvider)
    {
        _brainProvider = brainProvider ?? throw new ArgumentNullException(nameof(brainProvider));
    }

    public static string BuildAnswerPrompt(string question)
    {
        return $"Answer the question: {question}";
    }

    public static string BuildSummaryPrompt(string question, IReadOnlyList<string> contextSnippets)
    {
        var context = string.Join("\n", contextSnippets);
        if (context.Length > MaxContextCharacters)
        {
            context = context[..MaxContextCharacters];
        }

        return "Read the search results and answer the question in one or two sentences.\n\n" +
               $"Search results:\n{context}\n\nQuestion: {question}";
    }

    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent == IntentKind.AskLocalBrain;
    }

    public async Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var question = command.Target?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return ActionResult.Fail("No question was specified.", "QUESTION_NOT_SPECIFIED");
        }

        var brain = _brainProvider();
        if (brain is null || !brain.IsAvailable)
        {
            return ActionResult.Fail(
                "The on-device brain is not installed, so I cannot answer questions yet. " +
                "Deterministic commands still work — try 'open Notepad', 'search for …' needs the web, " +
                "or 'help' to list what I can do.",
                "BRAIN_UNAVAILABLE");
        }

        try
        {
            var answer = await Task.Run(
                () => brain.Generate(BuildAnswerPrompt(question), MaxAnswerTokens, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            answer = CleanAnswer(answer);

            if (answer.Length == 0)
            {
                return ActionResult.Ok(
                    "I don't know that one yet. Say 'search for …' and I will look it up on the web, " +
                    "or ask me to open an app or type something.",
                    verified: false,
                    data: new Dictionary<string, string> { ["question"] = question });
            }

            return ActionResult.Ok(
                answer,
                verified: false,
                data: new Dictionary<string, string>
                {
                    ["question"] = question,
                    ["model"] = brain.ModelDescription
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"The local brain could not answer: {ex.Message}", "BRAIN_FAILED");
        }
    }

    /// <summary>
    /// Produces a short spoken summary of search results, or null when the brain is
    /// unavailable or produces nothing usable (callers fall back to reading the top
    /// result verbatim). Never throws for brain failures.
    /// </summary>
    public async Task<string?> SummarizeSearchAsync(
        string question,
        IReadOnlyList<string> contextSnippets,
        CancellationToken cancellationToken)
    {
        var brain = _brainProvider();
        if (brain is null || !brain.IsAvailable || contextSnippets.Count == 0)
        {
            return null;
        }

        try
        {
            var summary = await Task.Run(
                () => brain.Generate(BuildSummaryPrompt(question, contextSnippets), MaxAnswerTokens, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            summary = CleanAnswer(summary);
            return summary.Length == 0 ? null : summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string CleanAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var cleaned = string.Join(" ", answer.Split(
            new[] { ' ', '\r', '\n', '\t' },
            StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Trim();
    }
}
