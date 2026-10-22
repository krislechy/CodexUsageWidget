# Privacy

- The widget communicates only with a local child Codex app-server over stdin/stdout. The official CLI makes the authenticated network calls required to retrieve your account limits.
- Existing CLI authentication is reused by the CLI itself. The widget never opens credential files or receives credentials as configuration.
- Stored preferences: executable path, refresh interval, language, always-on-top, compact mode and window coordinates.
- A pending reset stores an opaque credit ID and a GUID retry key locally. They are used only to safely complete or retry an action you confirmed; no authentication tokens are stored.
- No telemetry, analytics SDK, remote widget backend or crash-upload service is included.
- The widget discards raw app-server stderr and does not persist raw account responses.
- The optional `--live` developer test prints remaining quota percentages and whether reset dates exist; do not publish its output if you consider those values private.
- Release screenshots contain synthetic data. User preferences and authentication files are not included in source control or archives.
- Enabling startup creates only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexUsageWidget`. Disable it before moving or deleting the executable.

The app-server is a full-capability upstream process. Normal monitoring sends only initialization and quota-read requests. The explicit reset action additionally sends one earned-reset consume request after in-app confirmation. It does not start conversations, send messages or spend model tokens to refresh limits. Tests use a fake server for consume operations; live smoke tests only read data.

## GitHub update traffic

When enabled, the app contacts `api.github.com/repos/krislechy/CodexUsageWidget/releases/latest` after startup and at most once per six hours in the background. Manual checks use the same endpoint. It sends the app name/version and normal HTTPS connection metadata, with no GitHub token, Codex credentials, account metrics or conversation information. Update assets come from the matching release on `github.com` and GitHub's download redirects. Settings can disable automatic updates; manual checking remains available. Local update jobs contain package files, paths, process identity and verification/result metadata, not account data.

## Context usage data

The optional context bar reads a bounded tail of the manually selected local Codex session journal. Journal tails may contain conversation text, but only token-count metadata is extracted; no journal text is logged, persisted, uploaded or included in screenshots. The saved preference contains the selected conversation ID and context color. No model calls or additional authenticated network requests are made for context tracking.

The session picker additionally reads project names and explicit thread-project memberships from the local Codex desktop state file. Only those metadata fields are used; the file is never modified or uploaded. Session titles are held in memory for the visible menu and are not written to widget settings.
