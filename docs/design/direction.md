# Approved direction

- Audience and task: a Windows Codex user glances at subscription headroom while working.
- Mode: Operate. The central mechanism is remaining quota drawn as Activity-style rings, paired with prominent remaining values and local reset dates.
- Visual authority: the user's approval of `mockup-01.png`. Pink five-hour and lime weekly rings, matte dark surface, native Windows typography and a compact companion mode.
- First viewport: title/actions, concentric rings, two quota rows, update status. The main interaction is switching to a small, draggable compact view without interrupting monitoring.
- Implementation: C# / WPF vector arcs and semantic text, not a raster screenshot. Actual window geometry, keyboard focus, error recovery and high-DPI readability govern native adaptations.
- Honest limits: compact values/dates/status are exposed on hover; event delivery across distinct Codex clients is not assumed; polling remains enabled.
- Delivery: an independent local application, public GitHub source and portable Windows build. Documentation is English; the UI defaults to English with eight selectable languages.
- Extension approved by the user: an earned-reset row below quota rows, with availability, earliest known expiry, an explicit action and spend/retry confirmation. The existing ring identity stays intact.

The user pinned and approved the direction before implementation. A new visual-world interview or alternate concept tournament would reopen that decision and is intentionally not part of this build.

- Final authorized refinement: full rings mean all quota remains and empty rings mean none. Compact is a 104 × 104 DIP ring-only surface with double-click, Ctrl+M and tray expansion. Shared frameless rounded dark Settings/reset dialogs center on the owner and clamp to the monitor working area. The WPF dark tray menu and two-ring app icon carry the same near-black, pink, lime and Segoe grammar. The independent finish review of actual WPF captures in `artifacts/preview-030-final` returned SHIP with no material issues.
