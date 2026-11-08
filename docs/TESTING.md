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

### Stage 3 Notepad milestone

1. Say or type: `open Notepad`.
2. Confirm Notepad is running and focused.
3. Type: `type CIPHER milestone test 2026`.
4. Switch to another window.
5. Return with `switch to Notepad`.
6. Verify through UI Automation that the editor still contains the exact sentence.
7. Close Notepad gracefully and discard test data when prompted by Windows.

The UI Automation read-back verification step is part of Stage 3 hardening; the current executor
types through SendInput and reports typing as unverified until that read-back is implemented and
tested.

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
