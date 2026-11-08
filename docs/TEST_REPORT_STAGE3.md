# CIPHER Stage 3 Notepad milestone test report

## Result

- Date: 2026-11-08
- Commit tested: `88c9ef6` (branch `arena/1bb3d317-cipher`)
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42481087880
- Harness: `src/CIPHER.MilestoneTest` (`CIPHER.MilestoneTest.exe`)
- Outcome: **PASS** — the harness emitted its success annotation:
  “PASS - launch, editor identification, typing, window switching, and UI Automation
  read-back verification all succeeded.”

## Scenario executed

The harness drives the real CIPHER command path (`DeterministicCommandParser` →
`DefaultActionPolicy` → `TaskEngine` → `WindowsActionExecutor`) on Windows:

1. Launch Notepad with `open Notepad` and verify the process/window.
2. Identify the Notepad editor through UI Automation (readable Edit/Document control).
3. Focus Notepad with `switch to Notepad`.
4. Type `CIPHER milestone verification sentence.` through Win32 `SendInput`.
5. Read the editor back through UI Automation `ValuePattern`/`TextPattern` and confirm the
   sentence is present.
6. Switch away by opening File Explorer.
7. Return with `switch to Notepad` and confirm Notepad is in the foreground.
8. Run `verify Notepad contains CIPHER milestone verification sentence.` through the normal
   CIPHER command path and require a verified success.
9. Cleanup: select-all, delete the test sentence, and gracefully close Notepad and the
   File Explorer window.

Before typing, the harness checks that Notepad does not already contain user text and refuses
to modify an existing document. No generative LLM is involved at any step.

## Environment caveat

This pass was recorded in a GitHub-hosted `windows-latest` runner. That is a non-interactive
Windows session (session 0) rather than a physical laptop display. It proves the process,
UI Automation, SendInput, and read-back pipeline works end-to-end on real Windows, but the
milestone must still be repeated on an interactive Windows 10 and Windows 11 desktop before
release, where foreground-activation rules, DPI scaling, and the installed Notepad build can
differ.

## How to run it locally

On an interactive Windows 10/11 x64 machine with the .NET 10 SDK:

```powershell
dotnet build CIPHER.sln -c Release
.\src\CIPHER.MilestoneTest\bin\Release\net10.0-windows\CIPHER.MilestoneTest.exe
```

Exit code `0` means every scored step passed. Each step prints a `STEP: ... => PASS/FAIL`
line, followed by a final `MILESTONE RESULT` line.
