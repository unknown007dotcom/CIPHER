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

### Not yet released

- Voice recognition/synthesis, SQLite memory, browser automation, external AI delegation,
  recovery, performance benchmarks, and the offline WiX MSI remain staged work.
