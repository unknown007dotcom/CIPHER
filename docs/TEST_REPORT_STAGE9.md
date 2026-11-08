# CIPHER Test Report — Stage 9 (Security and Recovery Hardening)

**Branch:** `arena/1bb3d317-cipher`
**Feature commit:** `e24b9ea` ("Match quarantine and audit commands before generic list rule" — final
Stage 9 code state, merged forward through two fixups from `4a00d5e`)
**CI verification:** GitHub Actions `windows-latest` — push run `42504730361` ✓ and pull-request
run `42504734670` ✓, both fully green with every harness through Stage 9 passing.
**Prior stage:** `docs/TEST_REPORT_STAGE8.md` (CI-verified at `28132d0`).

## What Stage 9 delivered

- **Structured, tamper-evident audit log** (`AuditLog` in `CIPHER.Core`): append-only JSON lines
  at `%LocalAppData%\CIPHER\logs\cipher-audit.log`, each entry SHA-256 hash-chained to its
  predecessor; size-based rotation (1 MB threshold, one archive generation); every detail string
  passed through `SensitiveDataRedactor` before hashing/writing. `AuditLog.Verify` recomputes the
  chain and pinpoints the first invalid line.
- **Audited events** (app layer): `task.blocked`, `task.cancelled`, `delegation.completed/failed`
  (endpoint host + artifact path only), `security.api_key_saved/deleted`,
  `memory.interrupted_recovered`, `memory.corruption_recovered`, `app.safe_mode`. Auditing is
  best-effort and can never break command execution.
- **Journal redaction**: `TaskEngine` now redacts both the command text and the result message
  before they reach the SQLite task journal/history, so pasted secrets do not persist.
- **Quarantine recovery**: deletes now write a `.cipher-quarantine.json` sidecar recording the
  original path (`QuarantineService`); new commands `list quarantine` (read-only) and
  `restore <name>` (confirmation-required, refuses to overwrite an existing destination,
  falls back to `%LocalAppData%\CIPHER\Recovered` for pre-Stage-9 items without metadata).
  The app announces quarantined items at startup. Exit-gate mutation safety is preserved:
  restore itself goes through the same policy/confirmation pipeline as every other action.
- **Safe mode**: `--safe-mode` forces AI delegation off, refuses wake listening, and announces
  itself, while keeping typed commands, quarantine review, and `verify audit log` available.
- **Threat model**: `docs/THREAT_MODEL.md` (assets, trust boundaries, 12 threats with
  code-mapped mitigations, residual risks). Third-party inventory refreshed in
  `LICENSES/README.md` with exact pinned versions and the eSpeak NG GPL flag.

## Exit-gate evidence — blocked/cancelled/failed actions do not mutate data

**Deterministic tests** (97 → 118 cases, all gating in CI;
`src/CIPHER.Tests/SecurityHardeningTests.cs`):

- Gate tests with recording/throwing executors: unknown command → `Blocked` with **zero** executor
  calls; confirmation denied → `Cancelled` with **zero** executor calls; executor exception →
  `Failed`; journal records match outcomes; an alias expanding to `delete …` still stops at the
  confirmation gate.
- Audit tests: chain verifies after appends and across instances; a modified entry is detected at
  the exact line; a removed entry breaks the chain; credential-shaped text never reaches the
  file; rotation keeps the active chain verifiable; missing file verifies as empty.
- Redactor theories: labeled secrets, bare `sk-…` keys, bearer tokens, and URL userinfo are
  masked; ordinary text is untouched.
- Parser/policy: `list quarantine`/`verify audit log` are Low-risk read-only; `restore` requires
  confirmation.

**Stage 9 security-gate harness** (`CIPHER.MilestoneTest stage9`, CI session 0, exit 0):

```text
::notice title=CIPHER Stage 9 security gate::STAGE9-PASS delete→quarantine→list→restore
round-tripped; denied and blocked actions mutated nothing.
```

It drives the real engine + `WindowsActionExecutor` on temp files: delete moves the file to
quarantine, `list quarantine` shows it by original name, `restore` returns it byte-identical to
its original path via the sidecar, a denied delete leaves a sentinel file untouched, and a
blocked command mutates nothing.

**Cross-process audit harness** (`CIPHER.SecurityTest --write` then `--verify`):

```text
::notice title=Stage 9 audit harness::STAGE9-PASS hash chain verified across processes;
secrets redacted before writing; tampering detected at the modified line.
```

One process appends three events (one deliberately carrying `api_key=`/`password:` shaped text);
a second process verifies the chain, proves the secrets never reached the file, and pinpoints a
tampered copy at the modified line.

## What this evidence does and does not prove

- **Proved in CI:** audit chain integrity + tamper detection across processes, pre-write
  redaction in both audit log and SQLite journal, quarantine round-trip with exact-origin
  restore, and the exit gate (blocked/cancelled actions reach no executor and mutate no files).
- **Design-reviewed, not CI-proved:** safe-mode behavior (verified by code path inspection —
  delegation options forced off, wake start refused); interactive recovery UX (conversation
  notices) needs a real Windows desktop session, like Stages 2–6 interactive validation.
- **Stated limits:** the audit chain is tamper-evidence, not attestation — a same-user attacker
  can regenerate it (see `docs/THREAT_MODEL.md`). Redaction targets credential-shaped patterns;
  it is defense in depth, not a DLP guarantee.

## Not a release

Still outstanding before any release claim: interactive Windows 10/11 validation, headed-browser
validation, real-microphone/speaker checks, DPI/multi-monitor checks, real-provider delegation,
installer staging with dependency hashes (Stage 10), and the Stage 10 WiX MSI itself.

> **Post-stage verification note (see `docs/VERIFICATION_REPORT.md`):** a full audit after
> this report landed added a DOM-level password-fill refusal in the browser executor, a
> protected-origin guard for quarantine restores, confirmations for unknown applications,
> a 1 MB delegation response cap, a single-instance guard, and made the Stage 9 security-gate
> harness a gating CI step (the Stage 3/4/5/6 harnesses remain `continue-on-error`
> experimental in the non-interactive session). Test count rises to 119 and the stage9
> harness gains a forged-origin restore step.
