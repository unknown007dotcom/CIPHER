# Third-party license notices

CIPHER's own source code is MIT-licensed (see the repository `LICENSE`).

## Current component inventory (Stage 9)

Exact versions are pinned in the project files; this inventory is the distribution checklist
for the Stage 10 installer, which must drop each component's verbatim license text into
`licenses/` next to the binaries it ships.

| Component | Version | Used by | License (verify verbatim text at packaging) |
| --- | --- | --- | --- |
| .NET runtime / SDK libraries | 10.0 | Everything | MIT / .NET Foundation terms |
| Microsoft.Data.Sqlite (+ SQLitePCLRaw / e_sqlite3 native) | 10.0.0 | `CIPHER.Memory` | MIT (SQLite itself is public domain) |
| Microsoft.Playwright (+ driver; Chromium downloaded separately) | 1.49.0 | `CIPHER.Browser` | Apache-2.0 (Chromium: BSD-style, multi-license) |
| NAudio | 2.2.1 | `CIPHER.Voice` | MIT |
| Vosk (managed wrapper + native libvosk/Kaldi) | 0.3.38 | `CIPHER.Voice` | Apache-2.0 |
| Vosk English small model (`vosk-model-small-en-us-0.15`) | 0.15 | Runtime speech data | Apache-2.0 (per model card; re-verify before bundling) |
| System.Security.Cryptography.ProtectedData | 10.0.0 | `CIPHER.Delegation` | MIT |
| xUnit / xUnit runner | 2.9.3 / 2.8.2 | Tests only — never shipped | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | Tests only — never shipped | MIT |
| eSpeak NG (CI test tool; future bundled TTS) | CI-downloaded | `CIPHER.Voice` fallback | **GPL-3.0** — bundling has copyleft obligations; legal review required before Stage 10 ships it |

## Rules before any release installer

- The installer must include the exact license/notice file for every bundled binary and model,
  including WiX-generated components where notices are required.
- Licensing must be reviewed, not assumed: eSpeak NG's GPL obligations and the selected Vosk
  model's redistribution terms are explicitly flagged above.
- Optional llama.cpp components are not used today; if ever bundled, their MIT license and any
  model licenses must be added here first.
