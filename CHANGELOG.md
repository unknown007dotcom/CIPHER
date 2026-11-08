# Changelog

All notable changes to CIPHER will be documented in this file.

The format is based on Keep a Changelog, and this project intends to follow Semantic Versioning
once the first tested release exists.

## [Unreleased]

### Added

- Stage 8 external AI delegation: `ask ai …` / `delegate …` commands backed by a
  `CIPHER.Delegation` module (OpenAI-compatible client, cloud or local endpoints).
  Policy requires explicit approval of the exact prompt every time; the API key is
  entered only via the Settings password box and stored DPAPI-encrypted (never in
  settings, the database, logs, or command history). Replies are saved to the Results
  folder and read-back verified; AI-suggested commands are surfaced for manual review
  and never executed automatically. A loopback fake-endpoint harness verifies the full
  pipeline in CI, including denial and fail-closed cases (run 42501585138). 97
  deterministic test cases are now covered.
- Fixed (found by audit): the Stage 6 settings toggles for wake listening and spoken
  responses were missing from the settings panel, and `SpeechOutputEnabled` was not
  persisted on save. Both are corrected.

- Stage 7 persistent memory: a `CIPHER.Memory` SQLite store (WAL, schema-migrated,
  corruption-quarantining) holding the task journal, command history, user aliases
  (`alias morning = open notepad`), and learned application paths
  (`remember app … at …`, layered over the built-in catalog). Interrupted tasks are
  recovered at startup and marked Interrupted — reported, never automatically
  re-executed. A two-process kill/restart harness verifies recovery in CI
  (run 42496132641). 84 deterministic test cases are now covered.

- Stage 6 offline voice: a `CIPHER.Voice` module with Vosk recognition (loaded only
  while listening), NAudio microphone capture, and eSpeak NG speech output. Push-to-talk
  on the microphone button, optional continuous “Hey CIPHER” wake-phrase listening
  (12-second follow-up window, off by default), visible listening state, and spoken
  result summaries when enabled. A generated-speech harness (`CIPHER.VoiceTest`)
  verifies recognition, wake-phrase gating, and speech rendering in CI — SAPI speech
  “Hey Cipher, what time is it?” recognized verbatim by Vosk and gated to the command
  `what time is it` (run 42489921302). 57 deterministic Core test cases are now covered.

- Stage 5 browser automation: a `CIPHER.Browser` module on Playwright with lazy,
  visible Chromium startup; `open website` / `read page text` / `browser click` /
  `browser fill` / `browser download` commands routed through the standard
  parser/policy/task engine. Verified form fills (read-back), accessible
  role/label element location, tab list/switch/close, and downloads saved to a
  local folder (never executed). A loopback-only browser harness
  (`CIPHER.BrowserTest`) verifies navigation, fill, submit, tabs, download
  content, and policy blocking in CI (run 42486697622). 48 deterministic Core
  test cases are now covered, including browser command policy.

- Initial .NET 10 solution for Windows 10/11 x64.
- Offline deterministic command parser with wake-phrase text handling.
- Fail-closed action policy, protected-path checks, and confirmation requirements.
- Task engine implementing understand, validate, execute, and verification-status reporting.
- Windows application launch, graceful close, UI Automation window switching, SendInput text and
  shortcut entry, and reversible file operations.
- WPF Dynamic Island-style interface with conversation history, task state, progress, cancel,
  and settings.
- xUnit tests for parser, policy, wake-word handling, and task-engine behavior.
- Windows CI workflow for restore, build, test, and validation publishing.
- Developer batch scripts and dependency/package gates.
- Architecture, compatibility, security, testing, and implementation-plan documentation.

- UI Automation editor read-back and verification commands
  (`read text in <window>`, `verify <window> contains <text>`), with typing reported as
  verified only when the typed text is observed in the editor.
- Automated Stage 3 Notepad milestone harness (`CIPHER.MilestoneTest`) covering launch,
  editor identification, typing, window switching, return, and read-back verification;
  it passes in GitHub-hosted Windows CI (non-interactive session).
- System tray icon with show/hide/exit and a Ctrl+Alt+C global show/hide hotkey.
- Named UI-element targeting: `click <name> button in <window>` and
  `type <text> into <field> in <window>` with UI Automation pattern activation,
  read-back verification, dangerous-control confirmation, and credential-field refusal.
- Stage 4 verification harness (Calculator buttons/display plus CIPHER's own window)
  passing in GitHub-hosted Windows CI; it also caught and fixed a WPF startup crash
  (TwoWay binding to a read-only property) and added a local app crash log.
- Additional parser, policy, and text-matching tests (38 automated test cases total).

### Not yet released

- Voice recognition/synthesis, SQLite memory, browser automation, external AI delegation,
  recovery, performance benchmarks, and the offline WiX MSI remain staged work.
