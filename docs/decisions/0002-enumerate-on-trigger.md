# 0002. Enumerate on trigger with a fast path, instead of an event-maintained window cache

- Status: accepted
- Date: 2026-10-22
- Spec: §5 "Consumo" (cache updated via `SetWinEventHook`, no polling), "Latência" (150 ms for 30 windows), RF9

## Context

The spec suggests a window cache kept up to date by `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE` and `EVENT_SYSTEM_FOREGROUND`. Two problems:

1. Moving a window between monitors fires none of those events, only the very chatty `EVENT_OBJECT_LOCATIONCHANGE`. A cache built on them would hold stale monitor assignments: exactly the data the tool decides on.
2. Profiling showed the cost is not in `EnumWindows` (3–5 ms for ~400 windows). It is in a few expensive calls made for windows that cannot matter, measured right after a restore on a busy desktop:

| Call | Cost | Needed for |
| --- | --- | --- |
| `GetWindowPlacement` on every window | 50–70 ms | only visible windows (hidden ones are never touched) |
| `IVirtualDesktopManager` (COM to Explorer) on cloaked windows | 32–35 ms | nothing: cloaked windows are kept anyway |
| `QueryFullProcessImageName` on every visible window | 9–11 ms | only hiding candidates (exclusion list) |

## Decision

- Enumerate on each trigger: always fresh, zero idle cost, no polling.
- `Win32WindowSystem.Describe` queries cheapest first. It stops at "not visible", and it resolves process names only for candidates.
- Virtual desktops (RF9): the shell cloaks windows on other desktops, so a visible, uncloaked window is on screen. The fast path relies on `DWMWA_CLOAKED` and calls `IVirtualDesktopManager` only in detailed mode (`--list`, the tray's plan view), where it gives the precise reason.
- The only WinEvent hook is `EVENT_SYSTEM_FOREGROUND`, used to restore as soon as the switch lands (see 0001).

## Consequences

- Planning takes 4–30 ms with ~400 windows (was 55–150 ms).
- Side benefit: `IsWindowOnCurrentVirtualDesktop` wrongly reports "other desktop" for a few ms after a window is shown. That used to leave freshly restored windows unhidden on a quick second Alt+Tab; the fast path never asks.
- `ScopedWindowSystem` in the Win32 tests depends on `EnumerateWindows` returning everything. Keep that contract if a cache is ever added.
