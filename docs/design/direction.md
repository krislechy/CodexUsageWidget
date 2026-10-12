# Approved direction

- Audience and task: a Windows Codex user glances at subscription headroom while working.
- Mode: Operate. The central mechanism is consumed quota drawn as Activity-style rings, paired with prominent remaining values and local reset dates.
- Visual authority: the user's approval of `mockup-01.png`. Pink five-hour and lime weekly rings, matte dark surface, native Windows typography and a compact companion mode.
- First viewport: title/actions, concentric rings, two quota rows, update status. The main interaction is switching to a small, draggable compact view without interrupting monitoring.
- Implementation: C# / WPF vector arcs and semantic text, not a raster screenshot. Actual window geometry, keyboard focus, error recovery and high-DPI readability govern native adaptations.
- Honest limits: compact dates/status are exposed on hover; event delivery across distinct Codex clients is not assumed; polling remains enabled.
- Delivery: an independent local application, public GitHub source and portable Windows build. Documentation is English; the UI defaults to English with eight selectable languages.
- Extension approved by the user: an earned-reset row below quota rows, with availability, earliest known expiry, an explicit action and spend/retry confirmation. The existing ring identity stays intact.

The user pinned and approved the direction before implementation. A new visual-world interview or alternate concept tournament would reopen that decision and is intentionally not part of this build.
