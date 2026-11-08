using NAudio.Wave;

namespace CIPHER.Voice;

/// <summary>One captured microphone buffer (16-bit PCM, consumed synchronously by handlers).</summary>
public sealed class MicrophoneAudioEventArgs : EventArgs
{
    public MicrophoneAudioEventArgs(byte[] buffer, int bytesRecorded)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
    }

    /// <summary>NAudio-owned buffer; valid only during the event callback.</summary>
    public byte[] Buffer { get; }

    public int BytesRecorded { get; }
}

/// <summary>
/// Thin wrapper over NAudio's WaveInEvent. Capture only runs between explicit
/// <see cref="Start"/>/<see cref="Stop"/> calls — the microphone is never opened at idle.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _disposed;

    public event EventHandler<MicrophoneAudioEventArgs>? AudioAvailable;

    public event EventHandler<Exception>? CaptureError;

    public bool IsCapturing => _waveIn is not null;

    public void Start(int sampleRate = 16000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_waveIn is not null)
        {
            return;
        }

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 100
        };
        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;
        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.Dispose();
            throw;
        }

        _waveIn = waveIn;
    }

    public void Stop()
    {
        var waveIn = _waveIn;
        _waveIn = null;
        if (waveIn is null)
        {
            return;
        }

        waveIn.DataAvailable -= OnDataAvailable;
        waveIn.RecordingStopped -= OnRecordingStopped;
        try
        {
            waveIn.StopRecording();
        }
        catch (Exception)
        {
            // Stopping capture must be idempotent; the device may already be gone.
        }
        finally
        {
            waveIn.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            AudioAvailable?.Invoke(this, new MicrophoneAudioEventArgs(e.Buffer, e.BytesRecorded));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            CaptureError?.Invoke(this, e.Exception);
        }
    }
}
