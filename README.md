# Codex Usage Widget

A small Windows desktop companion that keeps your Codex subscription limits visible. Two Activity-inspired rings show **quota remaining**: full means all quota is available, empty means none. The numbers show the same remaining percentage and the dates show the next reset in your local time zone.

![Main widget](docs/screenshots/main.png)

> Independent open-source utility, not an official OpenAI application. The screenshot uses demonstration data. Subscription quota percentages are not a count of tokens remaining.

## Features

- Five-hour and weekly Codex quota windows, remaining percentages and local reset dates.
- Automatic polling every minute by default, plus app-server rate-limit notifications.
- Pink and lime vector rings with short, interruptible animations; Windows animation preferences are respected.
- Full and compact ring layouts, with a live five-hour reset countdown in HH:mm:ss format at their centers.
- Draggable windows, optional always-on-top, matching dark tooltips and a system tray menu.
- Shared rounded dark Settings and reset confirmation windows, using native XAML CenterOwner placement.
- A two-ring application icon, supplied in multiple resolutions for Windows and the tray.
- Remembers window position, layout, CLI path and refresh interval.
- Optional start with Windows, enabled only when you check it in Settings.
- Retains the last successful snapshot during an outage and marks it as stale.
- Available earned resets and the earliest known expiry date, with an explicit reset action and confirmation.
- English by default, with Russian, Ukrainian, Serbian (Cyrillic), French, German, Simplified Chinese and Japanese in Settings.
- Monitoring does not start prompts or model requests. A reset is spent only after you confirm the reset action.

![Compact widget](docs/screenshots/compact.png)

## Quick start

1. Download `CodexUsageWidget-win-x64.zip` from [Releases](https://github.com/krislechy/CodexUsageWidget/releases).
2. Extract the ZIP and run `CodexUsageWidget.exe`. Keep the extracted files together.
3. Have the official Codex CLI installed and signed in with your ChatGPT account:

   ```powershell
   codex --version
   codex login
   ```

The portable package includes the .NET runtime and uses compression. The `-lite` package requires the **Microsoft .NET 10 Desktop Runtime (Windows x64)**, available from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Neither package includes Codex CLI. Builds are currently unsigned. See [package size](docs/package-size.md) for the size breakdown and tradeoffs. A local build never updates GitHub; publication requires an explicitly authorized command.

If the widget cannot find Codex, open the gear button and select `codex.exe` or `codex.cmd`. An empty path searches `PATH`, preferring a native executable. You can also set `CODEX_USAGE_WIDGET_CODEX_PATH`.

### Controls

| Control | Action |
| --- | --- |
| Drag the window background | Move the widget |
| Pin | Toggle always-on-top |
| Gear | Open Settings |
| Reset limits in the expanded widget | Confirm spending one earned reset |
| Chevron / Ctrl+M | Switch between full and compact views |
| Double-click compact background | Expand the widget |
| Refresh / Ctrl+R | Refresh immediately |
| Close / Escape / Alt+F4 | Hide to the tray; updates continue |
| Left-click tray icon | Show the widget |
| Tray → Exit | Stop the widget and its child app-server |

Hover the compact widget for quota reset dates, earned reset availability/expiry and update status. Its center shows the five-hour reset countdown, refreshed every second. Unknown reset time shows `--:--:--`; elapsed time stays at `00:00:00` until fresh server data arrives. The tray menu contains Show, Full/compact, Settings and Exit. A second launch does not create a duplicate widget; use the tray icon to show the existing instance.

### Earned resets

The reset section uses Codex's authoritative `availableCount`. When Codex supplies all expiry dates it shows the earliest expiry; if the detail list is capped or incomplete, it explicitly labels the earliest **known** date. Missing metadata stays unknown. Resetting requires a usable returned credit ID and current quota data; a stale snapshot cannot start a new reset.

The confirmation explains that one earned reset will be spent. The widget selects the soonest-expiring usable returned credit, persists the request before sending it, then calls `account/rateLimitResetCredit/consume` with that credit ID and an idempotency key. If the answer is lost, **Retry reset** repeats the same request, including after restarting the application, rather than spending a second reset. Polling never triggers a reset. The widget re-reads authoritative limits after any definitive outcome.

### Languages

Choose the interface language in Settings. The whole interface previews the selection immediately; Save persists it and Cancel restores the previous language. Existing settings without a language field migrate to English. Numbers, dates, errors, tray labels, confirmations and accessibility descriptions follow the selection. Windows system file-picker buttons follow Windows itself.

## How updates work

The widget starts a local `codex app-server --listen stdio://`, performs the `initialize` / `initialized` handshake, then calls `account/rateLimits/read`. It also listens for `account/rateLimits/updated` on that connection. The official protocol is documented in [Codex App Server](https://learn.chatgpt.com/docs/app-server).

Notifications from a separate app-server are **not assumed to reflect activity in every other Codex session**. Periodic polling is the dependable fallback: normally your desktop/CLI usage is reflected within the selected interval plus request time. Choose 30 seconds, 1 minute, 2 minutes or 5 minutes in Settings. Requests time out after 20 seconds; a hung or closed server is recreated on the next refresh.

Quota windows are identified by their durations, not by their primary/secondary slot. The general `codex` bucket is preferred over model-specific limits. A missing window displays an em dash, never an invented 0%. The widget does not assume a reset has happened just because the clock passed the reset date; it waits for fresh values from Codex.

## Build and test

Requires Windows 10/11 and the .NET 10 SDK. There are no third-party NuGet dependencies.

```powershell
dotnet build CodexUsageWidget.sln -c Release
dotnet run --project tests/CodexUsageWidget.Tests -c Release
dotnet run --project src/CodexUsageWidget -c Release
```

Tests are an executable regression suite, not an adapter-based `dotnet test` project. They launch a fake stdio server to exercise handshakes, events, request correlation, timeouts, cancellation, process exit, polling and reset outcomes. They also cover quota/reset metadata, a lost reset response and durable retry identity. Routine verification runs the suite once and captures only English UI. Locale-wide catalog checks are opt-in with `--check-locales`, only when requested. Tests require no login or network access and never spend a real reset.

Optional read-only smoke test with your signed-in Codex CLI:

```powershell
dotnet run --project tests/CodexUsageWidget.Tests -c Release -- --live
```

Prepare both Portable and Lite with tests, English screenshots and checksums:

```powershell
./scripts/release.ps1
# Only after explicit publication authorization:
./scripts/release.ps1 -Commit -Publish
```

Preview with synthetic values, or capture the actual WPF UI without connecting to Codex:

```powershell
dotnet run --project src/CodexUsageWidget -c Release -- --demo
dotnet run --project src/CodexUsageWidget -c Release -- --capture artifacts/screenshots
```

See [release workflow](docs/releasing.md), [architecture](docs/architecture.md), [design system](DESIGN.md), [privacy](docs/privacy.md) and [troubleshooting](docs/troubleshooting.md).

## License

[MIT](LICENSE).
