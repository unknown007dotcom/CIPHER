# CIPHER implementation plan

## Delivery principle

Build CIPHER incrementally. Every stage must leave the repository buildable, tested, and honest:
no empty placeholder projects, no fake installer, no invented benchmark or test result, and no
feature described as complete until it has been exercised on Windows.

## Current repository state found in Stage 1

- The repository initially contained only an MIT `LICENSE`.
- GitHub is the source of truth on the active Arena development branch.
- Dropbox is available for later backups/release archives, but no Dropbox upload is needed for
  source-only Stage 1 work.
- The current development sandbox is Linux and cannot compile or run WPF. Authoritative builds
  therefore run on Windows through GitHub Actions and, for interactive desktop automation, a real
  Windows 10/11 x64 test machine or VM.

## Stage plan and gates

### Stage 1 — Repository inspection and compatibility foundation

Deliverables:

- [x] Inspect repository and preserve the existing MIT license.
- [x] Confirm preferred stack and staged architecture.
- [x] Create solution/project structure for implemented modules only.
- [x] Add Windows CI for .NET 10 restore/build/test.
- [x] Add root batch scripts with logging, exit codes, and fail-closed packaging gates.
- [x] Document compatibility, dependency risks, and installer constraints.
- [x] Pass Windows CI (GitHub Actions run 42480109478, 2026-11-08).
- [ ] Run the WPF shell interactively on a Windows machine and record the result.

Exit gate: solution builds on Windows; tests pass; known limitations are documented.

### Stage 2 — WPF Dynamic Island interface

Implemented foundation:

- [x] Floating borderless top-centered window.
- [x] Expand/collapse, status orb, conversation, progress, cancel, and settings panel.
- [x] Click-to-focus command input and Enter-to-send.
- [ ] Global show/hide hotkey.
- [ ] Tray icon and restore behavior.
- [ ] Multi-monitor/DPI placement tests.
- [ ] Low-resource idle measurement.

Exit gate: responsive UI on Windows 10 and 11 at 100%, 125%, and 150% scaling.

### Stage 3 — Native Windows automation

Implemented foundation:

- [x] Known-application catalog and process launching.
- [x] Graceful close without force-kill.
- [x] UI Automation top-level window discovery/focus.
- [x] SendInput Unicode typing and shortcuts.
- [x] File/folder create, list, copy, move, rename, and reversible delete.

Required milestone test on a real interactive Windows desktop:

1. Launch Notepad.
2. Identify its editor through UI Automation.
3. Type a known sentence.
4. Switch to another window.
5. Return to Notepad.
6. Read the editor value through UI Automation and verify the exact sentence remains.

Exit gate: milestone passes repeatedly without a generative LLM; failures report the failed step.

### Stage 4 — Deterministic command processing

Implemented foundation:

- [x] Wake-phrase text stripping.
- [x] Bounded deterministic intent grammar.
- [x] Application/window/input/file/information intents.
- [x] Confidence values and examples for unknown commands.
- [ ] User-defined application aliases persisted in memory.
- [ ] Richer UI Automation element targeting (`click Save button`, named text fields).
- [ ] Command disambiguation prompts.

Exit gate: parser/policy regression suite passes and ambiguous commands do not execute blindly.

### Stage 5 — Browser automation

Planned:

- Add Playwright for .NET in an isolated module.
- Prefer DOM/accessibility selectors; screenshots/OCR only as fallback.
- Support navigation, reading, click, fill, upload/download, tabs, and verification.
- Reuse the user's existing browser session only where the selected browser/profile design and
  platform terms permit it. Do not extract or store passwords/cookies in plaintext.

Exit gate: deterministic local test pages pass for navigation, form fill, download, and result
verification.

### Stage 6 — Voice recognition and speech output

Planned:

- Vosk offline recognition loaded only while listening.
- "Hey CIPHER" wake phrase with click-to-activate fallback.
- eSpeak NG output with configurable voice/language.
- Visible microphone state and immediate stop/cancel.
- Model packaging and license review.

Exit gate: offline command recognition test set measured on CPU-only hardware; no model is loaded
at idle unless the user enables always-listening.

### Stage 7 — SQLite memory and recovery

Planned:

- Preferences, application paths, tasks, task steps, history, and recovery checkpoints.
- Database migrations and corruption handling.
- No plaintext passwords, cookies, or credentials.

Exit gate: kill/restart tests recover interrupted tasks without repeating destructive steps.

### Stage 8 — External AI delegation

Planned:

- Configurable platforms and user-approved browser workflows.
- Upload/send only after explicit permission for sensitive files.
- Monitor state, surface clarifications, retrieve outputs, save locally, and verify artifacts.
- Never bypass CAPTCHA, MFA, access controls, or platform restrictions.
- Never claim completion without checking the resulting output.

Exit gate: end-to-end delegated task produces a verified local artifact and an accurate report.

### Stage 9 — Security and recovery hardening

Implemented foundation:

- [x] Fail-closed policy and protected paths.
- [x] Confirmation for high-risk actions.
- [x] No arbitrary shell execution.
- [x] Reversible quarantine delete.
- [x] Graceful close rather than force-kill.
- [ ] Structured local audit log with retention controls.
- [ ] Recovery UI for quarantine and interrupted tasks.
- [ ] Threat-model review for browser/delegation workflows.

Exit gate: security test cases prove blocked, cancelled, and failed actions do not mutate data.

### Stage 10 — Real offline WiX MSI

Planned deliverable: `CIPHER.msi` built with WiX Toolset.

The MSI must contain the self-contained application, native libraries, Vosk model, eSpeak NG,
configuration, licenses, shortcuts, and uninstall support. It must not download dependencies. A
`CIPHER-Setup.exe` bootstrapper is created only if a genuine machine-level prerequisite cannot be
included in the MSI.

Exit gate: on a clean offline Windows 10 and Windows 11 x64 VM:

1. Install from `CIPHER.msi`.
2. Launch CIPHER.
3. Run local commands.
4. Run the Notepad milestone.
5. Uninstall.
6. Verify files, shortcuts, and settings behavior match the documented policy.

### Stage 11 — Performance and release

Measure, do not estimate:

- Idle/normal/maximum RAM.
- Idle CPU.
- Memory retrieval latency.
- Simple command latency.
- Application launch overhead.
- Installer size and install time.

Exit gate: publish only with verified artifacts, hashes, test results, benchmark report,
changelog, dependency manifest, and license notices.

## Dependency size and risk register

These are planning estimates to be replaced by measured package inventories before release:

| Dependency | Estimated payload | Main risks |
|---|---:|---|
| Self-contained .NET 10 runtime + WPF | ~70–120 MB | Installer size, Windows version support, trimming incompatibilities |
| Vosk native runtime | ~20–50 MB | CPU instruction compatibility, native load failures, model licensing |
| Small English Vosk model | ~40 MB | Recognition quality, redistribution terms |
| eSpeak NG + data | ~20–40 MB | GPL obligations, voice quality |
| Playwright browser payload | ~150–200 MB | Size, browser updates, profile/session restrictions |
| SQLite | ~1–3 MB | Migration and corruption handling |
| Windows OCR/Graphics Capture | OS-provided | Windows edition/version differences |
| Optional llama.cpp/model | Hundreds of MB to GB | Performance and RAM; excluded from v1 default |

Key technical risks:

1. GitHub-hosted runners can build WPF but are not a substitute for interactive desktop tests.
2. Windows foreground-focus restrictions can make `SetForegroundWindow` report failure.
3. UI Automation trees differ across Win32, WPF, WinUI, Electron, and UWP applications.
4. SendInput cannot interact with secure desktops, UAC prompts, or higher-integrity windows from
   a normal-integrity process.
5. Playwright's bundled browser may not share a user's existing logged-in profile; using a real
   profile requires careful design and user consent.
6. Bundling speech/browser payloads can make the MSI large. Payloads must be measured and may
   require a bootstrapper only if a real prerequisite cannot be included.
7. Third-party licenses, especially eSpeak NG and speech models, must be reviewed before release.
