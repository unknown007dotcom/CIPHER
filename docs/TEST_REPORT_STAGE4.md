# CIPHER Stage 4 named-element targeting test report

## Result

- Date: 2026-11-08
- Commit tested: `a5cab316932614f6429e5346b293dad023097f72`
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42485111973
- Harness: `CIPHER.MilestoneTest.exe stage4 --app <CIPHER.exe>`
- Outcome: **PASS** — “named button clicking and named field entry verified.”
  The Stage 3 Notepad milestone also passed again in the same run.

## Scenario executed

All actions go through the normal CIPHER command path
(parser → policy → task engine → Windows executor):

### Calculator (third-party native UI)

1. `open Calculator`
2. `click Clear button in Calculator`
3. `click 5 button in Calculator` (falls back across naming variants: “Five”/“5”)
4. `click Add button in Calculator` (“Plus”/“Add” variants)
5. `click 3 button in Calculator`
6. `click Equals button in Calculator`
7. Verify the display shows `8` by reading UI Automation text elements.

The runner's Calculator is a legacy desktop build whose buttons are named `5`, `Add`,
`3`, and whose result is exposed as plain text elements; the harness handles both that
and the modern `Display is 8` / `CalculatorResults` forms.

### CIPHER's own WPF window

1. Launch `CIPHER.exe` and wait for its window.
2. `type STAGE4-FIELD-OK into CIPHER command input in CIPHER` — the executor sets the
   field through `ValuePattern` and verifies by read-back.
3. `click Clear button in CIPHER` — activated through `InvokePattern`.

## Real bug found and fixed by this harness

The first Stage 4 run exposed a startup crash in CIPHER itself: the WPF `ProgressBar`
binding defaulted to TwoWay against the read-only `ProgressValue` view-model property
(`InvalidOperationException`, exit code `-532462766`). Fixed by making the binding
explicitly `Mode=OneWay`, and CIPHER now logs unhandled exceptions to
`%LocalAppData%\CIPHER\logs\app-errors.log` (exception text only) so future field
failures are diagnosable.

## Safety behavior covered by unit tests

- Clicking a dangerous-named control (Delete/Send/Pay/…) requires explicit confirmation.
- Typing into a credential-named field (password/PIN/card) is blocked by policy, and
  the executor independently refuses controls whose `IsPassword` property is set.
- Clicks are reported as verified only when an observable toggle/selection/expansion
  state actually changes.

## Environment caveat

As with Stage 3, this pass was recorded in a GitHub-hosted non-interactive Windows
session. Interactive Windows 10/11 verification is still required before release.
