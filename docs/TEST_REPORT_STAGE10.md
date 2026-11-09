# CIPHER Test Report — Stage 10 (Real Offline WiX MSI)

**Branch:** `arena/5bf6306f-cipher`
**Verified commit:** `0f88c0c` ("Emit MSI directly to artifacts via WiX OutputPath")
**CI verification:** GitHub Actions `windows-latest` — push run `42624072388` ✓ fully green,
including every prior stage's harness (Stages 3–9) unchanged.
**Artifact:** `CIPHER.msi`, 364,007,213 bytes (~347 MiB), produced by `package.bat Release`.

## What Stage 10 delivered

- **WiX v5 installer** (`src/CIPHER.Installer`, WixToolset.Sdk 5.0.2): per-user x64 package
  (Scope="perUser", matching the app's `asInvoker` manifest) harvesting the self-contained
  published app plus `models\`, `tools\espeak-ng\`, `playwright\browsers\`, and `licenses\`
  under `%LocalAppData%\CIPHERProgram` — deliberately separate from CIPHER's data root
  (`%LocalAppData%\CIPHER`), which uninstall leaves untouched. Start menu shortcut,
  MajorUpgrade with downgrade protection, embedded CAB. The project sits in `CIPHER.sln`
  without a default build configuration, so ordinary solution builds skip it; `package.bat`
  builds it after publishing.
- **Real packaging script**: `package.bat` now verifies the bundle, publishes
  `CIPHER.App` self-contained (win-x64), builds the installer (MSI emitted straight to
  `artifacts\CIPHER.msi`), and fails with distinct exit codes per stage. It no longer stops
  at the "not implemented" gate — that placeholder is gone.
- **Offline dependency staging with integrity**: `scripts/stage-dependencies.ps1` (the only
  downloading step; developer/CI action) stages the Vosk model, native `libvosk.dll` from
  the restored NuGet package, eSpeak NG from its upstream MSI, Chromium from the local
  Playwright cache, and the three license notices, then regenerates
  `dependencies/manifest.sha256`. `install-dependencies.bat` enforces payload presence plus
  every SHA-256 hash (via `scripts/verify-dependencies.ps1`, hashing through .NET so it
  works under both `powershell.exe` and `pwsh`) before packaging may proceed.
- **Installed-layout path resolution** (the audit's mismatches, fixed): Playwright honors a
  bundled `playwright\browsers` via `PLAYWRIGHT_BROWSERS_PATH` (`BrowserSessionOptions`);
  eSpeak probes `tools\espeak-ng\` with the exe at root or `bin\` and data as child or
  sibling (`ESPEAK_DATA_PATH`); the Vosk locator probes `models\` and `vosk\` with both
  model folder names. Architecture is locked (`PlatformTarget=x64` in App + Voice;
  `RuntimeIdentifier=win-x64` in App), `global.json` allows prerelease SDKs, the
  single-instance mutex is per-user scoped, dispatcher exceptions are logged and handled,
  and window close now hides to tray with tray Exit performing an *awaited* shutdown
  (browser/memory cleanup completes before process exit).

## CI evidence (run 42624072388)

| Step | Result |
| --- | --- |
| Stage offline dependencies (staging + manifest) | ✓ (payloads cached across runs; downloads retry with backoff) |
| Build CIPHER.msi via package.bat | ✓ MSI 364,007,213 bytes |
| Verify MSI payload (administrative extraction) | ✓ `CIPHER.exe`, `models\vosk-model-small-en-us-0.15\conf\model.conf`, `tools\espeak-ng\espeak-ng.exe`, three license files, bundled `chrome.exe` all present |
| Install → verify → uninstall | ✓ installed `CIPHER.exe` under `%LocalAppData%\CIPHERProgram`, Start menu shortcut present, `CIPHERProgram` fully removed after uninstall |
| All prior gates | ✓ 119 deterministic tests; Stages 3/4/5/6/7/8/9 harnesses pass |

Notable build decisions, recorded for reviewers: ICE38 and ICE64 are suppressed in the
installer project — both are structural for per-user packages built from harvested file
components (file KeyPaths; generated directories without RemoveFile rows). Correctness is
instead proven functionally by the install/uninstall cycle above, which passed.

## What remains before a *release* (not built here)

The plan's clean-machine exit gate still requires human-run validation that CI cannot
substitute: install on clean offline Windows 10 and Windows 11 VMs, launch the installed
app, run local commands and the Notepad milestone from the installed copy, then uninstall
and confirm data-folder behavior. Also outstanding from earlier stages: interactive desktop,
headed-browser, real-microphone/speaker, DPI/multi-monitor validation, real-provider
delegation, and the Stage 11 performance measurements (idle RAM/CPU, command latency,
installer size/time — the 347 MiB figure above is the first measured input). No
bootstrapper was needed: every prerequisite ships inside the single MSI.
