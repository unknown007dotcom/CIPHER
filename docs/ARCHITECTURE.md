# CIPHER architecture

## Goals

- Offline-first local operation.
- Deterministic, inspectable commands before any optional generative intelligence.
- Small idle footprint: do not load speech, browser, OCR, or inference engines until needed.
- Fail closed for destructive or privileged actions.
- Report verification honestly: an action that was sent is not automatically an action that was
  verified.

## Current modules

```text
CIPHER.App (WPF, net10.0-windows)
  └─ tray + global hotkey shell, settings UI, DPAPI key entry, --safe-mode
     references Core, Actions, Browser, Voice, Memory, Delegation

CIPHER.Core (net10.0) — deterministic kernel, no platform dependencies
  ├─ DeterministicCommandParser / WakeWordDetector / TextMatching
  ├─ DefaultActionPolicy (fail-closed) / PathSafety / ApplicationCatalog
  ├─ TaskEngine (parse → policy → confirm → execute → verify → journal)
  ├─ MemoryCommandHandler (aliases/app paths re-enter the full pipeline)
  ├─ AuditLog (hash-chained, rotating) / SensitiveDataRedactor
  └─ BuiltInActionExecutor / AssistantSettings + JsonSettingsStore

CIPHER.Actions (net10.0-windows) — WindowsActionExecutor, QuarantineService
  (UI Automation, SendInput, process and file operations; graceful close only)

CIPHER.Browser (net10.0) — Playwright sessions, BrowserActionExecutor
  (lazy start; downloads saved, never executed; password inputs refused)

CIPHER.Voice (net10.0-windows) — Vosk recognition, NAudio capture, eSpeak NG output

CIPHER.Memory (net10.0) — SqliteMemoryStore: task journal with interrupted-task
  recovery, history, aliases, learned app paths, corruption quarantine

CIPHER.Delegation (net10.0) — opt-in OpenAI-compatible client, DPAPI key store,
  verified-artifact executor (suggestions never auto-executed)

Test harnesses (net10.0): CIPHER.Tests (xUnit), CIPHER.MilestoneTest (Stages 3/4/9),
CIPHER.BrowserTest, CIPHER.VoiceTest, CIPHER.MemoryTest, CIPHER.DelegationTest,
CIPHER.SecurityTest — see docs/TESTING.md
```

Only the planned Vision/OCR module and the Stage 10 installer remain unbuilt; modules are
added only with working implementations (see `docs/IMPLEMENTATION_PLAN.md`).

## Task flow

```text
UNDERSTAND
  DeterministicCommandParser converts text into a ParsedCommand.

OBSERVE / PLAN
  The executor resolves the current process, window, or path state at execution time.
  Browser and richer desktop planning will extend this step in later stages.

VALIDATE
  DefaultActionPolicy blocks unknown/arbitrary/protected actions and marks high-risk actions
  for explicit confirmation.

EXECUTE
  BuiltInActionExecutor handles informational commands.
  WindowsActionExecutor handles applications, windows, keyboard input, and files.

VERIFY
  Executors set ActionResult.Verified only when they check resulting state, such as a process,
  window focus, directory, or destination path. Text typing is reported as unverified because
  the focused control's content is not generically readable.

REMEMBER
  Stage 7 will persist preferences, tasks, and recovery checkpoints in SQLite. Stage 1 uses a
  local JSON settings file only for non-sensitive preferences.
```

## Verification semantics

`ActionResult.Success` means the executor completed its operation without an error.
`ActionResult.Verified` means it independently observed the expected resulting state.

The task engine maps these to:

- `Succeeded` — success and verified;
- `SucceededUnverified` — success but no independent verification;
- `Failed` — executor failure;
- `Blocked` — policy or missing-confirmation failure;
- `Cancelled` — user cancellation or denied confirmation.

The UI must preserve this distinction in its wording.

## Security boundaries

- The WPF application runs as `asInvoker`.
- Arbitrary shell execution is recognized by the parser only so the policy can block it clearly.
- Deletes move to `%LocalAppData%\CIPHER\Quarantine` with a timestamped name.
- Applications are closed via their main window; CIPHER does not escalate to force-kill in v1.
- Settings never contain passwords, cookies, or access tokens.

## Performance rules

- No polling loops except short, bounded verification waits during an active task.
- No speech/browser/OCR model is loaded by the Stage 1 application.
- File listing is capped at 200 displayed entries.
- Heavy modules must be loaded on demand and released when idle.
- Benchmarks must be measured on Windows hardware before performance claims are published.
