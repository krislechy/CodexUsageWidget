# Codex Usage Widget

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop.

## Stack

C# / .NET 10 / WPF. A framework-independent core owns the read-only Codex protocol and quota model.

## Users and Purpose

A Codex user wants to see the remaining subscription quota and reset dates at a glance while working, without manually refreshing it.

## Capabilities and Constraints

- Read general Codex subscription limits through the official local Codex CLI app-server.
- Subscribe to limit notifications and refresh every minute as a fallback. Notifications on our connection are not assumed to observe all other Codex clients.
- Show five-hour and weekly remaining percentages and local reset dates; absent quota windows stay unavailable rather than becoming zero.
- Offer draggable full and compact always-on-top windows, local preferences and a tray menu.
- English project documentation; Russian interface matching the approved concept.
- No account credentials are read or stored by the widget. No prompts, turns, credit redemption or account changes.

## Brand Commitments

The user approved `docs/design/mockup-01.png`: Apple Watch Activity-inspired pink and lime concentric rings on a dark surface. Rings close as usage grows; explicit numbers show the remaining quota.

## Delivery

Project name: Codex Usage Widget. Local Git repository and public GitHub repository authorized by the user. Windows executable and English documentation.
