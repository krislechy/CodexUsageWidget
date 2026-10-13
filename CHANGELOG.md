# Changelog

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
