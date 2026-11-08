# CIPHER full verification report (post-Stage 9 audit)

**Date:** 2026-11-08
**Audited state:** branch `arena/1bb3d317-cipher` at `19c977b` (Stage 9 complete)
**Method:** line-by-line review of the deterministic kernel (parser, policy, task engine),
all four executors, memory/delegation/browser/voice layers, the WPF shell, every test and
harness, the CI workflow, the batch scripts, and every user-facing document; structural
cross-checks scripted against the source (intent matrix, project graph, test counts).
CI (GitHub Actions, `windows-latest`) remains the build/test authority — there is no local
.NET SDK in the audit environment.

## Repository state verified

- Working tree clean; PR #1 (draft) open against `main`; final Stage 9 push/PR runs
  `42505438540` / `42505442999` green at `19c977b`.
- 112 tracked files, no binaries or models in Git (largest tracked file: 41 KB of source);
  `.gitignore` covers build output, databases, settings, models, and installers.
- Solution consistency: 14 projects on disk = 14 in `CIPHER.sln`; reference graph is
  acyclic and layered exactly as `docs/ARCHITECTURE.md` describes (Core is the leaf).
- App manifest is `asInvoker` with `uiAccess="false"` — no elevation, no UI bypass.
- Test inventory independently recounted: **118** deterministic cases before the fixes
  below (97 pre-Stage-9 + 21 Stage 9), matching the stage reports; no skipped or
  commented-out tests anywhere.

## Intent coverage matrix (scripted cross-check)

32 `IntentKind` values; the parser produces 31 (all but `Unknown`, the fallback). The
policy switch handles every intent explicitly with a **fail-closed default**
(`Block`). Executor coverage: BuiltIn 5, Windows 19, Browser 5, Delegation 1 = 30;
`RunCommand` (31st) intentionally has **no executor** and is blocked by policy at
Critical risk. Every executor switch ends in an `UNSUPPORTED_INTENT` failure — there is
no path where an allowed command silently does nothing and reports success.

## Area-by-area results

| Area | Verdict |
| --- | --- |
| TaskEngine pipeline | Parse→policy→confirm→execute→verify order confirmed; blocked/denied/missing-confirmation/missing-executor paths never touch an executor; journal writes are best-effort and redacted |
| DeterministicCommandParser | Fully read (597 lines). Precedence verified per rule (incl. the Stage 9 fix: quarantine/audit before the generic list rule). Known quirk, documented: `go to <site>` resolves as SwitchWindow because that rule precedes OpenWebsite; `open website <site>` is the browser form |
| DefaultActionPolicy | Every branch read; protected-path checks on delete/move/rename/copy-destination/create; sensitive catalog apps, executable opens, and dangerous shortcuts require confirmation; credential field names blocked |
| WindowsActionExecutor | Every method read. No force-kill anywhere; deletes are quarantine moves; no-overwrite on move/copy/create/restore; password controls refused at runtime (`IsPassword`); text entry verified by read-back before claiming `verified` |
| Browser layer | Lazy session, serialized commands; downloads saved with executable warning, never run; navigation verified by URL/host; scheme and embedded-credential URLs blocked by policy |
| Delegation layer | Disabled ⇒ fail before HTTP; remote ⇒ key required; loopback keyless allowed; reply artifact read-back verified; `COMMAND:` suggestions surfaced, never executed; harness request-counting proves zero calls on denial |
| Memory layer | All SQL parameterized; corrupt DB + WAL quarantined and recreated; interrupted tasks marked, never re-run; aliases expand once and re-enter full policy/confirmation |
| Voice layer | Model/microphone only created while listening; wake gating in parser layer; eSpeak child-process kill is limited to CIPHER's own tracked process on timeout |
| App shell | Confirmation dialog defaults to **No** and shows the full target (exact prompt for delegation); API key only via password box → DPAPI; hotkey conflicts reported, not fatal |
| AuditLog/Redactor | Chain math, rotation, cross-instance continuation, tamper/removal detection verified by tests + cross-process harness; hash payload field boundaries unambiguous |
| CI workflow | Step commands match build outputs; gating steps (build, xUnit, Stage 7, 8, 9-audit) throw on failure; `package.bat` gate proves packaging still refuses to build an installer early |
| Batch scripts | Fail-closed checks (missing solution/SDK/dependencies exit non-zero with logs) |

## Findings and dispositions

All fixed in the verification commit that accompanies this report unless noted:

1. **Browser fill had no DOM-level password guard** (name-based policy only). A
   password input with an innocent label could be filled. → `FillAsync` now refuses
   `type=password` inputs (`BrowserCredentialFieldException` → `CREDENTIAL_FIELD`), and
   the Stage 5 harness gained a step that proves it with an innocently-labeled field.
2. **Quarantine restore trusted recorded origins absolutely.** A tampered sidecar could
   steer a restore into a protected path. → Restores to protected destinations now fall
   back to the Recovered folder; the stage9 harness proves it with a forged sidecar.
3. **Threat model overstated delegation limits** ("30 s timeout and response size cap";
   code had a 90 s timeout and no cap). → The client now enforces a 1 MB response cap
   and the threat model states 90 s + cap accurately.
4. **`file://` was blessed by `BrowserUrl.Normalize`.** Policy blocked it upstream, but
   the session layer should not. → Removed; only http(s) pass through.
5. **Unknown application names launched via shell resolution without confirmation**
   (PATH/registered-handler search). → Policy now requires confirmation for
   non-catalog applications (catalog apps unaffected; new policy test added).
6. **No single-instance guard.** A second instance would double-register the hotkey and
   could interleave audit appends. → Named-mutex guard with an informative message.
7. **Stage 9 security-gate harness was `continue-on-error` in CI** (by analogy with the
   desktop-dependent harnesses) despite being file-operations only. → Now a gating step.
8. **Documentation staleness.** `docs/ARCHITECTURE.md` still described Stage 5–9 modules
   as future; README layout missed `CIPHER.SecurityTest`; reports said only Stage 10
   remains (Stage 11 exists). → All corrected; threat model gained explicit residual
   risks (lexical-only path checks, substring guard over-blocking, rotation chain scope).

**Accepted as-is (documented, fail-safe direction):** CloseApplication's already-correct
semantics (a dead empty loop was removed); credential-name substring matching can
over-block (e.g. "Shipping" contains "pin"); `TaskEngine` is single-flight by UI design;
audit chaining is per-process (single-instance guard now enforces this in practice).

## Re-verification

The fixes were re-verified in CI on the verification commit itself: build, all 119
deterministic tests (118 + the new unknown-application policy test), and every harness —
including the new browser password-guard step, the forged-origin stage9 step, and the
now-gating stage9 security gate. Run identifiers are recorded in the conversation and in
`git log` for this commit; the PR check list shows each step's result.

## What remains unproven (unchanged by this audit)

Interactive Windows 10/11 desktop validation, headed-browser and real-account flows,
real-microphone/speaker validation, DPI/multi-monitor behavior, real-provider delegation,
idle-resource measurement, and clean-machine MSI install/uninstall — tracked in
`docs/IMPLEMENTATION_PLAN.md` Stages 10–11 and the manual test list in `docs/TESTING.md`.
