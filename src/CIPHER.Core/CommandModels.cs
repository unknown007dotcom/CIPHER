namespace CIPHER.Core;

/// <summary>An immutable, structured representation of a user command.</summary>
public sealed record ParsedCommand(
    string OriginalText,
    string NormalizedText,
    IntentKind Intent,
    string? Target = null,
    string? Argument = null,
    string? SecondaryTarget = null,
    double Confidence = 0.0,
    RiskLevel Risk = RiskLevel.Low,
    string Summary = "")
{
    /// <summary>Example commands returned when parsing cannot identify an intent.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    public static ParsedCommand Unknown(string originalText, string normalizedText, string summary)
    {
        return new ParsedCommand(
            originalText,
            normalizedText,
            IntentKind.Unknown,
            Confidence: 0.0,
            Risk: RiskLevel.Low,
            Summary: summary)
        {
            Suggestions = new[]
            {
                "open Notepad",
                "search for the Apollo 11 Moon landing",
                "what is the capital of France",
                "start dictation",
                "type Hello from CIPHER",
                "press Ctrl+S",
                "create folder C:\\Temp\\CIPHER-Test"
            }
        };
    }
}

/// <summary>Ambient information supplied to parsers and executors.</summary>
public sealed record CommandContext(
    string WorkingDirectory,
    string UserName,
    string MachineName,
    DateTimeOffset Timestamp)
{
    public static CommandContext Current => new(
        Environment.CurrentDirectory,
        Environment.UserName,
        Environment.MachineName,
        DateTimeOffset.Now);
}

/// <summary>Decision emitted by the action policy before execution.</summary>
public sealed record PolicyDecision(
    bool IsAllowed,
    bool RequiresConfirmation,
    RiskLevel Risk,
    string Reason);

/// <summary>State/progress notification emitted while a task runs.</summary>
public sealed record AssistantEvent(
    DateTimeOffset Timestamp,
    AssistantState State,
    string Message,
    ParsedCommand? Command = null);

/// <summary>Final outcome of a parsed and policy-checked task.</summary>
public sealed record TaskExecutionResult(
    ParsedCommand Command,
    PolicyDecision Policy,
    ActionResult? ActionResult,
    CipherTaskStatus Status,
    TimeSpan Duration);
