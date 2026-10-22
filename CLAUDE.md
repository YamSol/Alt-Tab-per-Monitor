# MonitorSwitch

Windows tray tool: Alt+Tab shows only the windows of the monitor under the cursor. It hides the other monitors' windows with `SW_HIDE` while the switcher is open, then restores them exactly. The spec (Portuguese) is `Spec v2 Alternador de janelas por monitor (Windows).md`: requirements are `RF1`–`RF9`, acceptance criteria are §6 1–10. Cite those ids in code comments, tests and commits.

## Toolchain

.NET 8 SDK pinned by `mise.toml` and `global.json`. Prefix dotnet commands with `mise exec --`:

```powershell
mise exec -- dotnet test                                   # all tests; Win32 tests flash real windows
mise exec -- dotnet test tests/MonitorSwitch.Core.Tests    # fast, no windows
mise exec -- dotnet run --project src/MonitorSwitch.App -- --list
```

Coverage gate (CI): `build/check-coverage.ps1`. Core ≥ 90% lines, total ≥ 80%.

## Layout and boundaries

- `MonitorSwitch.Core` (net8.0): all decisions. Rules, plan, snapshot, restore, key state machine, settings. Zero P/Invoke; CI runs its tests on Linux to keep it that way.
- `MonitorSwitch.Win32`: the only P/Invoke. `Win32WindowSystem` implements Core's `IWindowSystem`; plus the keyboard hook, input injection and the WinEvent hook.
- `MonitorSwitch.App`: WinForms tray, wiring, CLI (`--list`, `--list-all`, `--restore`).
- New behaviour goes into Core behind `IWindowSystem` and gets tested with `FakeWindowSystem`. Win32 stays a thin translation.

## Invariants (break one and users lose windows)

- **Snapshot before hide**: `state.json` is on disk before the first `Hide`, and deleted only after restore (RF3, RF7). `SwitchController` owns this.
- **Only what we hid**: restore touches snapshot entries only. A window the app re-showed, closed, or whose handle now belongs to another pid is left alone (RF5, O4).
- **Hook thread stays tight**: the `WH_KEYBOARD_LL` callback classifies and posts, nothing else. Slow work there gets the hook silently removed by Windows.
- **Async toward other processes**: `ShowWindowAsync`, `SWP_ASYNCWINDOWPOS`. A hung app must never block the tool.
- **Privacy**: logs and `state.json` hold handles and process names, never titles or keys.
- **View-only is the default mode**; every change must keep it touching zero windows (criterion 9).

## Gotchas learned the hard way

- `GetWindowText` on a window of *your own* process sends `WM_GETTEXT` to its thread. In-process test windows therefore distort timing. The latency test uses `WindowFarm.exe` processes.
- `IsWindowOnCurrentVirtualDesktop` says "other desktop" for a few ms after a window is shown. The fast path trusts `DWMWA_CLOAKED` instead ([decision 0002](docs/decisions/0002-enumerate-on-trigger.md)).
- `MonitorSwitch.exe` is a GUI-subsystem exe: PowerShell does not wait for it. Use `Start-Process -Wait -PassThru` to get the exit code.
- Win32 tests must go through `ScopedWindowSystem`, so only the test's own windows can ever be hidden on a developer machine.

## Where to look

- Why the design is the way it is: `docs/decisions/` (deferred Tab, enumeration fast path, answers to spec §7).
- Before a release, or when touching keyboard or switch flow: `docs/manual-acceptance.md`. The two-monitor checks no CI can run.
- Agents in `.claude/agents/`: `spec-auditor` (does the code meet RF/criteria?), `win32-reviewer` (P/Invoke, hooks, threading), `test-author` (Core tests with `FakeWindowSystem`).
