using System.Diagnostics;
using CIPHER.Core;
using CIPHER.Voice;

namespace CIPHER.VoiceTest;

/// <summary>
/// Stage 6 acceptance harness. Verifies offline voice without a microphone or speakers:
/// a generated speech WAV ("Hey Cipher, what time is it?") is recognized by the real Vosk
/// pipeline, the transcript is gated through <see cref="VoiceSession"/>, and eSpeak NG (when
/// provided) renders speech to a WAV file. Inputs come from arguments or the
/// CIPHER_VOSK_MODEL / CIPHER_VOICE_WAV / CIPHER_ESPEAK_EXE environment variables.
///
/// Exit codes: 0 = PASS, 1 = FAIL, 3 = SKIP (model or test audio unavailable).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var modelPath = GetOption(args, "--model") ?? Environment.GetEnvironmentVariable("CIPHER_VOSK_MODEL");
        var wavePath = GetOption(args, "--wav") ?? Environment.GetEnvironmentVariable("CIPHER_VOICE_WAV");
        var espeakPath = GetOption(args, "--espeak") ?? Environment.GetEnvironmentVariable("CIPHER_ESPEAK_EXE");

        var failures = 0;
        var skippedCoreChecks = false;

        // 1) Wake-phrase session logic on synthetic transcripts (no audio involved).
        RunStep("Voice session wake-phrase gating", () =>
        {
            var now = DateTimeOffset.Now;
            var session = new VoiceSession(new WakeWordDetector("Hey CIPHER"));

            Require(session.HandleFinalTranscript("a perfectly ordinary sentence", now).Outcome
                    == VoiceTranscriptOutcome.Ignored,
                "Transcript without the wake phrase was not ignored.");
            Require(session.HandleFinalTranscript("hey cipher", now).Outcome
                    == VoiceTranscriptOutcome.WakePhraseHeard,
                "Wake phrase alone was not detected.");
            var followUp = session.HandleFinalTranscript("open notepad", now.AddSeconds(3));
            Require(followUp.Outcome == VoiceTranscriptOutcome.CommandReady &&
                    followUp.CommandText == "open notepad",
                $"Follow-up command was not accepted (got {followUp.Outcome}: '{followUp.CommandText}').");
            return "ignore / wake-only / follow-up command transitions are correct";
        }, ref failures);

        // 2) Real offline recognition of generated speech through Vosk.
        string recognizedText = string.Empty;
        if (string.IsNullOrWhiteSpace(modelPath) || !VoskModelLocator.IsModelDirectory(modelPath))
        {
            skippedCoreChecks = true;
            Console.WriteLine("::warning title=Stage 6 voice harness::STAGE6-SKIP no Vosk model directory was provided; speech recognition was not exercised.");
        }
        else if (string.IsNullOrWhiteSpace(wavePath) || !File.Exists(wavePath))
        {
            skippedCoreChecks = true;
            Console.WriteLine("::warning title=Stage 6 voice harness::STAGE6-SKIP no generated test WAV was provided; speech recognition was not exercised.");
        }
        else
        {
            RunStep("Vosk offline recognition of generated speech", () =>
            {
                var stopwatch = Stopwatch.StartNew();
                recognizedText = VoskSpeechRecognizer.RecognizeWaveFile(modelPath, wavePath);
                stopwatch.Stop();
                Console.WriteLine($"::notice title=Stage 6 voice harness::Recognized '{recognizedText}' in {stopwatch.ElapsedMilliseconds} ms.");
                Require(recognizedText.Contains("cipher", StringComparison.OrdinalIgnoreCase),
                    $"Recognized text does not contain the wake word: '{recognizedText}'.");
                return $"recognized '{recognizedText}'";
            }, ref failures);

            // 3) The recognized transcript must pass wake-phrase gating as a command.
            RunStep("Recognized transcript passes wake-phrase gating", () =>
            {
                Require(recognizedText.Length > 0, "No transcript was available from the recognition step.");
                var session = new VoiceSession(new WakeWordDetector("Hey CIPHER"));
                var result = session.HandleFinalTranscript(recognizedText, DateTimeOffset.Now);
                Require(result.Outcome == VoiceTranscriptOutcome.CommandReady,
                    $"Recognized transcript was gated as {result.Outcome}, not CommandReady.");
                Require(result.CommandText.Contains("time", StringComparison.OrdinalIgnoreCase),
                    $"Extracted command does not mention 'time': '{result.CommandText}'.");
                return $"command extracted: '{result.CommandText}'";
            }, ref failures);
        }

        // 4) eSpeak NG speech output rendered to a file (no audio device needed).
        if (string.IsNullOrWhiteSpace(espeakPath))
        {
            Console.WriteLine("::warning title=Stage 6 voice harness::eSpeak NG path was not provided; speech output rendering was skipped.");
        }
        else
        {
            RunStep("eSpeak NG speech output rendering", () =>
            {
                using var synthesizer = new EspeakSpeechSynthesizer(espeakPath, "en");
                Require(synthesizer.IsAvailable, $"eSpeak NG was not found at '{espeakPath}'.");
                var outputPath = Path.Combine(Path.GetTempPath(), $"cipher-stage6-{Guid.NewGuid():N}.wav");
                try
                {
                    var rendered = synthesizer
                        .SpeakToWaveFileAsync("CIPHER voice output verification.", outputPath)
                        .GetAwaiter().GetResult();
                    Require(rendered, $"eSpeak NG did not produce a valid WAV file. {synthesizer.LastError}");
                    return $"rendered {new FileInfo(outputPath).Length} bytes of speech audio";
                }
                finally
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                }
            }, ref failures);
        }

        if (skippedCoreChecks)
        {
            Console.WriteLine("STAGE6-SKIP: recognition checks were skipped (see warnings above).");
            return 3;
        }

        if (failures > 0)
        {
            Console.WriteLine($"::error title=Stage 6 voice harness::STAGE6-FAIL {failures} step(s) failed.");
            return 1;
        }

        Console.WriteLine("::notice title=Stage 6 voice harness::STAGE6-PASS offline recognition, wake-phrase gating, and speech output checks succeeded.");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void RunStep(string name, Func<string> step, ref int failures)
    {
        try
        {
            var detail = step();
            Console.WriteLine($"[PASS] {name}: {detail}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"::error title=Stage 6 voice harness::{name} failed: {ex.Message}");
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
