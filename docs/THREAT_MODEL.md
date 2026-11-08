# CIPHER threat model

Stage 9 deliverable. Scope: the CIPHER desktop assistant as implemented through Stage 9 —
deterministic command parsing and policy, Windows automation actions, Playwright browser
automation, offline voice, SQLite memory, opt-in external AI delegation, quarantine recovery,
and the local audit log. This document states what CIPHER defends against, what it does not,
and where each mitigation lives in the code.

## Assets

| Asset | Where it lives | Sensitivity |
| --- | --- | --- |
| User files and folders | The user's disk | Deletion/corruption is the worst outcome CIPHER could cause |
| External AI API key | `%LocalAppData%\CIPHER\secrets\ai-api-key.bin` (DPAPI ciphertext) | Billable credential; theft = financial harm |
| Command history, aliases, learned app paths | `%LocalAppData%\CIPHER\cipher-memory.db` (SQLite) | Privacy; reveals habits; never contains credentials by policy |
| Audit log | `%LocalAppData%\CIPHER\logs\cipher-audit.log` | Accountability record; tampering undermines trust |
| Quarantined items | `%LocalAppData%\CIPHER\Quarantine` (+ `.cipher-quarantine.json` sidecars) | Deleted-but-recoverable user data |
| Remote AI provider account | The configured OpenAI-compatible endpoint | Whatever the user sends is processed by a third party |

## Trust boundaries and actors

- **The user** is trusted to approve or deny confirmations, but their *input* is not: typed,
  pasted, or spoken text can contain mistakes, injected instructions (e.g. a pasted
  "command" from a web page), or credential-shaped secrets.
- **The deterministic core** (parser → policy → engine) is the security kernel. Executors are
  behind it; no executor runs without a policy decision, and confirmation happens in the
  engine, before any executor is invoked.
- **UI Automation targets, web pages, and downloadable files** are untrusted content.
  Browsers downloads land on disk but are never executed; web content is never turned into
  commands.
- **The external AI provider** is untrusted: features are off by default, the endpoint is
  user-configured, responses are data (saved to a user-readable artifact), and suggested
  commands are never auto-executed.
- **Other local processes running as the same user** are out of the defense perimeter:
  DPAPI and file ACLs inherit the user's account security. A same-user attacker can read
  the memory database and regenerate the audit chain (see Residual risks).

## Threats and mitigations

| # | Threat | Mitigation (where) |
| --- | --- | --- |
| T1 | Destructive command (typed, pasted, misheard, or injected) deletes user data | Deletes are reversible quarantine moves, never permanent (`WindowsActionExecutor.DeletePath`); protected paths blocked; confirmation required (policy); restore via `restore <name>` with origin metadata (`QuarantineService`) |
| T2 | Arbitrary shell/code execution via crafted input | No shell executor exists; unknown intents blocked fail-closed (`DefaultActionPolicy`); `run <app>` only launches known apps/paths |
| T3 | A learned alias or AI suggestion smuggles in a dangerous action | Aliases expand back into full text and re-enter policy + confirmation (Stage 7, verified by tests); AI suggestions are shown in the input box, never executed (Stage 8) |
| T4 | Credential leakage into history/logs | `SensitiveDataRedactor` scrubs credential-shaped text before journaling (`TaskEngine`) and before audit writes (`AuditLog`); the API key itself never appears in command text (password box → DPAPI file) |
| T5 | API key theft from disk | DPAPI `CurrentUser` ciphertext (`DpapiApiKeyStore`); key loaded only at execution time; never logged, never in settings JSON |
| T6 | Silent or unaccountable high-impact actions | Hash-chained audit log (`AuditLog`): blocked/cancelled outcomes, delegation calls (endpoint host + artifact), key save/delete, memory recovery; `verify audit log` command; rotation with retention |
| T7 | Automation fills credentials or types into the wrong window | Credential-field heuristics block typing into password-like fields; window-title matching guards; graceful close only (no force-kill) |
| T8 | Browser workflow downloads and runs malware | Downloads are saved and reported, never opened (`CIPHER.Browser`); browser actions require explicit commands and confirmations per policy |
| T9 | Voice input triggers actions without the user's intent | Wake-word gating; continuous listening off by default, never auto-starts, requires the wake phrase; push-to-talk scoped to the app window |
| T10 | Crash or kill mid-task leaves inconsistent state | Task journal marks interrupted tasks `Interrupted` at next start and never re-executes them; corrupt databases are quarantined and recreated (Stage 7) |
| T11 | Delegation sends data the user didn't approve | Disabled by default; exact request preview in the mandatory confirmation; remote endpoints require a key; loopback-only keyless mode; 90-second timeout and a 1 MB response-size cap (`OpenAiChatClient`) |
| T12 | Tampering with the audit record to hide actions | SHA-256 hash chain with genesis anchor; tampering and deletion detected with first-bad-line (`AuditLog.Verify`, `verify audit log`) — detection only; see residual risks |

## Residual risks and non-goals

- **Same-user local attacker.** DPAPI, the memory database, and the audit log all sit under
  the user's profile. Audit chaining detects edits, but an attacker with write access can
  recompute a fresh chain; this is tamper *evidence*, not attestation. Server-side or
  append-only remote logging is out of scope for a local assistant.
- **Confirmation fatigue / user error.** CIPHER shows exact targets but cannot stop a user
  from approving a harmful action. Quarantine reversibility is the safety net for deletes;
  other destructive-adjacent actions (move/rename overwrite prevention, graceful close)
  follow the same no-surprises rule but are not all reversible.
- **Third-party provider risk.** Anything sent to the configured AI endpoint is governed by
  that provider. CIPHER minimizes exposure (opt-in, preview, redaction of the journal) but
  cannot sanitize what a user explicitly approves sending.
- **Path checks are lexical.** Protected-path checks normalize and expand paths but do not
  resolve symlinks/junctions, so a user-created junction inside an unprotected folder that
  points into a protected one is not detected lexically (the Windows ACLs still apply).
  Restores never use a recorded origin that is protected; they fall back to the Recovered
  folder.
- **Name-based field guards are conservative.** Credential/dangerous-name detection is
  substring-based: it can over-block innocent names (a "Shipping" field contains "pin") but
  is layered over runtime checks (UI Automation `IsPassword`, browser `type=password`)
  that block actual credential controls regardless of label.
- **Audit rotation starts a fresh chain.** The rotated archive (`cipher-audit.log.1`) is a
  separate chain with its own genesis; verify after rotation covers the active file.
- **Model/voice supply chain.** The Vosk model and eSpeak NG binaries used in CI and any
  future installer must be hash-verified at staging time (`dependencies/` manifest); the
  repository does not bundle them yet.
- **Not in scope:** multi-user machines with profile snooping, kernel-level malware,
  physical access, and enterprise policy management.

## Review cadence

Revisit this document whenever a stage adds a new executor, a new network surface, or a new
store. The Stage 10 installer must re-review T5/T6 paths (installed file locations and ACLs)
and the dependency manifest before release.
