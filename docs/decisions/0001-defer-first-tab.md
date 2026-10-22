# 0001. Swallow the first Tab, hide, then re-inject it

- Status: accepted
- Date: 2026-10-22
- Spec: RF2, §5 "Corrida do seletor"

## Context

The system draws its Alt+Tab switcher as soon as it processes the Tab key-down. If the tool hides windows *in parallel*, the switcher can capture its window list before the hides land, and still show windows from other monitors.

Hiding *inside* the `WH_KEYBOARD_LL` callback is not an option either. The callback must return within `LowLevelHooksTimeout`, or Windows silently skips and eventually removes the hook. `ShowWindow` on another process's window is also a synchronous cross-process call that can hang on an unresponsive app.

## Decision

- The hook runs on its own thread with its own message loop (`KeyboardHook`). It only classifies keys (`AltTabDetector`) and posts work to the UI thread.
- On Alt+Tab the hook **swallows** the first Tab down and its matching Tab up.
- The UI thread plans, writes the snapshot, hides with `ShowWindowAsync`, and waits up to `HideTimeoutMs` until the windows are hidden. Then it **re-injects** Tab with `SendInput`, tagged with `KeyboardHook.InjectionMarker` so the hook lets it through.
- If Alt was already released by then (a quick tap), the tool replays the whole Alt(+Shift)+Tab, so the quick switch still happens, now limited to the current monitor.
- On Alt release, restoration waits for `EVENT_SYSTEM_FOREGROUND` (the switch landed) or `RestoreDelayMs`, whichever comes first.

`DeferFirstTab` (settings.json) turns the deferral off. In view-only mode nothing is deferred.

## Consequences

- The switcher opens after our hide work: ~10–40 ms on a warm desktop, measured by `Hiding_thirty_windows_takes_less_than_150_ms`.
- Ctrl+Alt+Tab (the sticky switcher) is ignored, because it stays open after Alt is released.
