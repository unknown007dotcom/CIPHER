# CIPHER testing strategy

## Principles

- Test deterministic logic automatically on every change.
- Test Windows desktop behavior on a real interactive Windows session.
- Do not report a test as passed unless it was executed and its output recorded.
- Keep safety tests fail-closed: blocked, denied, and cancelled actions must not mutate the system.

## Automated tests in the repository

Current xUnit coverage:

- wake-phrase detection and removal;
- open/type/press/help/time/date/system-info parsing;
- read/verify window-text command parsing;
- named-element click and type-into-field command parsing (including names containing
  " in "), dangerous-control confirmation, and credential-field blocking;
- whitespace/case-tolerant text matching used for editor read-back verification;
- folder and move command parsing;
- unknown-command suggestions;
- delete confirmation requirements;
- protected Windows directory blocking;
- arbitrary shell-command blocking;
- sensitive application confirmation;
- task-engine handling of verified success, unknown commands, denied confirmation, and approved
  confirmation.

Run on Windows:

```bat
test.bat Release
```

Or:

```powershell
dotnet test CIPHER.sln -c Release
```

GitHub Actions runs restore, Release build, tests, and a framework-dependent validation publish
on `windows-latest`.

## Manual/interactive Windows tests still required

The Linux authoring sandbox cannot run WPF or Windows desktop automation. The following must be
run on Windows before their stages are marked complete:

### Dynamic Island smoke test

1. Launch CIPHER.
2. Confirm it appears at the top center of the primary display.
3. Collapse and expand it.
4. Open and save settings.
5. Run `help`, `time`, and `system info`.
6. Cancel a long-running file copy if available.
7. Close and relaunch CIPHER.

### Stage 5 browser automation

Run the loopback browser harness (serves deterministic pages on `127.0.0.1`; no
external network):

```powershell
pwsh src\CIPHER.BrowserTest\bin\Release\net10.0\playwright.ps1 install chromium
src\CIPHER.BrowserTest\bin\Release\net10.0\CIPHER.BrowserTest.exe
```

Exit code `0` with `STAGE5-PASS` means navigation, verified form fill, submit,
read-back, tab switching, download content verification, and policy blocking (no
credential-field fills; no non-http(s) schemes) all succeeded through the real
command path. It passes in GitHub-hosted Windows CI with headless Chromium; see
`docs/TEST_REPORT_STAGE5.md`.

### Stage 3 Notepad milestone

This milestone is automated by `src/CIPHER.MilestoneTest` and runs in GitHub-hosted Windows
CI. On 2026-11-08 it passed end-to-end in the runner's non-interactive session
(run 42481087880; see `docs/TEST_REPORT_STAGE3.md`). The same harness should be run on an
interactive Windows desktop before release:

```powershell
dotnet build CIPHER.sln -c Release
.\src\CIPHER.MilestoneTest\bin\Release\net10.0-windows\CIPHER.MilestoneTest.exe
```

It launches Notepad, identifies the editor through UI Automation, types
`CIPHER milestone verification sentence.`, switches to File Explorer, returns to Notepad, and
verifies the sentence through UI Automation read-back plus the
`verify Notepad contains ...` command. Typing also reports itself as verified only when the
typed text is observed in the editor afterwards. The harness refuses to run if Notepad already
contains user text.

### Stage 4 named-element targeting

Automated by `CIPHER.MilestoneTest.exe stage4 --app <path-to-CIPHER.exe>` and passing in
GitHub-hosted Windows CI (see `docs/TEST_REPORT_STAGE4.md`). It clicks named Calculator
buttons (5 + 3 =), verifies the displayed result through UI Automation, types into
CIPHER's own named command field with read-back verification, and clicks CIPHER's Clear
button. This harness caught a real WPF startup crash (a TwoWay binding against a
read-only property), which is why shell-level harnesses matter even when unit tests pass.

### Safety tests

- Deny a delete confirmation and confirm the source remains unchanged.
- Approve a delete and confirm the item appears in `%LocalAppData%\CIPHER\Quarantine`.
- Attempt to delete a protected Windows directory and confirm CIPHER blocks it.
- Attempt `run command ...` and confirm CIPHER blocks arbitrary shell execution.
- Close an application with unsaved data and confirm CIPHER requests a graceful close but does not
  force-kill it.

## Performance testing

No performance result is claimed yet. Stage 11 must measure on representative modest hardware:

- idle RAM and CPU after 10 minutes;
- normal RAM during desktop and browser tasks;
- peak RAM during voice/browser work;
- parser and memory-retrieval latency;
- simple command latency;
- application launch overhead;
- installer size and installation time.

Publish the exact hardware, Windows build, CIPHER commit, and measurement method with results.
