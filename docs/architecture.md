# Architecture

## Source map

```text
src/CodexUsageWidget.Core/
  CodexLocator.cs     Executable discovery and hidden process launch
  CodexClient.cs      JSON-line stdio protocol, request correlation and reconnect
  UsageSnapshot.cs    General quota bucket selection and window normalization
  UsageMonitor.cs     Event subscription, periodic polling and refresh serialization
  ResetCredits.cs     Earned reset metadata, outcomes and durable pending-request store
  Localization.cs     Embedded catalogs, culture selection and observable text
  Locales/*.json      Eight complete language catalogs
src/CodexUsageWidget/
  App.xaml(.cs)      WPF resources, application lifetime and single instance
  MainWindow.xaml    Full and compact layouts
  MainWindow.xaml.cs Window actions, tray, monitor binding and preview capture
  WidgetViewModel.cs Display state, date/percentage formatting, stale status
  WidgetSettings.cs  Local preferences and explicit startup registration
  WidgetDialogWindow.cs Shared frameless dialogs, shared drag/close behavior; placement is in XAML
  Assets/app.*       Two-ring SVG/PNG/ICO brand assets
  SettingsWindow.*  Configuration UI
  ResetConfirmationWindow.* Explicit spend/retry confirmation
  NoticeWindow.*    Shared-shell duplicate-launch notice
  TranslateExtension.cs Live XAML bindings to the selected catalog
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
6. `WidgetViewModel` exposes display values. The rings and prominent full-view values represent remaining percentages. Compact mode renders only rings; its tooltip retains values and reset/status detail. Older snapshots cannot replace a newer snapshot.

The transport has no dependency on WPF. The application uses code-behind for native window/tray/reset actions and a small view model for display bindings. A DI container, database and event bus would add no useful capability here.

## Failure and refresh behavior

- Request deadline: 20 seconds. A timed-out request aborts the connection; the next refresh initializes a new one.
- Process exit: outstanding requests fail immediately and the next refresh reconnects.
- Missing CLI/login/network: show a recoverable status; retain the last successful snapshot.
- Polling: fixed selected interval, even when the previous refresh failed. The first read happens immediately.
- Events: process `account/rateLimits/updated` received on our connection. Cross-client event fanout is not relied upon.
- Settings change: stop/dispose the old monitor before starting a new one. Queued callbacks from the old monitor are ignored.
- Exit: cancel the monitor, drain pending activity and kill only the owned process tree. Closing the window normally hides it to the tray. The notification icon uses the embedded brand ICO and opens a styled WPF ContextMenu; full/compact mode is also toggled by Ctrl+M and compact-ring double-click.

## Storage and boundaries

Preferences are in `%LOCALAPPDATA%\CodexUsageWidget\settings.json`, saved by a temporary-file replacement. Startup registration is the current user's `Run` entry and is changed only through the explicit checkbox.

Authentication belongs to the official CLI. The widget does not read `auth.json`, use browser cookies, change Codex configuration or start inference. No raw protocol responses or stderr lines are persisted.

## Earned reset state machine

The authoritative count comes from `rateLimitResetCredits.availableCount`; detail rows may be capped or null. Eligible rows must have an available status, the Codex reset type and an opaque ID. Expired rows are excluded from credit selection. Partial expiry information is labelled as a known date rather than a globally guaranteed earliest date.

After confirmation, `PendingResetStore.Begin` writes one GUID idempotency key and the chosen credit ID to `%LOCALAPPDATA%\\CodexUsageWidget\\pending-reset.json` before any request is sent. New calls reuse an existing pending record. `UsageMonitor.ConsumeResetAsync` shares the polling semaphore so reads and mutations do not overlap. Definitive outcomes (`reset`, `alreadyRedeemed`, `nothingToReset`, `noCredit`) clear the pending record and trigger a quota read. If the quota read fails after a definitive consume outcome, the action still completes but the displayed data stays marked stale.

Timeouts and transport errors keep the pending record. The UI offers a confirmed retry of that exact request, even after an application restart. No polling or startup path calls the consume method. Corrupt/unwritable pending state blocks a new reset rather than replacing its identity.

## Localization

`Localization` loads embedded JSON catalogs, defaults to English and notifies live XAML bindings and the view model when the selection changes. All catalogs must have the same keys and compatible formatting placeholders. The selected culture formats numbers and dates; WPF `Language` bindings use an explicit conversion culture to avoid a circular language conversion.

Settings preview a selection immediately. Cancel restores the original locale; Save persists it with the other preferences. Tray text and accessibility names also update. Serbian uses Cyrillic; Chinese uses the simplified catalog. Runtime catalogs are marked `WithCulture=false` so they stay in the core assembly rather than being split into satellite assemblies.

## Native dialog behavior

Settings and reset confirmation inherit `WidgetDialogWindow`, sharing transparent frameless windows and rounded dark XAML surfaces. Settings and reset confirmation declare WindowStartupLocation="CenterOwner" in XAML; no positioning callback is used. The duplicate-launch NoticeWindow uses the same shell and centers on screen without an owner. Header dragging excludes buttons. The close control preserves Cancel behavior, including reverting a Settings language preview.

## Verification

The executable test suite uses a real child process with synthetic JSON-line responses. `--live` checks the official local server without starting a model turn. `--capture` renders the actual WPF visuals into PNGs for full/compact/stale/settings review. Final visual evidence in `artifacts/preview-040` also includes earned-reset states, English UI, ring boundaries, countdown, tooltip and the WPF tray menu.

## Monitor bounds and countdown visibility

WindowPlacement uses GetWindowRect and SetWindowPos in physical pixels under the PerMonitorV2 manifest, retaining the selected screen during expansion. The work area excludes taskbars and supports negative screen origins. The main scroll viewer keeps content reachable when a monitor is smaller than the requested window. See [Microsoft DPI coordinate guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

IsVisibleChanged and StateChanged stop the local countdown clock while hidden/minimized and restart it with an immediate binding update when shown. The server timestamp remains authoritative, so the countdown never drifts from a paused integer counter. Quota polling is independent and continues in the tray. Global PreviewKeyDown handles Tab/Shift+Tab for owned WPF elements. ResetEntries lists returned future/undated available credits, sorted by expiry, and labels partial metadata without inventing missing dates.

## Extra info

`ExtraInfoSnapshot` and `UsageDetails` parse whitelisted metadata from account/read, account/usage/read, account/rateLimits/read and account/workspaceMessages/read. Unknown/null metrics remain nullable. The client batches independent reads and isolates per-endpoint failures, while using the same reconnectable stdio transport as quota monitoring. Thread/list supports search and cursor pagination; account/usage/read receives only the selected thread ID. Returned IDs are correlated before estimates are shown. Account notifications clear transient thread-token caches and invalidate the window's previous data; revision checks reject late responses from a prior account. No thread resume, model request or content hydration is issued.

`ExtraInfoWindow` owns a cancellable lifetime and a five-minute timer active only for visible data refreshes. Closing unsubscribes events and cancels pending work. `UsageChart` renders 7/30-day reported values without creating zero-value records for missing dates. Public projections exclude account email and raw response bodies. Demo captures use synthetic values only.

`QuotaAlerts` evaluates already-polled fresh snapshots and deduplicates notifications per quota window/threshold or expiring reset for the process lifetime. MainWindow uses a custom unowned QuotaNotificationWindow, governed by saved opt-in preferences. This introduces no endpoint, model-token or reset-consumption calls. Settings cloning preserves all notification preferences.

## GitHub release updates

`GitHubUpdates` reads the public latest stable release endpoint without credentials and selects a strict repository/tag/variant asset pair. Version parsing rejects prerelease tags and downgrade candidates. The archive and sidecar have bounded downloads; `UpdatePackages` verifies SHA-256, archive paths, duplicate/case-colliding entries, links, expanded size, product/version/variant and every owned-file hash. `scripts/publish.ps1` writes the package manifest before ZIP creation.

`UpdateController` runs on the WPF dispatcher, serializes manual and automatic operations, persists a last-check timestamp independently of user settings and throttles background checks to six hours. Preview/capture mode makes no GitHub request. Settings can manually check and prepare installation with auto-update disabled. A prepared automatic update waits until dialogs and quota/reset work are idle. Unknown pending reset identity is never interrupted.

`UpdateInstallation` creates a GUID-scoped LocalAppData job. A copy of the current bundled executable starts with `--apply-update` before ordinary app initialization and the single-instance gate. It validates the job and exact parent PID/start-time/executable, obtains a named installation semaphore, acknowledges readiness, and waits for the parent to exit. Package-owned files are backed up and atomically replaced per file, obsolete owned files are removed, and a journal enables rollback of only touched files. New-version files cannot overwrite unrelated local files. The restarted build must acknowledge startup within 20 seconds; otherwise the helper restores the previous files and restarts the old build. The most recent successful backup is retained, while completed staging/helper files and older successful backups are cleaned up. A crash/power loss during the transaction can require manual backup recovery; this is not a whole-directory atomic installer.

No GitHub publication or Codex/account endpoint is performed by the updater. All metadata and result files exclude account information. Both published variants are checked in preview mode and in invalid-plan helper mode by the release script.

## Custom quota notifications

QuotaNotificationWindow is a WPF tool window with ShowActivated=false, WS_EX_NOACTIVATE and a WM_MOUSEACTIVATE response of MA_NOACTIVATE. It has no owner, so hiding/minimizing the main window does not hide an active notification. It remains out of the taskbar and closes explicitly during app exit. Source hooks, display-change subscriptions and the dismissal timer are released on close.

WindowPlacement anchors the native rectangle inside the primary monitor's physical working area with DPI-scaled margins, and responds to display, size and DPI changes. Native preview also exercises placement on each attached monitor. A single visible popup coalesces incoming messages and opens the widget when its content or background is clicked; close and scrollbar interactions never trigger that action. Its timer uses the saved NotificationSeconds preference (2, 3 or 5, default 2), starts after the first input timestamp change, pauses while hovered, and restarts on mouse leave. New messages wait for fresh activity. GetLastInputInfo is sampled every 250 ms only while waiting in a visible popup; polling stops after activity or close. No hooks or input contents are captured. Native input-query failure falls back to timed dismissal. Existing QuotaAlerts selection and session deduplication remain unchanged. This is independent of the Windows notification broker and has no Notification Center history.

The non-activation hook follows [Microsoft's WM_MOUSEACTIVATE contract](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate): accept the click without activating the popup. English native captures exercise the real window styles, timer, hover, dismissal, hidden-owner independence and action callback without sending account notifications or spending resets.