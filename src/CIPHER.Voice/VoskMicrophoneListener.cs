namespace CIPHER.Voice;

/// <summary>
/// Connects the microphone to the recognizer and surfaces partial/final transcripts.
/// Events fire on the capture thread; UI consumers must marshal to their dispatcher.
/// </summary>
public sealed class VoskMicrophoneListener : IDisposable
{
    private readonly VoskSpeechRecognizer _recognizer;
    private readonly MicrophoneCapture _microphone = new();
    private bool _disposed;

    public VoskMicrophoneListener(VoskSpeechRecognizer recognizer)
    {
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _microphone.AudioAvailable += OnAudioAvailable;
        _microphone.CaptureError += (_, ex) => ListeningError?.Invoke(this, ex);
    }

    public event EventHandler<string>? PartialTextAvailable;

    public event EventHandler<string>? FinalTextAvailable;

    public event EventHandler<Exception>? ListeningError;

    public bool IsListening => _microphone.IsCapturing;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _microphone.Start(VoskSpeechRecognizer.SampleRate);
    }

    /// <summary>Stops capture and returns any transcript flushed from pending audio.</summary>
    public string Stop()
    {
        if (!_microphone.IsCapturing)
        {
            return string.Empty;
        }

        _microphone.Stop();
        return _recognizer.FlushFinalText();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _microphone.AudioAvailable -= OnAudioAvailable;
        try
        {
            _microphone.Stop();
        }
        finally
        {
            _microphone.Dispose();
            _recognizer.Dispose();
        }
    }

    private void OnAudioAvailable(object? sender, MicrophoneAudioEventArgs e)
    {
        try
        {
            if (_recognizer.FeedAudio(e.Buffer, e.BytesRecorded))
            {
                var finalText = _recognizer.GetResultText();
                if (finalText.Length > 0)
                {
                    FinalTextAvailable?.Invoke(this, finalText);
                }
            }
            else
            {
                var partialText = _recognizer.GetPartialText();
                if (partialText.Length > 0)
                {
                    PartialTextAvailable?.Invoke(this, partialText);
                }
            }
        }
        catch (Exception ex)
        {
            ListeningError?.Invoke(this, ex);
        }
    }
}
