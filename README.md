# CIPHER

**Cognitive Interactive Protocol for Human Engagement and Response**

CIPHER is an offline-first Windows desktop assistant with a floating Dynamic Island-style WPF
interface, deterministic local commands, Windows automation, an explicit action-policy layer,
and a staged path toward voice, browser automation, external-AI delegation, persistent memory,
and a real offline WiX MSI installer.

> Current status: **Stages 1–3 in progress; Windows CI passing, including the automated
> Notepad milestone harness.** This repository is not yet a release and does not yet contain a
> completed `CIPHER.msi`. Interactive WPF/desktop tests on a physical Windows session are still
> required before release.

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
- Built-in informational commands: help, time, date, and system information.
- Automated tests for the parser, policy, wake-phrase handling, and task engine.
- Windows batch scripts for setup checks, dependency checks, build, test, package gating, and
  clean.
- GitHub Actions workflow that restores, builds, tests, and publishes a validation build on a
  Windows runner.

## What is not implemented yet

The following are planned stages, not current claims:

- Vosk offline speech recognition and wake-word microphone pipeline.
- eSpeak NG speech output.
- SQLite persistent memory and crash/task recovery.
- Playwright browser automation.
- External AI delegation.
- Windows Graphics Capture/OCR fallback.
- Performance benchmark report against the target budgets.
- Final WiX `CIPHER.msi` and clean-machine offline install/uninstall verification.

The UI's microphone button currently reports the voice status honestly instead of pretending to
listen.

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
src\CIPHER.App\bin\Release\net10.0-windows\CIPHER.exe
```

Or with the .NET CLI:

```powershell
dotnet restore CIPHER.sln
dotnet build CIPHER.sln -c Release
dotnet test CIPHER.sln -c Release
```

The WPF application must be built/run on Windows. The platform-independent core and its tests are
kept separate so command/security behavior can evolve without coupling every rule to WPF.

## Batch scripts

| Script | Purpose |
|---|---|
| `setup-dev.bat` | Checks OS architecture, Git, .NET 10 SDK, WiX, and prepares local folders. It does not silently install software. |
| `install-dependencies.bat` | Verifies the staged offline dependency bundle. It never downloads dependencies and fails closed when payloads are missing. |
| `build.bat` | Restores and builds the solution with logging and exit codes. |
| `test.bat` | Runs automated tests, writes a TRX file, and creates a test report. |
| `package.bat` | Gates packaging on dependency/installer readiness. It will not create a fake MSI. |
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
  docs/
  dependencies/       Offline dependency staging instructions (large files not in Git)
  .github/workflows/  Windows build/test pipeline
```

Future modules such as `CIPHER.Voice`, `CIPHER.Memory`, `CIPHER.Browser`, and
`CIPHER.Installer` will be added only when their implementations are real enough to build and
test; empty placeholder projects are intentionally avoided.

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
- [Changelog](CHANGELOG.md)

## License

CIPHER source code is available under the MIT License. Third-party dependency licenses will be
listed in `LICENSES/` before any bundled release is produced.
