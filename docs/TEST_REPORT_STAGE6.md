# CIPHER Stage 6 offline voice test report

## Result

- Date: 2026-11-08
- Commit tested: `47dcfb7` (branch `arena/1bb3d317-cipher`)
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42489921302
- Harness: `src/CIPHER.VoiceTest` (`CIPHER.VoiceTest.exe`)
- Outcome: **PASS** — “offline recognition, wake-phrase gating, and speech output checks
  succeeded.” Stage 3, Stage 4, and Stage 5 harnesses also passed in the same run, and
  the deterministic Core test suite grew from 48 to 57 cases.

## Scenario executed

The harness verifies the full offline voice path without a microphone or speakers,
using deterministic generated inputs and the real production code:

1. **Wake-phrase session logic** — `VoiceSession` (Core) ignores transcripts without the
   wake phrase, treats “hey cipher” alone as arming a follow-up window, and accepts the
   next utterance inside that window as the command.
2. **Offline recognition** — CI generates a speech WAV with Windows SAPI
   (“Hey Cipher, what time is it?”, 16 kHz mono), downloads the Vosk small English model
   (`vosk-model-small-en-us-0.15`), and runs `VoskSpeechRecognizer.RecognizeWaveFile`.
   Recognized verbatim: **“hey cipher what time is it”** in ≈1.65 s of CI compute.
3. **Gating of the recognized transcript** — the transcript is fed through
   `VoiceSession`, which returns `CommandReady` with command text `what time is it`.
4. **Speech output** — CI extracts eSpeak NG from its official MSI (administrative
   install, no machine changes) and `EspeakSpeechSynthesizer` renders
   “CIPHER voice output verification.” to a valid WAV file. An extracted-data layout
   issue (`ESPEAK_DATA_PATH`) found by the first run was fixed and re-verified.

## What the application does with this

- The 🎙 button is now **push-to-talk**: it loads the Vosk model on demand, opens the
  microphone (the button turns into a red ⏹ while listening, partial transcripts show
  live), and the next utterance runs through the same parser/policy/task engine as typed
  commands. With nothing heard, the flushed final transcript is still honored.
- Settings offer **continuous wake-phrase listening** (“Hey CIPHER” + command, with a
  12-second follow-up window). It is off by default, never auto-starts at launch, the
  model is only loaded while listening, and turning it off closes the microphone.
- **Speak responses aloud (eSpeak NG)** reads result summaries (truncated) after commands.
  When eSpeak NG is missing, CIPHER states that once and stays text-only — no silent
  failure. Cancel also stops in-progress speech.
- When the Vosk model is absent, voice controls explain exactly where to unzip the model
  (`%LocalAppData%\CIPHER\models\vosk-model-small-en-us-0.15` or a `models` folder next to
  the app). Nothing audio-related is loaded at idle, and no audio leaves the PC.

## Honest limitations

- **No physical microphone or speakers were available in CI.** Capture uses NAudio
  WaveInEvent (standard Windows path) and recognition from WAV uses the same recognizer,
  but real-microphone acoustics, SAPI-vs-human voice differences, and speaker output must
  still be verified on a real Windows 10/11 machine before release.
- Test speech was synthesized; recognition accuracy on human speech (accents, noise) is
  unmeasured. The small Vosk model is the lightweight option; larger models are a future
  quality/performance trade-off.
- The Vosk model and eSpeak NG are **not yet bundled** — the Stage 10 offline installer
  must ship both (with license review) so first-run voice works without downloads.
- CPU-only recognition timing here (≈1.65 s for a ~3 s utterance on a CI runner) is an
  initial data point, not the Stage 11 benchmark.
