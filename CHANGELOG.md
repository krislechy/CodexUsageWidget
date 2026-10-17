# Changelog

## Unreleased

- Fixed native UI verification to accept owner-centered dialogs constrained by the monitor's working area, including DPI scaling. Added an oversized-dialog regression and coordinate diagnostics for CI failures.

## 0.10.3 — 2026-10-17 (local build)

- Renamed the notification setting to Notification display time and limited choices to 2 sec, 3 sec and 5 sec, with localized units and no explanatory text.
- Previous unsupported duration values fall back to 2 seconds. Activity detection and hover behavior remain unchanged.
- Built Portable and Lite locally and verified the English interface. No GitHub publication performed.

## 0.10.2 — 2026-10-17 (local build)

- Removed the Open widget button: clicking the notification card restores the widget; the cross only dismisses it.
- Added a saved notification duration in Settings, from 1 to 60 seconds, defaulting to 2. Unattended notifications wait for mouse or keyboard activity before starting the countdown. Hover pauses dismissal; leaving restarts it.
- Updated localized labels, English native interaction checks and both local Portable/Lite packages. No GitHub operations performed.

## 0.10.1 — 2026-10-17 (local build)

- Replaced Windows balloon notifications with a custom non-activating popup using the app's dark surface, rounded outline, ring colors and shared typography/buttons.
- Notifications anchor above the taskbar at the bottom-right of the primary monitor's working area, respecting DPI and display changes. The unowned window remains visible while the widget is hidden in the tray.
- A close control dismisses the popup and Open widget restores the main view. Notifications hide after 10 seconds; hovering pauses dismissal. Incoming alerts coalesce into the visible popup instead of opening a stack of windows.
- Existing opt-in thresholds, quota/reset polling and session deduplication are unchanged. Popups do not enter Windows Notification Center and are not governed by Windows notification settings.
- English native verification covers non-activation, tray-hidden visibility, coalescing, hover timing, monitor placement, manual dismissal and Open widget behavior. Portable and Lite packages are built locally; no GitHub publication is performed.

## 0.10.0 — 2026-10-16

- Published the accumulated changes since 0.4.0 in both Windows x64 Portable and Lite variants.
- Larger, clearer quota rings and countdowns; safe placement across monitors and display scaling; animated refill when changing view or restoring the widget. Tab traversal remains disabled by request.
- Entire reset details collapse under one heading, with a refined available-count badge, localized first-expiry summary and chronological rows. Every reset still requires confirmation, with durable retry identity for uncertain outcomes.
- Customizable five-hour ring, weekly ring and widget background colors, with live preview, cancellation, default-palette restoration and adaptive text contrast.
- Experimental Extra info: account/plan and backend permission; token statistics and 7/30-day charts; credits, extra quota buckets and spend controls; reset metadata, workspace notices, selected-chat token breakdowns and estimated costs. Missing account-dependent data remains unavailable.
- Optional low-quota and earned-reset expiry notifications use existing polling and session-local deduplication, with configurable thresholds.
- Automatic stable GitHub Releases updates, preserving Portable/Lite and settings. Settings includes Check for updates and Update now. Verified ZIP/owned-file manifests, a separate helper and rollback protect installation; automatic installation waits for dialogs and reset work.
- English-only routine UI verification, 32 regression tests, both packaged helper smoke checks and SHA-256 checksums. Documentation and release screenshots are in English.

For the first upgrade from 0.4.0 or another build without the updater, download and extract this release manually. Portable bundles its runtime; Lite requires Microsoft .NET 10 Desktop Runtime (Windows x64). Future compatible stable releases can update automatically. Costs in Extra info are estimates, not final billing; some metrics and workspace features depend on the account.

## 0.9.0 — 2026-10-16 (local build)

- Added GitHub Releases automatic updates for packaged Windows x64 Portable and Lite builds. Stable releases are checked after startup and every six hours; drafts, prereleases, same versions and downgrades are ignored. The package variant is preserved and no GitHub token is needed.
- Settings includes an automatic-update preference, **Check for updates** and **Update now**. Manual checks and installation work when automatic updates are off. Settings are saved before a requested restart; automatic installation waits for dialogs, active quota work and pending reset recovery.
- ZIP assets require their matching SHA-256 sidecar and an internal owned-file manifest. Downloads, extraction sizes, paths, duplicate entries, package identity and per-file hashes are validated. Development checkouts cannot be replaced in place.
- A copy of the existing single-file executable acts as the hidden updater. It waits for the exact parent process to exit, backs up package-owned files, preserves unrelated local files, replaces the package and checks the restarted app's startup acknowledgement. Installation or startup failure rolls back changed files and restarts the previous build. The latest successful backup is retained under LocalAppData.
- Added updater regression coverage for variant/version selection, public GitHub requests, checksum identity, traversal/corruption rejection, local-file preservation and actual rollback after a sharing violation. Both packaged helpers are smoke-checked with a rejected invalid plan; routine UI verification remains English-only.

## 0.8.0 — 2026-10-13 (local build)

- Added the experimental Extra info window, opened from the expanded widget footer. Its collapsible sections cover account/auth plan and backend usage permission; token activity, lifetime/peak/streak/longest-turn metrics and 7/30-day charts; all returned quota buckets, credits and spend controls; earned-reset grant/title/description metadata; workspace notices; selected-chat usage and estimated credit/USD costs.
- Chat selection supports search and cursor pagination. Model breakdowns show input, cached/new input and output tokens. Context, reasoning and last-turn values appear only when this connection receives the corresponding notification; no chat is resumed and no model turn is started.
- Data loads on opening and every five minutes while the window is visible. Endpoint failures are isolated, missing fields stay unavailable, and account changes invalidate prior data. Daily gaps are not invented zeroes; cost estimates and quota percentages remain separate.
- Optional Windows low-quota and reset-expiry notifications reuse existing polling, with configurable thresholds and session-local deduplication. They are disabled by default. The Extra info window never consumes a reset or sends an external message.
- Added parser, partial-RPC, thread-correlation and alert regression coverage plus English native captures. Read-only live checks confirmed account/usage/limits methods and paginated local thread listing; workspace messages are disabled for the current account.

## 0.7.1 — 2026-10-13 (local build)

- The reset heading explicitly identifies the first expiring credit. Compact day/hour units and spacing follow the selected language, including Russian `дн.` and `ч`, rather than hard-coded English suffixes.
- Refined the count badge into a smaller cranberry-red circle with a restrained outline and tabular white numerals. Zero and unknown counts use a neutral surface; larger numbers fit without clipping.
- The expiry tooltip includes the complete localized summary and exact date when the inline text is shortened. English-only routine checks and both local packages remain enabled.

## 0.7.0 — 2026-10-13 (local build)

- Settings customizes both quota ring colors and the widget background with palette swatches or #RRGGBB values. Changes preview immediately; Cancel restores the previous colors and Save persists them. Default colors restores the original palette.
- Widget text adjusts for readable contrast on custom backgrounds; ring tracks follow the selected colors. Settings scrolls when the monitor cannot fit the full content.
- Limit resets shows the earliest future credit expiry beside the heading: whole days from 24 hours onward, rounded-up hours below it. Rows remain sorted earliest first; partial metadata is labeled as a known expiry.
- English verification covers preview, invalid input, cancellation, JSON persistence, expiry boundaries and unordered server metadata. Reset confirmation remains mandatory.

## 0.6.0 — 2026-10-13 (local build)

- Reference-inspired reset rows: localized title and expiry with UTC offset at left, an individual Use reset button at right, separated by subtle lines in the existing dark theme.
- Header count and disclosure are aligned at the right. The entire section body still collapses.
- Every row opens mandatory confirmation identifying its expiry, with Cancel as default; after confirmation the selected ID is revalidated against current available credit metadata.
- Pending attempts disable all fresh row actions and expose an explicit same-attempt retry. Missing retry identity blocks sending instead of creating a new reset.
- English native checks exercise selection of the second row, cancellation without spending, expired-row rejection and accessible pending retry placement.

## 0.5.2 — 2026-10-13 (local build)

- Collapse the entire earned-reset body, including action, availability and expiry dates. The heading stays visible with a red circular, white-number available-count badge.
- Explicit dark tray separator styling removes the Windows theme's asymmetric inset.
- Switching compact/full or restoring from tray/minimized replays visible quota arcs from zero to their current percentage with the existing 550 ms ease-out. Displayed data and accessibility values remain authoritative; Windows reduced-animation settings are respected.
- English native preview verifies body visibility, badge counts, intermediate refill frames and final quota values.

## 0.5.1 — 2026-10-13 (local build)

- Earned-reset dates are collapsed by default behind a styled disclosure heading. Availability, nearest expiry and the reset action remain visible.
- Opening/closing the list adjusts the full window height and keeps it within its current monitor. Compact mode is unchanged.
- English native preview checks the collapsed default, disclosure expansion and recovered space.
- Documented available app-server data and prioritized possible widget extensions; no additional account queries are enabled.

## 0.5.0 — 2026-10-13 (local build)

- Larger compact rings (92/70 DIP) in a 128 × 128 tile, with a 13 DIP countdown; full rings grow to 232/184 DIP.
- Expansion and dragging keep the entire widget inside the current monitor's working area, using native physical coordinates for mixed-DPI desktops. Oversized windows fit the available area and scroll their content.
- Countdown clock stops while hidden in the tray or minimized, resumes immediately when shown, and keeps running in visible compact mode. Quota monitoring continues.
- Tab and Shift+Tab navigation are disabled across owned WPF controls and windows.
- Double-click anywhere in compact mode to expand; double-click the ring area in full mode to compact.
- The reset section lists every returned available reset with its local expiry date/time. Unknown or incomplete metadata is explicit; longer lists scroll.
- English-only native checks cover monitor-edge expansion, timer visibility, Tab suppression and reset-list completeness.

## 0.4.0 — 2026-10-12

- Live five-hour reset countdown in both ring centers, updated every second in HH:mm:ss format.
- Dark rounded tooltip theme shared across the app.
- Native XAML CenterOwner/CenterScreen placement replaces manual dialog positioning.
- Double-click anywhere on the compact tile expands it; tray refresh/reset actions removed.
- Reusable verification/release scripts build and publish both Portable and Lite with checksums and English screenshots.
- English-only routine UI verification; no per-language capture loop.

## 0.3.0 — 2026-10-12 (local build)

- Rings and their accessibility values now show available quota: full at 100%, empty at 0%.
- Compact mode is a 104 × 104 DIP tile with only two rings; double-click, Ctrl+M or the tray menu expands it.
- Shared rounded dark Settings, reset confirmation and WPF tray menu, with visible keyboard focus.
- Dialogs center on the widget, with a screen-edge clamp to keep every control visible.
- Multi-resolution two-ring application and tray icon, with SVG source and a reproducible icon generator.
- Updated local English screenshots and preview captures across all eight languages.
- Compressed self-contained Portable package and a much smaller Lite package requiring .NET 10 Desktop Runtime.
- Release packages omit developer mockups and all-language QA screenshots. No automatic GitHub publishing.

## 0.2.0 — 2026-10-12

- Earned reset availability and earliest known expiry, with clear labels for incomplete metadata.
- Explicit reset action and localized confirmation, including a safe persisted retry after an uncertain response.
- English-default interface and live language selection in Settings.
- Russian, Ukrainian, Serbian (Cyrillic), French, German, Simplified Chinese and Japanese catalogs.
- Localized dates, numbers, errors, tray controls and accessibility names.
- Expanded native layouts and preview/state checks across all eight languages.
- 22 dependency-free quota, protocol, reset and catalog regression tests.

## 0.1.0 — 2026-10-12

- Initial Windows WPF application with Activity-inspired consumed-quota rings.
- Five-hour and weekly remaining percentages and local reset dates.
- Automatic updates through app-server notifications and configurable polling.
- Full and compact layouts, tray controls, saved window position and optional startup.
- Recovery from missing CLI, network failures, timeouts and process exits.
- Read-only Codex integration, English documentation and Russian UI.
- Keyboard focus states and accessible read-only progress values.
- Dependency-free protocol regression suite and portable Windows x64 release.
