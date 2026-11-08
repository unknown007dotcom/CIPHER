using CIPHER.Core;
using NAudio.Wave;
using Vosk;

namespace CIPHER.Voice;

/// <summary>
/// Offline speech recognition backed by Vosk. The model is loaded only when a recognizer is
/// constructed (i.e. while the user is listening) and released on <see cref="Dispose"/>, so
/// nothing speech-related consumes memory at idle. Audio input is 16 kHz mono 16-bit PCM.
/// </summary>
public sealed class VoskSpeechRecognizer : IDisposable
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private bool _disposed;

    public VoskSpeechRecognizer(string modelPath)
    {
        if (!VoskModelLocator.IsModelDirectory(modelPath))
        {
            throw new DirectoryNotFoundException(
                $"No Vosk speech model was found at '{modelPath}'. " +
                $"Download {VoskModelLocator.SmallEnglishModelName} and place it under " +
                "%LocalAppData%\\CIPHER\\models (or next to the app in a models folder).");
        }

        ModelPath = modelPath;
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, SampleRate);
        _recognizer.SetWords(true);
    }

    public string ModelPath { get; }

    /// <summary>Feeds one PCM buffer; returns true when a final utterance is ready.</summary>
    public bool FeedAudio(byte[] buffer, int bytesRecorded)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return bytesRecorded > 0 && _recognizer.AcceptWaveform(buffer, bytesRecorded);
        }
    }

    /// <summary>Transcript of the most recently completed utterance (empty when none).</summary>
    public string GetResultText()
    {
        lock (_gate)
        {
            return _disposed ? string.Empty : VoskResultParser.GetText(_recognizer.Result()) ?? string.Empty;
        }
    }

    /// <summary>In-progress transcript for live feedback (empty when silent).</summary>
    public string GetPartialText()
    {
        lock (_gate)
        {
            return _disposed ? string.Empty : VoskResultParser.GetPartialText(_recognizer.PartialResult()) ?? string.Empty;
        }
    }

    /// <summary>Flushes any pending audio and returns its transcript (empty when none).</summary>
    public string FlushFinalText()
    {
        lock (_gate)
        {
            return _disposed ? string.Empty : VoskResultParser.GetText(_recognizer.FinalResult()) ?? string.Empty;
        }
    }

    /// <summary>
    /// Recognizes a complete audio file offline. The file is resampled to 16 kHz mono PCM,
    /// so CI can verify recognition from generated speech without a microphone.
    /// </summary>
    public static string RecognizeWaveFile(string modelPath, string waveFilePath)
    {
        if (!File.Exists(waveFilePath))
        {
            throw new FileNotFoundException("The audio file to recognize was not found.", waveFilePath);
        }

        using var recognizer = new VoskSpeechRecognizer(modelPath);
        using var reader = new AudioFileReader(waveFilePath);
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(SampleRate, 16, 1))
        {
            ResamplerQuality = 60
        };

        var buffer = new byte[SampleRate];
        var lastFinal = string.Empty;
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (recognizer.FeedAudio(buffer, read))
            {
                lastFinal = recognizer.GetResultText();
            }
        }

        var flushed = recognizer.FlushFinalText();
        return flushed.Length > 0 ? flushed : lastFinal;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _recognizer.Dispose();
            _model.Dispose();
        }
    }
}
