using System.Diagnostics;

namespace CIPHER.Voice;

/// <summary>
/// Speech output through the eSpeak NG command-line synthesizer (fully offline, no audio
/// service dependency). The executable is resolved from an explicit path, an app-local
/// <c>tools\espeak-ng</c> folder (installer layout), Program Files, or PATH. When eSpeak NG
/// is absent, <see cref="IsAvailable"/> is false and speak calls return false so the UI can
/// say so honestly instead of failing silently.
/// </summary>
public sealed class EspeakSpeechSynthesizer : IDisposable
{
    private readonly object _gate = new();
    private Process? _currentProcess;
    private bool _disposed;

    public EspeakSpeechSynthesizer(string? executablePath = null, string voice = "en")
    {
        Voice = string.IsNullOrWhiteSpace(voice) ? "en" : voice.Trim();
        ExecutablePath = ResolveExecutable(executablePath);
    }

    /// <summary>Resolved espeak-ng.exe path, or null when eSpeak NG was not found.</summary>
    public string? ExecutablePath { get; }

    public string Voice { get; }

    /// <summary>Diagnostic detail from the most recent failed speak attempt, when any.</summary>
    public string? LastError { get; private set; }

    public bool IsAvailable => ExecutablePath is not null && File.Exists(ExecutablePath);

    public static string? ResolveExecutable(string? configuredPath)
    {
        var candidates = new List<string?> { configuredPath };
        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "tools", "espeak-ng", "espeak-ng.exe"));
        candidates.Add(Path.Combine(baseDirectory, "tools", "espeak-ng", "bin", "espeak-ng.exe"));
        candidates.Add(Path.Combine(baseDirectory, "espeak-ng", "espeak-ng.exe"));
        candidates.Add(Path.Combine(baseDirectory, "espeak-ng", "bin", "espeak-ng.exe"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "eSpeak NG", "espeak-ng.exe"));
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            candidates.AddRange(pathVariable
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directory => Path.Combine(directory, "espeak-ng.exe")));
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Speaks text through the default audio device. Returns false when unavailable.</summary>
    public async Task<bool> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        StopCurrent();
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("--stdin");

        using var process = new Process { StartInfo = startInfo };
        TrackProcess(process);
        try
        {
            if (!process.Start())
            {
                return false;
            }

            var errorReader = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteAsync(text).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The synthesizer exited before reading input; its exit code/stderr explains why.
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            RecordOutcome(process.ExitCode, await errorReader.ConfigureAwait(false));
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
        finally
        {
            UntrackProcess(process);
        }
    }

    /// <summary>
    /// Renders text to a WAV file without an audio device (used by CI to prove output works).
    /// Returns true only when eSpeak NG exited cleanly and produced a non-trivial file.
    /// </summary>
    public async Task<bool> SpeakToWaveFileAsync(
        string text,
        string waveFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(waveFilePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(waveFilePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        StopCurrent();
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add(waveFilePath);
        startInfo.ArgumentList.Add(text.Trim());

        using var process = new Process { StartInfo = startInfo };
        TrackProcess(process);
        try
        {
            if (!process.Start())
            {
                LastError = "espeak-ng.exe could not be started.";
                return false;
            }

            var errorReader = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            RecordOutcome(process.ExitCode, await errorReader.ConfigureAwait(false));
            return process.ExitCode == 0 &&
                   File.Exists(waveFilePath) &&
                   new FileInfo(waveFilePath).Length > 100;
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
        finally
        {
            UntrackProcess(process);
        }
    }

    /// <summary>Immediately stops any in-progress speech (the voice cancel path).</summary>
    public void StopCurrent()
    {
        Process? process;
        lock (_gate)
        {
            process = _currentProcess;
            _currentProcess = null;
        }

        if (process is not null)
        {
            KillQuietly(process);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCurrent();
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo(ExecutablePath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };

        // Extracted/portable layouts keep the voice data either next to the executable
        // (child "espeak-ng-data") or — when the exe lives in a "bin" subfolder — as a
        // sibling of that folder. Point eSpeak NG at whichever exists.
        var executableDirectory = Path.GetDirectoryName(ExecutablePath!);
        if (!string.IsNullOrWhiteSpace(executableDirectory))
        {
            var directDataDirectory = Path.Combine(executableDirectory, "espeak-ng-data");
            var parentDataDirectory = Path.GetFullPath(Path.Combine(executableDirectory, "..", "espeak-ng-data"));
            if (Directory.Exists(directDataDirectory))
            {
                startInfo.Environment["ESPEAK_DATA_PATH"] = directDataDirectory;
            }
            else if (Directory.Exists(parentDataDirectory))
            {
                startInfo.Environment["ESPEAK_DATA_PATH"] = parentDataDirectory;
            }
        }

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add(Voice);
        return startInfo;
    }

    private void RecordOutcome(int exitCode, string standardError)
    {
        if (exitCode == 0)
        {
            LastError = null;
            return;
        }

        var detail = standardError?.Trim() ?? string.Empty;
        if (detail.Length > 300)
        {
            detail = detail[..300];
        }

        LastError = detail.Length > 0
            ? $"eSpeak NG exited with code {exitCode}: {detail}"
            : $"eSpeak NG exited with code {exitCode}.";
    }

    private void TrackProcess(Process process)
    {
        lock (_gate)
        {
            _currentProcess = process;
        }
    }

    private void UntrackProcess(Process process)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_currentProcess, process))
            {
                _currentProcess = null;
            }
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
        catch (Exception)
        {
            // The process may have exited between the check and the kill.
        }
    }
}
