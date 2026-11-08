# CIPHER Stage 8 external AI delegation test report

## Result

- Date: 2026-11-08
- Commit tested: `28132d0` (branch `arena/1bb3d317-cipher`)
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42501585138
  (push) and 42501590417 (pull request) — both fully green
- Harness: `src/CIPHER.DelegationTest` (`CIPHER.DelegationTest.exe`)
- Outcome: **PASS** — “approved delegation answered with a verified artifact; suggestions
  surfaced without execution; denial/disabled/missing-key all failed closed before HTTP.”
  All earlier harnesses (Stages 3–7) also passed, and the deterministic suite grew from
  84 to 97 cases. (One earlier run hung on a harness server-shutdown bug and another on
  a fake-server matching bug; both were harness bugs, fixed and re-verified.)

## Scenario executed

A loopback fake AI endpoint (OpenAI-compatible, canned answers, no external network, no
real key) is driven through the real parser → policy → confirmation → executor pipeline:

1. **Approved delegation**: `ask ai what is the capital of France` → the canned answer is
   returned and saved to a local artifact file whose read-back matches, so the result is
   reported as verified (exit-gate artifact).
2. **Suggestion handling**: a reply containing `COMMAND: what time is it` surfaces the
   suggestion in the result data with an explicit “NOT executed” note; nothing runs it.
3. **Denial**: with confirmation denied, the endpoint receives **zero** requests.
4. **Fail-closed config**: delegation disabled, and a remote endpoint without a saved
   key, both refuse before any HTTP is attempted.

Unit tests additionally cover the HTTP client (URL, bearer header, body, error mapping),
the executor, and a DPAPI round-trip proving the stored key file never contains the
plaintext key.

## What the application does with this

- `ask ai <question>` / `delegate <task>` (deterministic commands). Policy marks every
  delegation **High risk, always requiring confirmation**, and the confirmation dialog
  previews the exact prompt — approving is what sends it. Core CIPHER remains fully
  functional offline with delegation off (the default).
- Any OpenAI-compatible endpoint works (cloud or a local Ollama/LM Studio server;
  loopback endpoints may be keyless). The API key is entered only in the Settings
  password box and stored DPAPI-encrypted — never typed into chat, because typed
  commands are journaled. The key is never displayed, logged, or stored in settings/DB.
- Replies are saved to the Results folder with prompt/endpoint/model metadata and
  read-back verified. A suggested command is placed in the input box for the user to
  review and explicitly Send (it then goes through the normal policy/confirmation flow).

## Deferred, honestly

- **Browser-based AI platforms** (driving chat web UIs as the delegation transport) are
  deferred: they depend on fragile third-party page structures and brush against
  CAPTCHA/MFA/platform-terms boundaries that CIPHER must not bypass. API delegation
  provides the capability without those risks.
- Delegated multi-step desktop workflows (AI plans a sequence, CIPHER executes with
  per-step verification) are future work; v1 is single-request delegation with a
  verified artifact.

## Also fixed this stage (found by code audit)

Two Stage 6 UI gaps introduced by a parallel-edit accident were found and repaired:
the settings panel's wake-listening and speak-responses toggles were missing from the
XAML (ViewModel support existed but was unreachable), and `SpeechOutputEnabled` was
loaded at startup but never written back by Save Settings. Both are in place now; the
Stage 6 report predates the discovery.
