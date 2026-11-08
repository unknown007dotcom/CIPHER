# CIPHER Stage 7 SQLite memory and recovery test report

## Result

- Date: 2026-11-08
- Commit tested: `1e85c61` (branch `arena/1bb3d317-cipher`)
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42496132641
- Outcome: **Stage 7 exit gate PASSED** — the two-phase kill/restart harness recovered an
  interrupted task across processes without re-executing it. The deterministic suite grew
  from 57 to 84 cases, including real-SQLite integration tests. Stages 3, 5, and 6 also
  passed in the same run; the Stage 4 harness reported a window-launch timeout in this
  run only. Follow-up: in run 42496912571 all five harnesses (Stages 3–7) passed,
  confirming that single Stage 4 timeout was a runner timing flake, not a regression.

## Scenario executed

Two separate processes share one SQLite file (`--write`, then `--verify`):

1. Write process: initializes the store, journals a task as **Running**, saves an alias,
   a learned application path, and a history entry — then exits *without completing the
   task* (the simulated kill).
2. Verify process: a brand-new `SqliteMemoryStore` on the same file must find the
   interrupted task, the alias (case-insensitive), the app path, and the history entry.
   It then marks the task **Interrupted** — recovered, reported, and explicitly *not*
   re-executed — and confirms nothing remains incomplete.

The unit/integration suite additionally verifies: schema migration on fresh files,
history ordering/limits, completed tasks not counting as incomplete, persistence across
store instances, and **corruption recovery** — a deliberately garbaged database file is
quarantined to `*.corrupt-<timestamp>` (kept, never deleted) and replaced with a working
fresh database. A fault-injecting store also proves memory failures can never break
command execution in the task engine.

## What the application does with this

- **Task journal & recovery**: every command is journaled (`Running` → terminal status).
  On startup, leftover `Running` tasks are reported in the conversation and marked
  Interrupted — never silently re-run, so destructive steps are never repeated.
- **Aliases**: `alias morning = open notepad`, `create alias … for …`, `forget alias …`,
  `list aliases`. Saying the alias expands it once, and the expansion still passes the
  full parser → policy → confirmation flow (an alias cannot bypass safety checks).
- **Learned application paths**: `remember app my editor at C:\Tools\editor.exe`,
  layered over the built-in catalog (`open my editor` launches it), `forget app …`,
  `list apps`.
- **History**: recent commands are journaled and the last few are shown at startup.
- The database lives at `%LocalAppData%\CIPHER\cipher-memory.db` (SQLite, WAL mode). No
  passwords, cookies, or credentials are stored — see `SECURITY.md`. Memory failures
  degrade to an honest startup notice; commands keep working.

## Honest limitations

- Application preferences remain in the existing JSON settings file; the database holds
  tasks, history, aliases, and app paths (one storage technology per concern, by design).
- Recovery reports interrupted tasks and waits for the user; it does not attempt
  step-level resume of a partially completed multi-step action.
- The kill simulation ends the writer process cleanly between steps; an OS-level power
  loss mid-write is covered by SQLite WAL guarantees plus the corruption-quarantine
  path, both verified above except the literal power cut.
