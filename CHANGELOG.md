# Changelog

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
