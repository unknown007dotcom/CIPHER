# Bundled dependency staging area

This directory holds the offline payloads bundled into `CIPHER.msi` (Stage 10). Large
binaries and models are **not** committed to ordinary Git history — stage them locally or
in CI with:

```powershell
dotnet restore CIPHER.sln   # populates the NuGet cache (libvosk source)
pwsh scripts/stage-dependencies.ps1
```

The staging script produces exactly this layout and regenerates `manifest.sha256`
(SHA-256 for every staged file). `install-dependencies.bat` verifies payload presence
plus every hash before `package.bat` is allowed to build the MSI. Nothing is ever
downloaded at install or packaging time — downloads happen only in the staging step.

```text
dependencies/
  manifest.sha256
  vosk/
    vosk-model-small-en-us-0.15/   (am/, conf/model.conf, graph/, ivector/, ...)
    native/win-x64/libvosk.dll
  espeak-ng/
    espeak-ng.exe
    libespeak-ng.dll               (when shipped by the upstream MSI)
    espeak-ng-data/                (phontab, phondata, ...)
  playwright/
    browsers/
      chromium-<revision>/chrome-win/chrome.exe
  licenses/
    LICENSE-VOSK.txt
    LICENSE-ESPEAK-NG.txt
    LICENSE-PLAYWRIGHT.txt
```

How the running app finds these after installation (installer layout =
`<install folder>` mirrors the app base directory):

- Vosk model → `models\` (probed by `VoskModelLocator`, which also accepts a `vosk\`
  folder and the `model-en-us-small` alias name);
- eSpeak NG → `tools\espeak-ng\` (`EspeakSpeechSynthesizer` probes the exe in the
  folder root or `bin\`, and `espeak-ng-data` as a child or sibling directory);
- Chromium → `playwright\browsers\` (`BrowserSessionOptions.BrowsersPath`, exported as
  `PLAYWRIGHT_BROWSERS_PATH` before the driver starts);
- License notices → `licenses\` (also inventoried in `LICENSES/README.md`).
