# CIPHER

**Cognitive Interactive Protocol for Human Engagement and Response**

CIPHER is an offline-first Windows desktop assistant with a floating Dynamic Island-style WPF
interface, deterministic local commands, Windows automation, an explicit action-policy layer,
and a staged path toward voice, browser automation, external-AI delegation, persistent memory,
and a real offline WiX MSI installer.

> Current status: **Stages 1–10 implemented; Windows CI passing, including the automated
> Notepad milestone, named-element targeting, browser automation, offline voice,
> kill/restart memory-recovery, external-AI delegation, security-gate harnesses, and a
> CI-built `CIPHER.msi` (~347 MiB) that installs, verifies, and uninstalls on the
> runner.** This repository is not yet a release: clean-machine offline install
> validation on Windows 10/11 VMs and interactive WPF/desktop tests on a physical
> Windows session are still required before release.

## What is implemented now

- .NET 10 solution targeting Windows 10/11 x64.
- WPF Dynamic Island-style window with:
  - status indicator;
  - expandable/collapsible layout;
  - command input;
  - conversation history;
  - progress/status text;
  - cancellation;
  - settings panel for wake phrase and always-on-top behavior;
  - system tray icon with show/hide/exit;
  - Ctrl+Alt+C global show/hide hotkey (with a clear message if another app owns it).
- Offline deterministic command parser. It does not require an LLM, API key, cloud account, or
  internet connection.
- Action policy with fail-closed behavior:
  - arbitrary shell commands are blocked;
  - destructive actions require explicit confirmation;
  - protected Windows locations are blocked;
  - sensitive applications require confirmation.
- Windows action services:
  - launch known or user-specified applications;
  - request graceful application close without force-killing;
  - find and focus windows through UI Automation;
  - type Unicode text and send keyboard shortcuts through Win32 `SendInput`;
  - read editor text back through UI Automation and verify expected content
    (`read text in Notepad`, `verify Notepad contains ...`); typing reports itself as
    verified only when the typed text is observed afterwards;
  - create/list/copy/move/rename files and folders;
  - move deleted items to a reversible local quarantine instead of permanently deleting them.
- Playwright browser automation (Stage 5): open websites, read page title/text, fill
  fields and buttons by accessible role/label, and save downloads locally
  (never executed). Only http/https URLs open; credential-bearing URLs and
  credential-field fills are blocked, and dangerous buttons confirm first. The
  browser launches lazily and visibly when a browser command runs.
- External AI delegation (Stage 8, optional and off by default): `ask ai <question>`
  shows the exact prompt for your approval, then sends it to a configurable
  OpenAI-compatible endpoint (cloud or a local model server). The API key is stored
  DPAPI-encrypted, replies are saved to the Results folder and read-back verified, and
  AI-suggested commands are shown for review — never auto-run. Core CIPHER needs none
  of this and works fully offline without it.
- Persistent memory (Stage 7): a local SQLite store journals every task, so a session
  that ends mid-task is reported (and marked interrupted, never auto re-run) at the next
  start. Teach CIPHER with `alias morning = open notepad` and
  `remember app my editor at C:\Tools\editor.exe`; `list aliases` / `list apps` /
  `forget …` manage them. Aliases expand through the same safety-checked command flow.
- Security & recovery (Stage 9): a hash-chained local audit log (see `verify audit log`),
  credential-shaped redaction in the journal and audit log, quarantine recovery
  (`list quarantine`, `restore <name>` with confirmation), a `--safe-mode` startup flag,
  and a threat model in `docs/THREAT_MODEL.md`.
- Offline voice (Stage 6): Vosk speech recognition with “Hey CIPHER” wake-phrase
  listening (off by default, microphone only open while enabled) and push-to-talk via
  the 🎙 button; recognized speech runs through the same deterministic command engine.
  Optional eSpeak NG speech output for results. The model is loaded only while
  listening — unzip `vosk-model-small-en-us-0.15` into
  `%LocalAppData%\CIPHER\models` (the installer will bundle it later).
- Built-in informational commands: help, time, date, and system information.
- Automated tests for the parser, policy, wake-phrase handling, and task engine.
- Windows batch scripts for setup checks, dependency checks, build, test, package gating, and
  clean.
- GitHub Actions workflow that restores, builds, tests, and publishes a validation build on a
  Windows runner.

## What is not implemented yet

The following are planned stages, not current claims:

- Clean-machine offline install/uninstall verification of `CIPHER.msi` on Windows 10/11
  VMs (CI builds the MSI and proves install → shortcut → uninstall on the runner;
  see `docs/TEST_REPORT_STAGE10.md`).
- Browser-based AI platforms and multi-step delegated workflows (Stage 8 follow-ups;
  API-based delegation is implemented).
- Windows Graphics Capture/OCR fallback.
- Performance benchmark report against the target budgets (Stage 11).

The microphone button is push-to-talk and only opens the microphone while listening is
active; when the speech model is missing it explains where to install it instead of
pretending to listen.

## Supported typed commands

Examples:

```text
open Notepad
close Notepad
switch to Notepad
type Hello from CIPHER
press Ctrl+S
read text in Notepad
verify Notepad contains Hello from CIPHER
click Save button in Notepad
type hello into Search field in Notepad
open website example.com
read page text
list quarantine
restore report.txt
verify audit log
browser fill Email with ada@example.com
browser click Submit registration
browser download Download hello.txt
create folder C:\Temp\CIPHER-Test
create folder in C:\Temp called CIPHER-Test
create file C:\Temp\cipher-note.txt
list files in C:\Users\Public
copy C:\Temp\source.txt to C:\Temp\copy.txt
move C:\Temp\source.txt to C:\Temp\moved.txt
rename C:\Temp\old.txt to new.txt
delete C:\Temp\unneeded.txt
time
date
system info
help
```

The optional wake phrase can prefix typed or (later) transcribed commands:

```text
Hey CIPHER, open Notepad
```

Destructive commands require a confirmation dialog. `delete` moves the item to
`%LocalAppData%\CIPHER\Quarantine` so the action is reversible; CIPHER v1 does not silently
permanently erase data.

## Build on Windows

Prerequisites:

- Windows 10 or Windows 11, x64
- .NET 10 SDK
- Git

From a Developer PowerShell or Command Prompt:

```bat
setup-dev.bat
build.bat Release
test.bat Release
src\CIPHER.App\bin\Release\net10.0-windows\win-x64\CIPHER.exe
```

Or with the .NET CLI:

```powershell
dotnet restore CIPHER.sln
dotnet build CIPHER.sln -c Release
dotnet test CIPHER.sln -c Release
```

The WPF application must be built/run on Windows. The platform-independent core and its tests are
kept separate so command/security behavior can evolve without coupling every rule to WPF.

## Building the offline installer (Stage 10)

```powershell
dotnet restore CIPHER.sln
pwsh scripts\stage-dependencies.ps1   # one-time payload staging (downloads here only)
package.bat Release                    # produces artifacts\CIPHER.msi
```

The MSI is per-user (no admin prompt), installs under `%LocalAppData%\CIPHERProgram` with a
Start menu shortcut, and bundles the Vosk model, eSpeak NG, Chromium, and license notices —
it never downloads anything at install time. See `docs/TEST_REPORT_STAGE10.md`.

## Batch scripts

| Script | Purpose |
|---|---|
| `setup-dev.bat` | Checks OS architecture, Git, .NET 10 SDK, WiX, and prepares local folders. It does not silently install software. |
| `install-dependencies.bat` | Verifies the staged offline dependency bundle. It never downloads dependencies and fails closed when payloads are missing. |
| `build.bat` | Restores and builds the solution with logging and exit codes. |
| `test.bat` | Runs automated tests, writes a TRX file, and creates a test report. |
| `package.bat` | Verifies the dependency bundle (SHA-256 manifest), publishes the app self-contained, and builds the real `CIPHER.msi` into `artifacts\`. |
| `scripts\stage-dependencies.ps1` | One-time staging of offline payloads (Vosk model, libvosk, eSpeak NG, Chromium, licenses) + manifest regeneration. The only step that downloads. |
| `clean.bat` | Removes `bin`, `obj`, test results, and `.vs` data without deleting source, dependencies, release artifacts, or user data. |

## Repository layout

```text
CIPHER/
  CIPHER.sln
  src/
    CIPHER.App/        WPF Dynamic Island application
    CIPHER.Core/       Offline parser, policy, task engine, settings
    CIPHER.Actions/    Windows process/UI Automation/SendInput/file actions
    CIPHER.Tests/      xUnit tests for core behavior
    CIPHER.MilestoneTest/  Interactive Notepad milestone harness (Stage 3)
    CIPHER.Browser/        Playwright browser automation (Stage 5)
    CIPHER.BrowserTest/    Loopback browser automation harness (Stage 5)
    CIPHER.Voice/          Vosk recognition, microphone capture, eSpeak NG output (Stage 6)
    CIPHER.VoiceTest/      Generated-speech voice harness (Stage 6)
    CIPHER.Memory/         SQLite task journal, history, aliases, app paths (Stage 7)
    CIPHER.MemoryTest/     Kill/restart recovery harness (Stage 7)
    CIPHER.Delegation/     External AI delegation with DPAPI key store (Stage 8)
    CIPHER.DelegationTest/ Loopback fake-endpoint delegation harness (Stage 8)
    CIPHER.SecurityTest/   Cross-process audit-chain harness (Stage 9)
    CIPHER.Installer/      WiX v5 per-user MSI project (Stage 10; built by package.bat)
  scripts/               Dependency staging + SHA-256 manifest verification (Stage 10)
  docs/
  dependencies/       Offline dependency staging instructions (large files not in Git)
  .github/workflows/  Windows build/test pipeline
```

Future modules (Vision/OCR fallback, the Stage 10 installer project) will be added only when
their implementations are real enough to build and test; empty placeholder projects are
intentionally avoided.

## Security model

CIPHER runs as the current user (`asInvoker`) and must not bypass Windows security boundaries.
It does not store passwords, cookies, or credentials in settings. Confirmation is required for
high-risk actions, and arbitrary shell execution is disabled in v1.

See [SECURITY.md](SECURITY.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Documentation

- [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Windows compatibility](docs/WINDOWS_COMPATIBILITY.md)
- [Testing strategy](docs/TESTING.md)
- [Stage 1 test report](docs/TEST_REPORT_STAGE1.md)
- [Stage 3 Notepad milestone report](docs/TEST_REPORT_STAGE3.md)
- [Stage 4 element targeting report](docs/TEST_REPORT_STAGE4.md)
- [Stage 5 browser automation report](docs/TEST_REPORT_STAGE5.md)
- [Stage 6 offline voice report](docs/TEST_REPORT_STAGE6.md)
- [Stage 7 memory and recovery report](docs/TEST_REPORT_STAGE7.md)
- [Stage 8 external AI delegation report](docs/TEST_REPORT_STAGE8.md)
- [Stage 9 security & recovery report](docs/TEST_REPORT_STAGE9.md)
- [Stage 10 WiX MSI report](docs/TEST_REPORT_STAGE10.md)
- [Threat model](docs/THREAT_MODEL.md)
- [Changelog](CHANGELOG.md)

## License

CIPHER source code is available under the MIT License. Third-party dependency licenses will be
listed in `LICENSES/` before any bundled release is produced.
