# Security Policy

## Supported versions

CIPHER has not published a supported release yet. Security fixes will be applied to the active
development branch until version 1.0 is released and tested.

## Reporting a vulnerability

Please report suspected vulnerabilities privately to the repository owner through GitHub's
private vulnerability reporting/advisory process when available. Do not publish credentials,
cookies, personal files, or exploit details in a public issue.

Include:

- CIPHER version or commit;
- Windows version and architecture;
- steps to reproduce;
- expected and actual behavior;
- whether user confirmation or a security boundary was bypassed.

## Security design rules

- CIPHER runs with the current user's privileges (`asInvoker`) by default.
- Arbitrary shell execution is disabled in v1.
- High-risk and destructive actions require explicit user confirmation.
- Protected Windows directories and drive roots are blocked for destructive operations.
- Deletes are moved to a reversible local quarantine rather than permanently erased.
- Applications are closed gracefully; CIPHER v1 does not force-kill target processes.
- Passwords, authentication cookies, and sensitive credentials must never be stored in plaintext
  settings or logs.
- External sharing/delegation must ask before sending private documents to a third-party service.
- CIPHER must not bypass CAPTCHAs, MFA, access controls, Windows UAC, or platform restrictions.

## External AI delegation (Stage 8)

- Delegation is **off by default** and each prompt requires explicit approval showing the
  exact text that will be sent. CIPHER's core never needs it.
- The API key exists only as DPAPI-encrypted data (`%LocalAppData%\CIPHER\secrets\ai-api-key.bin`).
  It is never written to settings, the SQLite memory, logs, or command history, and the UI
  refuses key entry through the (journaled) command box by design.
- Only the approved prompt text is sent — the client has no access to files, history,
  browser data, or memory contents. Replies are treated as untrusted text: a suggested
  command is displayed for review and is never executed automatically.
- Browser automation uses ephemeral, CIPHER-managed sessions; CIPHER does not read,
  store, or replay the user's browser passwords/cookies, and it does not automate
  CAPTCHA, MFA, or login challenges.

## Current limitations

- Voice and browser automation run in the current user's desktop session; microphone use
  is visible and user-started (or explicitly toggled for wake listening).
- UI Automation and simulated input act within the current desktop session. CIPHER cannot and
  must not interact with secure desktops or another user's session.
- The final installer has not yet undergone clean-machine install/uninstall security testing.
