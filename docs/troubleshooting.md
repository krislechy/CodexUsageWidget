# Troubleshooting

## No data / Codex not found

Run `codex --version` in PowerShell. If it fails, install the official Codex CLI or select its existing `codex.exe` / `codex.cmd` in Settings. A `codex.ps1` shim alone is not selected; use the adjacent `codex.cmd` or native executable.

If the widget was started before `PATH` changed, exit it from the tray and restart it. An explicit path or `CODEX_USAGE_WIDGET_CODEX_PATH` also works.

## Account limits unavailable

Run `codex login` and sign in with ChatGPT. API-key-only authentication does not provide ChatGPT subscription quota windows. If your account does not expose one of the windows, that row remains unavailable.

## Stale data

Hover the status line or compact window for details. The widget keeps the last successful values and automatically retries on the next polling interval. You can click Refresh to retry immediately. Check network connectivity and CLI login if failures persist.

An app-server notification is an additional update source, not a promise that all other clients emit events to the widget's connection. Use the polling interval to control the maximum normal delay.

## Reset date passed but the ring has not reset

The widget waits for the authoritative server values. Local clock passage is not proof of an account reset. It labels the passed date while continuing to refresh.

## Widget disappeared

Close, Escape and Alt+F4 hide it to the tray. Left-click the pink/lime ring icon near the Windows clock. To stop it fully, use the tray menu's Exit action.

The widget checks saved coordinates against connected monitors and moves an off-screen window back into the primary work area.

## Reset button disabled / expiry unknown

A new reset requires a current snapshot, a positive server-reported count and an eligible returned credit ID. Refresh if metadata is unavailable or stale. Codex can return only a count or a partial detail list; the widget does not invent expiry dates. A known date is not a guarantee that another undisclosed reset will not expire earlier.

## Reset result unknown

Use Retry reset to repeat the confirmed attempt. Its persisted idempotency key prevents a second reset being spent if the first request succeeded but its reply was lost. Do not delete `pending-reset.json` while an outcome is uncertain. A damaged or unwritable pending record blocks new resets; restore the record or folder access before retrying. The widget does not automatically consume resets.

## Language

Open Settings and choose a language. Changes preview immediately. Save keeps the choice; Cancel restores the previous language. English is the default for old or missing settings. OS-native file-picker chrome follows the Windows display language.

## Reset preferences / uninstall

1. Disable Start with Windows in Settings.
2. Exit from the tray.
3. Delete the extracted release folder and `%LOCALAPPDATA%\CodexUsageWidget`.

This does not uninstall Codex or change its account authentication.
