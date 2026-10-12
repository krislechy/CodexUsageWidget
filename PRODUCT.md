# Codex Usage Widget

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop.

## Stack

C# / .NET 10 / WPF. A framework-independent core owns the quota protocol, reset metadata and localization catalogs.

## Users and Purpose

A Codex user wants to see the remaining subscription quota and reset dates at a glance while working, without manually refreshing it.

## Capabilities and Constraints

- Read general Codex subscription limits through the official local Codex CLI app-server.
- Subscribe to limit notifications and refresh every minute as a fallback. Notifications on our connection are not assumed to observe all other Codex clients.
- Show five-hour and weekly remaining percentages and local reset dates; absent quota windows stay unavailable rather than becoming zero.
- Offer draggable full and ring-only compact (104 × 104 DIP) always-on-top windows, local preferences and a dark WPF tray menu. Compact mode expands by double-click, Ctrl+M or the tray.
- English project documentation and English-default UI. Settings offer English, Russian, Ukrainian, Serbian (Cyrillic), French, German, Simplified Chinese and Japanese.
- Show available earned resets and their earliest known expiry; distinguish complete from partial expiry metadata.
- Consume one earned reset only after explicit in-app confirmation. Persist a retry identity before sending; uncertainty never creates a fresh reset attempt.
- No account credentials are read or stored. No prompts, turns, token purchases or automatic resets.

## Brand Commitments

The user approved `docs/design/mockup-01.png`: Apple Watch Activity-inspired pink and lime concentric rings on a dark surface. The final authorized semantics encode remaining quota: full rings mean all quota remains; empty rings mean none remains. Full-view numbers and compact tooltips show remaining quota. Settings and reset confirmation share a frameless rounded dark shell centered on the owner and clamped to the screen working area. A two-ring pink/lime icon identifies the app and tray.

## Delivery

Project name: Codex Usage Widget. Local Git repository and public GitHub repository authorized by the user. Windows executable and English documentation.

Current delivery policy: build and document locally. GitHub reads, pushes, releases and repository changes require an explicit user command; feature work does not authorize publishing. The 0.3.0 refinement is a local build.
