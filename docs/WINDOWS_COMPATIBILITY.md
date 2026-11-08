# Windows compatibility

## Target for CIPHER v1

- Windows 10 64-bit
- Windows 11 64-bit
- x64 CPU; no dedicated GPU required
- Current-user installation and execution by default
- No administrator rights for normal local commands

ARM64, 32-bit Windows, Windows Server, and LTSC-specific behavior are not v1 release targets
unless separately tested.

## Runtime and API notes

- The application targets .NET 10 and `net10.0-windows` for WPF and Windows automation.
- WPF, UI Automation, Windows Graphics Capture, and Windows.Media.Ocr behavior must be tested on
  both Windows 10 and Windows 11 before release.
- The application manifest requests `asInvoker`; CIPHER must not silently elevate.
- Long paths and per-monitor DPI awareness are enabled in the application manifest.

## Automation limitations

CIPHER's normal-integrity process cannot automate:

- Windows secure desktop;
- UAC consent prompts;
- lock screen or another user's session;
- higher-integrity/administrator windows;
- applications that intentionally block UI Automation or synthetic input.

Windows may also deny foreground activation. CIPHER reports focus as unverified when Windows
does not confirm that the target window became foreground.

UI Automation trees vary across Win32, WPF, WinUI, UWP, Electron, and browser applications. The
Stage 3 milestone must therefore be tested against real Notepad builds on Windows 10 and 11, not
only against mocked elements.

## CPU compatibility

The initial application avoids optional local inference. Later native components must be checked
for instruction-set assumptions:

- Vosk/Kaldi binaries must run on older x64 CPUs targeted by the release.
- Optional llama.cpp binaries, if ever shipped, must not require AVX2/AVX-512 unless a compatible
  fallback binary is selected after CPU capability detection.
- Startup should detect unsupported native payloads and disable that feature with a clear message
  rather than crashing the assistant.

## Test matrix before release

| Test | Windows 10 x64 | Windows 11 x64 |
|---|:---:|:---:|
| Build and unit tests | Required | Required |
| WPF island at 100/125/150% scaling | Required | Required |
| Notepad milestone | Required | Required |
| Clean offline MSI install | Required | Required |
| Upgrade/repair/uninstall | Required | Required |
| CPU-only voice test | Required | Required |
| Browser automation test pages | Required | Required |
| Low-resource laptop benchmark | At least one OS | At least one OS |

A result is recorded only after the test is actually run; CI build success is not a substitute for
interactive desktop verification.
