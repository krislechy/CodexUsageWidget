# Codex Usage Widget

A small Windows desktop companion that keeps your Codex subscription limits visible. Two Activity-inspired rings show **usage consumed**, while the numbers below show **quota remaining** and the dates show the next reset in your local time zone.

![Main widget](docs/screenshots/main.png)

> Independent open-source utility, not an official OpenAI application. The screenshot uses demonstration data. Subscription quota percentages are not a count of tokens remaining.

## Features

- Five-hour and weekly Codex quota windows, remaining percentages and local reset dates.
- Automatic polling every minute by default, plus app-server rate-limit notifications.
- Pink and lime vector rings with short, interruptible animations; Windows animation preferences are respected.
- Full and compact layouts, draggable windows, optional always-on-top and a system tray menu.
- Remembers window position, layout, CLI path and refresh interval.
- Optional start with Windows, enabled only when you check it in Settings.
- Retains the last successful snapshot during an outage and marks it as stale.
- Russian interface; English documentation.
- Read-only integration: no prompts, model requests, token purchases or quota resets.

![Compact widget](docs/screenshots/compact.png)

## Quick start

1. Download `CodexUsageWidget-win-x64.zip` from [Releases](https://github.com/krislechy/CodexUsageWidget/releases).
2. Extract the ZIP and run `CodexUsageWidget.exe`. Keep the extracted files together.
3. Have the official Codex CLI installed and signed in with your ChatGPT account:

   ```powershell
   codex --version
   codex login
   ```

The portable release includes the .NET runtime. It does **not** include Codex CLI. Builds are currently unsigned.

If the widget cannot find Codex, open the gear button and select `codex.exe` or `codex.cmd`. An empty path searches `PATH`, preferring a native executable. You can also set `CODEX_USAGE_WIDGET_CODEX_PATH`.

### Controls

| Control | Action |
| --- | --- |
| Drag the window background | Move the widget |
| Pin | Toggle always-on-top |
| Gear | Open Settings |
| Chevron / Ctrl+M | Switch between full and compact views |
| Double-click compact background | Expand the widget |
| Refresh / Ctrl+R | Refresh immediately |
| Close / Escape / Alt+F4 | Hide to the tray; updates continue |
| Left-click tray icon | Show the widget |
| Tray → Exit | Stop the widget and its child app-server |

Hover the compact widget for reset dates and update status. A second launch does not create a duplicate widget; use the tray icon to show the existing instance.

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

Tests are an executable regression suite, not an adapter-based `dotnet test` project. They launch a fake stdio server to exercise handshakes, events, request correlation, timeouts, cancellation, process exit and polling. They also cover quota parsing and missing/invalid fields. They require no login or network access.

Optional read-only smoke test with your signed-in Codex CLI:

```powershell
dotnet run --project tests/CodexUsageWidget.Tests -c Release -- --live
```

Create the portable build:

```powershell
./scripts/publish.ps1
```

Preview with synthetic values, or capture the actual WPF UI without connecting to Codex:

```powershell
dotnet run --project src/CodexUsageWidget -c Release -- --demo
dotnet run --project src/CodexUsageWidget -c Release -- --capture artifacts/screenshots
```

See [architecture](docs/architecture.md), [design system](DESIGN.md), [privacy](docs/privacy.md) and [troubleshooting](docs/troubleshooting.md).

## License

[MIT](LICENSE).
