# 0004. Active mode by default

- Status: accepted (supersedes the "view-only by default" part of 0003)
- Date: 2026-10-22
- Spec: RF8 ("modo somente visualizar (padrão para testes)"), criterion 9

## Context

v0.1.0 started in view-only mode. The first real user opened it, saw "Enabled" checked in the tray menu, pressed Alt+Tab, and nothing changed. They concluded the tool was broken. "Enabled" read as "working", while the mode that actually decides whether windows are hidden sat one submenu deeper.

The spec calls view-only the default *for testing*. That is a development and evaluation aid, not what someone who just downloaded the tool expects.

## Decision

- `AppSettings.Mode` defaults to `Active`. View-only stays in the tray menu and in `settings.json`.
- The first, non-clickable line of the tray menu and the icon tooltip show the effective state: `active`, `view only (not hiding)` or `disabled`.
- Anything that starts the real tray during development (`TrayStartupTests`, `build/smoke-tray.ps1`) writes `"Mode": "ViewOnly"` into its own `--data-dir`. A developer's own Alt+Tab during a test run therefore never hides their windows.

## Consequences

- A fresh install does what the README promises on the first Alt+Tab.
- Users who already saved settings keep their explicit choice. Only a missing `Mode` picks up the new default.
- Safety no longer depends on starting in view-only. It rests on the snapshot-before-hide invariant, start-up recovery and the emergency hotkey (RF7), which were always the real guarantees.
