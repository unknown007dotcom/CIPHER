namespace CIPHER.Core;

/// <summary>Converts natural-language or structured text into a deterministic command.</summary>
public interface ICommandParser
{
    ParsedCommand Parse(string input, CommandContext? context = null);
}

/// <summary>Executes one or more related command intents.</summary>
public interface IActionExecutor
{
    bool CanExecute(ParsedCommand command);

    Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken);
}

/// <summary>Evaluates whether a parsed command may run and whether approval is required.</summary>
public interface IActionPolicy
{
    PolicyDecision Evaluate(ParsedCommand command, CommandContext context);
}

/// <summary>Requests explicit user approval for a policy-controlled action.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(
        ParsedCommand command,
        PolicyDecision decision,
        CancellationToken cancellationToken);
}
