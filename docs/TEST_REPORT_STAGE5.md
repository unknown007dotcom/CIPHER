# CIPHER Stage 5 browser automation test report

## Result

- Date: 2026-11-08
- Commit tested: `aab1239` (branch `arena/1bb3d317-cipher`)
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42486697622
- Harness: `src/CIPHER.BrowserTest` (`CIPHER.BrowserTest.exe`)
- Outcome: **PASS** — “navigation, verified form fill, submit, read-back, tabs,
  download, and policy blocking all succeeded.”
  The Stage 3 and Stage 4 harnesses also passed again in the same run.

## Scenario executed

The harness serves deterministic pages from a loopback `HttpListener`
(`127.0.0.1`, random free port — no external network, no real accounts) and drives
them through CIPHER's real command path
(parser → policy → task engine → `BrowserActionExecutor` → Playwright Chromium):

1. `open website http://127.0.0.1:<port>/form` — navigation verified by URL/title.
2. `browser fill Name with Ada Lovelace` — filled by accessible label and verified
   by reading the input value back.
3. `browser fill Email with ada@example.com` — same verified path.
4. `browser click Submit registration` — clicked by button role/name.
5. `read page text` — page text contains the generated confirmation
   “Thanks, Ada Lovelace! Registration recorded for ada@example.com.”
6. Tabs: open the details page in a second tab (title verified), switch back to the
   form tab (title verified).
7. `browser download Download hello.txt` — download saved to a local folder and
   its contents verified byte-for-byte against the served file. Downloads are
   never executed.
8. Policy checks through the engine: `browser fill Password with ...` is blocked,
   and `open website ftp://...` is blocked.

## Design points proven

- Element location prefers accessible roles, labels, placeholders, and visible
  text (DOM-level), with CSS selectors available when the user supplies one.
- Form fills are only reported as verified when the value read back from the page
  equals the requested text.
- The browser is launched lazily on the first browser command (in the desktop app
  it runs headed so the user can watch or take over); nothing browser-related is
  loaded at idle.
- Only `http`/`https` URLs open; embedded-credential URLs are blocked, credential
  fields are never filled, and dangerous-named buttons (Pay/Delete/Send…) require
  explicit confirmation.

## Limitations

- Real-account, logged-in browser workflows (the Stage 8 delegation feature) are
  intentionally not tested or enabled here; Playwright sessions here are
  CIPHER-managed and ephemeral.
- The Chromium build used by Playwright is installed by CI for the harness. The
  offline installer (Stage 10) must bundle the browser payload so end users need
  no download.
- Interactive/headed behavior on a physical Windows desktop still requires
  verification before release, as with Stages 3–4.
