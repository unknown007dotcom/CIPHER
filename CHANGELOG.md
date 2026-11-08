# Changelog

All notable changes to CIPHER will be documented in this file.

The format is based on Keep a Changelog, and this project intends to follow Semantic Versioning
once the first tested release exists.

## [Unreleased]

### Added

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
