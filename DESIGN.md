---
name: Codex Usage Widget
description: Activity-inspired quota rings for a quiet Windows desktop companion
colors:
  surface: "#141619"
  text: "#F6F6F7"
  muted: "#ABB0BA"
  five-hour: "#FF397E"
  weekly: "#9BEF29"
  five-hour-track: "#3D1E2B"
  weekly-track: "#263719"
  outline: "#353940"
  separator: "#2A2D33"
  warning: "#F1B859"
  current: "#83E788"
  control-hover: "#282B31"
  control-pressed: "#353942"
  control-hover-text: "#FFFFFF"
  action: "#2B3036"
  action-hover: "#3B424C"
  action-outline: "#59616C"
  field: "#252930"
  field-outline: "#626A76"
  option-highlight: "#414A55"
typography:
  title:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "21px"
    fontWeight: 600
  percentage:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "29px"
    fontWeight: 700
  label:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "17px"
    fontWeight: 600
  detail:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "11px"
    fontWeight: 400
  settings-body:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "13px"
    fontWeight: 400
rounded:
  main: "26px"
  compact: "22px"
  control: "7px"
  action: "8px"
  select: "6px"
  checkbox: "5px"
  tray: "14px"
spacing:
  outer: "8px"
  content: "19px"
  row-gap: "8px"
  dialog-content: "23px"
  field-inset: "9px"
components:
  widget:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.main}"
  icon-button:
    textColor: "{colors.muted}"
    rounded: "{rounded.control}"
    size: "30px"
  icon-button-hover:
    backgroundColor: "{colors.control-hover}"
    textColor: "{colors.control-hover-text}"
    rounded: "{rounded.control}"
    size: "30px"
  icon-button-pressed:
    backgroundColor: "{colors.control-pressed}"
    rounded: "{rounded.control}"
    size: "30px"
  action-button:
    backgroundColor: "{colors.action}"
    textColor: "{colors.text}"
    rounded: "{rounded.action}"
    padding: "0px 14px"
    height: "36px"
  action-button-hover:
    backgroundColor: "{colors.action-hover}"
    textColor: "{colors.text}"
    rounded: "{rounded.action}"
    height: "36px"
  text-field:
    backgroundColor: "{colors.field}"
    textColor: "{colors.text}"
    padding: "{spacing.field-inset}"
  select:
    backgroundColor: "{colors.field}"
    textColor: "{colors.text}"
    rounded: "{rounded.select}"
    height: "30px"
---

# Design System: Codex Usage Widget

## Overview

**Creative North Star: "Activity-style quota rings"**

The user approved an Apple Watch Activity-inspired concept: concentric pink and lime rings on a near-black, rounded floating window. The interface is a glanceable utility. Exact quota semantics matter more than decorative effects. This identity uses native Windows WPF typography, controls and vector rendering.

The approved reference is [mockup-01.png](docs/design/mockup-01.png). Current actual WPF renders are in [artifacts/preview-030-final](artifacts/preview-030-final), including all eight locales, ring boundaries and the tray menu. [docs/screenshots](docs/screenshots) holds the current English and localized captures. All screenshot values are synthetic.

**Key Characteristics:**

- Matte dark surface with stable pink and lime window identities.
- Remaining quota rings paired with explicit remaining numbers in full view.
- Separate full and compact compositions with local reset details.
- Native Windows keyboard interaction and accessible quota semantics.
- English-default, locale-aware copy with reversible live Settings preview.
- Earned-reset availability with explicit spend confirmation and same-attempt recovery.

## Colors

Pink always identifies the five-hour window; lime always identifies the weekly window. Dark tinted tracks show depleted quota and remain visible for unavailable windows. Warning amber marks unavailable or stale status; it does not recolor a quota window.

Percentages remain white and use explicit labels so color is not the only cue. Muted text carries dates, supporting labels and status, with readable contrast against the surface.

The current-status dot uses soft green. Controls use darker gray fields and tonal hover backgrounds; lime also marks keyboard focus. Focus is a control state, distinct from the weekly quota's identity. Exact colors live in the frontmatter and map to the brushes and literals in `App.xaml`, `MainWindow.xaml` and `SettingsWindow.xaml`.

**The Quota Identity Rule.** Pink identifies five-hour quota and lime identifies weekly quota in both modes; warning states retain those identities.

## Typography

Use the native Segoe UI Variable / Segoe UI family for the approved Windows system typography. Titles are semibold; percentages are bold. Dates use the user's local time zone and selected locale; percentages and available-reset counts use the same culture. The UI defaults to English and supports English (en), Russian (ru), Ukrainian (uk), Serbian Cyrillic (sr), French (fr), German (de), Simplified Chinese (zh-CN) and Japanese (ja). Keep project documentation and design-panel examples in English.

The remaining-value role is the frontmatter's percentage token. The full ring center uses a bold five-hour remaining value (30 device-independent pixels) and semibold weekly remaining value (18). Compact mode contains no text. Settings inherit the same Segoe UI family at 13 and use a semibold heading at 24. WPF sizes recorded as `px` in portable tokens mean device-independent pixels, not physical display pixels. No explicit line-height or tracking scale is set.

## Layout

Main window: 386 × 598 device-independent pixels, including transparent margins. Header, 220-pixel ring assembly, two quota rows, earned-reset section, then the status/control footer. The expanded grid allocates 34, 244, 74, 74, 94 and 32 pixels to those rows; its inset is 19 horizontally, 14 above and 12 below within the eight-pixel outer margin. Full rows include remaining percentage and reset date. Compact window: 104 × 104 pixels with only concentric 68- and 48-pixel rings; no percentages, status text or inline buttons. Hover reveals quota values, local reset dates, earned resets and status. Double-click the rings, press Ctrl+M or use the tray mode action to expand; drag the surface to reposition.

The full and compact layouts are separate compositions, not a scaled screenshot. WPF vector rendering handles display scaling. Settings use the shared frameless dark rounded dialog shell (510 × 570), with an eight-pixel transparent outer margin, one-pixel outline and 23-pixel content inset. The language selector is 32 pixels high; the refresh selector remains 30 pixels high. Earned-reset confirmation is a 470-pixel-wide owner-centered dialog that sizes its height to wrapped content, with the same shell and inset. Both dialogs center on the owner after rendering and clamp their initial position to that monitor's working area; their headers are draggable and their close controls cancel. The duplicate-launch notice shares this shell and centers on screen when no owner exists. Keep every control reachable by keyboard and give icon controls accessible names. These are explicit desktop modes; there are no web breakpoints.

## Elevation & Depth

The implementation uses one subtle outline around the floating surface. No decorative glass, glow or background photography. Transparent window corners let the real desktop remain visible.

## Shapes

Concentric clockwise arcs start at twelve o'clock and have round caps. Outer: 220 pixels, inner: 170 pixels, both with an 18-pixel stroke. Compact arcs use 6-pixel strokes. Zero remaining leaves only the track; 100% remaining becomes a complete circle. Full and dialog shells use 26-pixel corners; compact uses 22. The tray popup uses 14-pixel corners and eight-pixel menu-row corners.

## Components

- `RingGauge`: remaining percentage, consistently in full and compact modes.
- Quota row: localized label, prominent remaining value, explicit remaining label and local reset date.
- Icon button: consistent stroked vector geometry, 30-pixel target, visible hover/pressed/focus states.
- Status: updating, current or stale. Errors retain the last good snapshot and expose recovery detail in a tooltip.
- Compact view: rings only; retain remaining numbers, quota reset dates, earned-reset availability/expiry and status details in the tooltip. Expansion uses double-click, Ctrl+M or the tray.

### Buttons

Icon buttons have transparent resting surfaces, muted vector strokes, tonal hover and pressed fills, and a lime one-pixel border when keyboard-focused. Disabled opacity is 0.35. Settings action buttons use filled gray surfaces, an outline and a lime focus border; disabled opacity is 0.4. Their template defines hover and focus, with no separate pressed fill.

### Inputs / Fields

Settings text fields use dark gray surfaces, gray outlines, nine-pixel padding and a lime caret, with native WPF text-entry focus behavior. The custom ComboBox has a six-pixel corner radius and changes its border to lime while keyboard focus is within it. Menu options use a lighter gray highlight and nine-by-six-pixel padding. Checkboxes use a custom dark 18-pixel box with five-pixel corners and a lime check; hover changes the box tone, keyboard focus marks its outline in lime, and disabled opacity is 0.4. Native WPF checking and keyboard behavior remain.

### Quota Rings and Status

Each ring exposes a UI Automation ProgressBar peer with a read-only RangeValue pattern from 0 to 100. Its name combines the quota window's remaining value and reset description with available percentage. Numeric accessibility values follow the quota data, rather than animation frames. The status text has a polite live setting. Tooltips carry full reset and recovery details where text is truncated.

Ring changes animate for 550 ms with exponential ease-out, from the current visible value so updates can interrupt cleanly. Respect the Windows client-area animation preference. No perpetual animation.

**The Remaining Ring Rule.** Arcs encode remaining quota: full means all quota remains, empty means none remains. Full-view numbers and accessible values use the same semantics; compact values live in the tooltip.

### Tray and App Icon

The tray uses a WPF dark rounded context menu with the shared Segoe UI family, muted dark separators, tonal hover and lime keyboard focus. It exposes show, mode, refresh, Settings, reset and exit actions. The brand icon is a near-black rounded square with only pink and lime round-capped arcs; `Assets/app.svg`, `Assets/app.png` and `Assets/app.ico` share this two-ring identity. The ICO is used by the application windows and tray.

### Language Selection

Settings shows the eight language choices by native name. Selecting a language previews copy immediately across the widget, Settings, tray labels, tooltips, errors and accessibility names. Dates and numbers follow the selected culture without changing the local time zone. Save persists the selection; Cancel or closing Settings restores the language active when the dialog opened. Preserve layout and quota color identities across locales; wrap explanatory dialog text and expose complete truncated details in tooltips.

### Earned Resets

The full window places earned resets below both quota rows. A semibold heading (14 device-independent pixels), muted availability count (12) and expiry detail (11) accompany an explicit gray action button. This section uses existing surface, separator, action and focus tokens; it adds no ring or quota color.

When expiry metadata is complete, label the earliest expiry. When details are capped or incomplete, label only the earliest known expiry and explain incompleteness in the tooltip. If no future expiry is known, say so; zero available resets has no expiry line. Preserve the last known metadata when an update omits it.

A new spend is disabled while updating, with empty or unknown credits, stale or failed quota data, no usable credit, or blocked pending-record recovery. Fresh data becomes stale after six minutes. The confirmation dialog explains spending one earned reset, repeats availability and expiry, and offers Cancel as the default and escape action. Confirmation is required before spending.

An uncertain outcome changes the action to Retry reset and explains that the earlier result is unknown. The retry uses the persisted attempt and credit identity; it can remain available with stale data to recover that attempt, but stays disabled during an operation or when the pending record cannot be recovered. Retry also opens confirmation. Clear the pending identity only after a definitive outcome.

**The Explicit Reset Rule.** Spending a reset requires confirmation with Cancel as default; an uncertain outcome retries the same persisted attempt.

## Do's and Don'ts

- Do preserve the approved concentric-ring identity across both modes.
- Do label absent windows as unavailable; do not invent a zero remaining value.
- Do keep dates close to the quota they describe.
- Do keep real quota data out of committed preview images.
- Do retain visible keyboard focus and read-only accessible quota semantics.
- Do preview language changes immediately and restore the previous language on Cancel.
- Do distinguish earliest known expiry from complete expiry metadata.
- Do keep reset spending explicit and preserve the same attempt for uncertain retries.
- Don't add an Apple logo, watch hardware frame, extra activity ring or decorative metrics.
