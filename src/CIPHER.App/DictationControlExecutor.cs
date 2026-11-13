using CIPHER.Core;

namespace CIPHER.App;

/// <summary>
/// Gives the Start/StopDictation intents a policy-checked home in the task engine.
/// The mode itself — and the microphone lifetime that goes with it — is owned by
/// <c>MainWindowViewModel</c>, which reacts to successful results; this executor
/// only reports the mode change (and produces the spoken confirmation).
/// </summary>
internal sealed class DictationControlExecutor : IActionExecutor
{
    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent is IntentKind.StartDictation or IntentKind.StopDictation;
    }

    public Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = command.Intent == IntentKind.StartDictation
            ? ActionResult.Ok(
                "Dictation is on. Speak and I will type into the focused window. " +
                "Say 'stop dictation' when you are done.",
                verified: true)
            : ActionResult.Ok("Dictation is off.", verified: true);
        return Task.FromResult(result);
    }
}
