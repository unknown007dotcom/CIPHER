using System.Diagnostics;

namespace CIPHER.Core;

/// <summary>
/// Implements CIPHER's UNDERSTAND → VALIDATE → EXECUTE → VERIFY task flow for v1 commands.
/// </summary>
public sealed class TaskEngine
{
    private readonly ICommandParser _parser;
    private readonly IActionPolicy _policy;
    private readonly IReadOnlyList<IActionExecutor> _executors;
    private readonly IConfirmationService? _confirmationService;

    public TaskEngine(
        ICommandParser parser,
        IActionPolicy policy,
        IEnumerable<IActionExecutor> executors,
        IConfirmationService? confirmationService = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _executors = (executors ?? throw new ArgumentNullException(nameof(executors))).ToList();
        _confirmationService = confirmationService;
    }

    public AssistantState State { get; private set; } = AssistantState.Idle;

    public event EventHandler<AssistantEvent>? EventPublished;

    public async Task<TaskExecutionResult> ExecuteTextAsync(
        string input,
        CommandContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveContext = context ?? CommandContext.Current;
        var stopwatch = Stopwatch.StartNew();

        Publish(AssistantState.Processing, "Understanding command.");

        ParsedCommand command;
        try
        {
            command = _parser.Parse(input, effectiveContext);
        }
        catch (Exception ex)
        {
            command = ParsedCommand.Unknown(input, string.Empty, "The command parser failed.");
            var parseFailure = ActionResult.Fail($"The command could not be parsed: {ex.Message}", "PARSER_FAILED");
            Publish(AssistantState.Failed, parseFailure.Message, command);
            return new TaskExecutionResult(
                command,
                new PolicyDecision(false, false, RiskLevel.Low, parseFailure.Message),
                parseFailure,
                TaskStatus.Failed,
                stopwatch.Elapsed);
        }

        Publish(AssistantState.Validating, $"Validating: {GetIntentDescription(command)}", command);
        var policy = _policy.Evaluate(command, effectiveContext);

        if (!policy.IsAllowed)
        {
            var blocked = ActionResult.Fail(policy.Reason, "POLICY_BLOCKED");
            Publish(AssistantState.Failed, policy.Reason, command);
            return new TaskExecutionResult(command, policy, blocked, TaskStatus.Blocked, stopwatch.Elapsed);
        }

        if (policy.RequiresConfirmation)
        {
            Publish(AssistantState.WaitingForConfirmation, "Waiting for user confirmation.", command);

            if (_confirmationService is null)
            {
                var unavailable = ActionResult.Fail(
                    "This action requires confirmation, but no confirmation service is available. Nothing was executed.",
                    "CONFIRMATION_UNAVAILABLE");
                Publish(AssistantState.Failed, unavailable.Message, command);
                return new TaskExecutionResult(command, policy, unavailable, TaskStatus.Blocked, stopwatch.Elapsed);
            }

            bool approved;
            try
            {
                approved = await _confirmationService
                    .ConfirmAsync(command, policy, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var cancelled = ActionResult.Fail("The confirmation request was cancelled.", "USER_CANCELLED");
                Publish(AssistantState.Cancelled, cancelled.Message, command);
                return new TaskExecutionResult(command, policy, cancelled, TaskStatus.Cancelled, stopwatch.Elapsed);
            }

            if (!approved)
            {
                var cancelled = ActionResult.Fail("The user did not approve this action. Nothing was executed.", "USER_CANCELLED");
                Publish(AssistantState.Cancelled, cancelled.Message, command);
                return new TaskExecutionResult(command, policy, cancelled, TaskStatus.Cancelled, stopwatch.Elapsed);
            }
        }

        var executor = _executors.FirstOrDefault(candidate => candidate.CanExecute(command));
        if (executor is null)
        {
            var unavailable = ActionResult.Fail(
                $"No executor is installed for intent '{command.Intent}'. Nothing was executed.",
                "EXECUTOR_UNAVAILABLE");
            Publish(AssistantState.Failed, unavailable.Message, command);
            return new TaskExecutionResult(command, policy, unavailable, TaskStatus.Failed, stopwatch.Elapsed);
        }

        Publish(AssistantState.Executing, $"Executing: {GetIntentDescription(command)}", command);

        ActionResult result;
        try
        {
            result = await executor
                .ExecuteAsync(command, effectiveContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = ActionResult.Fail("The task was cancelled.", "USER_CANCELLED");
            Publish(AssistantState.Cancelled, cancelled.Message, command);
            return new TaskExecutionResult(command, policy, cancelled, TaskStatus.Cancelled, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            result = ActionResult.Fail($"The action failed unexpectedly: {ex.Message}", "EXECUTOR_EXCEPTION");
        }

        result = result with { Duration = stopwatch.Elapsed };

        if (!result.Success)
        {
            Publish(AssistantState.Failed, result.Message, command);
            return new TaskExecutionResult(command, policy, result, TaskStatus.Failed, stopwatch.Elapsed);
        }

        Publish(AssistantState.Verifying, "Verifying the result.", command);
        var status = result.Verified ? TaskStatus.Succeeded : TaskStatus.SucceededUnverified;
        var completionMessage = result.Verified
            ? result.Message
            : $"{result.Message} The action completed, but CIPHER could not independently verify the final state.";

        Publish(AssistantState.Completed, completionMessage, command);
        return new TaskExecutionResult(command, policy, result, status, stopwatch.Elapsed);
    }

    private static string GetIntentDescription(ParsedCommand command)
    {
        return string.IsNullOrWhiteSpace(command.Summary)
            ? command.Intent.ToString()
            : command.Summary;
    }

    private void Publish(AssistantState state, string message, ParsedCommand? command = null)
    {
        State = state;
        EventPublished?.Invoke(this, new AssistantEvent(DateTimeOffset.Now, state, message, command));
    }
}
