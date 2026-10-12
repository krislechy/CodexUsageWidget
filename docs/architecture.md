# Architecture

## Source map

```text
src/CodexUsageWidget.Core/
  CodexLocator.cs     Executable discovery and hidden process launch
  CodexClient.cs      JSON-line stdio protocol, request correlation and reconnect
  UsageSnapshot.cs    General quota bucket selection and window normalization
  UsageMonitor.cs     Event subscription, periodic polling and refresh serialization
src/CodexUsageWidget/
  App.xaml(.cs)      WPF resources, application lifetime and single instance
  MainWindow.xaml    Full and compact layouts
  MainWindow.xaml.cs Window actions, tray, monitor binding and preview capture
  WidgetViewModel.cs Display state, date/percentage formatting, stale status
  WidgetSettings.cs  Local preferences and explicit startup registration
  SettingsWindow.*  Configuration UI
  Controls/RingGauge.cs Animated, resolution-independent quota arcs
tests/CodexUsageWidget.Tests/
  Program.cs         Dependency-free regressions and fake stdio app-server
```

## Data flow and ownership

1. `MainWindow` owns one `UsageMonitor` and marshals its events to the WPF dispatcher.
2. `UsageMonitor` owns one `CodexClient`. Polling and manual refresh share a semaphore; overlapping refresh requests are skipped.
3. `CodexClient` owns one child process at a time. A connection gate protects startup and the handshake. Each connection owns its reader, stderr drainer, pending request dictionary, write gate and cancellation source.
4. Replies are correlated by numeric ID. Notifications do not consume request completions. Unsupported server requests receive a method-not-supported response; the widget never supplies auth tokens or approves actions.
5. `UsageSnapshot` selects only the general `codex` bucket and matches 300/10080-minute windows. Unknown windows are absent, not zero.
6. `WidgetViewModel` exposes display values. The rings represent consumed percentages and the prominent row values represent remaining percentages. Older snapshots cannot replace a newer snapshot.

The transport has no dependency on WPF. The application uses code-behind for native window/tray actions and a small view model for display bindings. A DI container, database and event bus would add no useful capability here.

## Failure and refresh behavior

- Request deadline: 20 seconds. A timed-out request aborts the connection; the next refresh initializes a new one.
- Process exit: outstanding requests fail immediately and the next refresh reconnects.
- Missing CLI/login/network: show a recoverable status; retain the last successful snapshot.
- Polling: fixed selected interval, even when the previous refresh failed. The first read happens immediately.
- Events: process `account/rateLimits/updated` received on our connection. Cross-client event fanout is not relied upon.
- Settings change: stop/dispose the old monitor before starting a new one. Queued callbacks from the old monitor are ignored.
- Exit: cancel the monitor, drain pending activity and kill only the owned process tree. Closing the window normally hides it to the tray.

## Storage and boundaries

Preferences are in `%LOCALAPPDATA%\CodexUsageWidget\settings.json`, saved by a temporary-file replacement. Startup registration is the current user's `Run` entry and is changed only through the explicit checkbox.

Authentication belongs to the official CLI. The widget does not read `auth.json`, use browser cookies, change Codex configuration, redeem reset credits or start inference. No raw protocol responses or stderr lines are persisted.

## Verification

The executable test suite uses a real child process with synthetic JSON-line responses. `--live` checks the official local server without starting a model turn. `--capture` renders the actual WPF visuals into PNGs for full/compact/stale/settings review.
