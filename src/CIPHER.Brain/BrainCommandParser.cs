using CIPHER.Core;

namespace CIPHER.Brain;

/// <summary>
/// Composite parser: the deterministic grammar runs first (fast, exact, predictable).
/// Only when it cannot map the text does the on-device brain get a chance to classify
/// the request into a structured intent. If the brain is missing, disabled, or fails,
/// the deterministic result stands — CIPHER behaves exactly as it did before Stage 11.
/// </summary>
public sealed class BrainCommandParser : ICommandParser
{
    private readonly ICommandParser _inner;
    private readonly BrainIntentMapper _mapper;
    private readonly Func<bool> _brainEnabled;

    public BrainCommandParser(ICommandParser inner, ILocalBrain brain, Func<bool>? brainEnabled = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(brain);
        _mapper = new BrainIntentMapper(brain);
        _brainEnabled = brainEnabled ?? (() => brain.IsAvailable);
    }

    public ParsedCommand Parse(string input, CommandContext? context = null)
    {
        var deterministic = _inner.Parse(input, context);
        if (deterministic.Intent != IntentKind.Unknown)
        {
            return deterministic;
        }

        bool enabled;
        try
        {
            enabled = _brainEnabled();
        }
        catch (Exception)
        {
            enabled = false;
        }

        if (!enabled)
        {
            return deterministic;
        }

        try
        {
            var commandText = string.IsNullOrWhiteSpace(deterministic.NormalizedText)
                ? input
                : deterministic.NormalizedText;
            return _mapper.TryMap(deterministic.OriginalText, commandText) ?? deterministic;
        }
        catch (Exception)
        {
            // The brain is an enhancement, never a single point of failure.
            return deterministic;
        }
    }
}
