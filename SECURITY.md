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

## Current limitations

- Voice, browser automation, and external AI delegation are not implemented yet, so their runtime
  attack surface is not present in this build.
- UI Automation and simulated input act within the current desktop session. CIPHER cannot and
  must not interact with secure desktops or another user's session.
- The final installer has not yet undergone clean-machine install/uninstall security testing.
