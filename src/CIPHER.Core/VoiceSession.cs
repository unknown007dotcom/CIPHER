namespace CIPHER.Core;

/// <summary>What the voice session decided to do with one final transcript.</summary>
public enum VoiceTranscriptOutcome
{
    /// <summary>The transcript was not addressed to CIPHER and no command is pending.</summary>
    Ignored,

    /// <summary>The wake phrase was heard on its own; the next utterance is the command.</summary>
    WakePhraseHeard,

    /// <summary>A command addressed to CIPHER is ready to execute.</summary>
    CommandReady
}

/// <summary>Result of feeding one final speech transcript into a <see cref="VoiceSession"/>.</summary>
public readonly record struct VoiceSessionResult(
    VoiceTranscriptOutcome Outcome,
    string CommandText,
    string Transcript);

/// <summary>
/// Wake-phrase gating for recognized speech. A transcript only becomes a command when it
/// starts with the configured wake phrase (any command text after the phrase is used
/// immediately), or when it arrives inside the short follow-up window after the wake phrase
/// was heard on its own. Push-to-talk primes the session so the next utterance counts as a
/// command without needing the wake phrase.
/// </summary>
public sealed class VoiceSession
{
    private readonly WakeWordDetector _detector;
    private readonly TimeSpan _followUpWindow;
    private DateTimeOffset _awaitingCommandUntil = DateTimeOffset.MinValue;

    public VoiceSession(WakeWordDetector detector, TimeSpan? followUpWindow = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _followUpWindow = followUpWindow ?? TimeSpan.FromSeconds(12);
        if (_followUpWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(followUpWindow), "The follow-up window must be positive.");
        }
    }

    public string WakePhrase => _detector.Phrase;

    public bool IsAwaitingCommand(DateTimeOffset now) => now <= _awaitingCommandUntil;

    /// <summary>Treats the next utterance within the follow-up window as a command (push-to-talk).</summary>
    public void Prime(DateTimeOffset now) => _awaitingCommandUntil = now + _followUpWindow;

    public void Reset() => _awaitingCommandUntil = DateTimeOffset.MinValue;

    public VoiceSessionResult HandleFinalTranscript(string? transcript, DateTimeOffset now)
    {
        var text = WakeWordDetector.NormalizeWhitespace(transcript);
        if (text.Length == 0)
        {
            return new VoiceSessionResult(VoiceTranscriptOutcome.Ignored, string.Empty, text);
        }

        var wake = _detector.Process(text);
        if (wake.Detected)
        {
            if (wake.CommandText.Length > 0)
            {
                Reset();
                return new VoiceSessionResult(VoiceTranscriptOutcome.CommandReady, wake.CommandText, text);
            }

            Prime(now);
            return new VoiceSessionResult(VoiceTranscriptOutcome.WakePhraseHeard, string.Empty, text);
        }

        if (IsAwaitingCommand(now))
        {
            Reset();
            return new VoiceSessionResult(VoiceTranscriptOutcome.CommandReady, text, text);
        }

        Reset();
        return new VoiceSessionResult(VoiceTranscriptOutcome.Ignored, string.Empty, text);
    }
}
