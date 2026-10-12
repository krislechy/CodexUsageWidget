# Privacy

- The widget communicates only with a local child Codex app-server over stdin/stdout. The official CLI makes the authenticated network calls required to retrieve your account limits.
- Existing CLI authentication is reused by the CLI itself. The widget never opens credential files or receives credentials as configuration.
- Stored preferences: executable path, refresh interval, always-on-top, compact mode and window coordinates.
- No telemetry, analytics SDK, remote widget backend or crash-upload service is included.
- The widget discards raw app-server stderr and does not persist raw account responses.
- The optional `--live` developer test prints remaining quota percentages and whether reset dates exist; do not publish its output if you consider those values private.
- Release screenshots contain synthetic data. User preferences and authentication files are not included in source control or archives.
- Enabling startup creates only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexUsageWidget`. Disable it before moving or deleting the executable.

The app-server is a full-capability upstream process, but this client sends only initialization and quota-read requests. It does not start conversations, send messages or spend model tokens to refresh limits.
