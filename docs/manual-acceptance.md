# Manual acceptance checklist

Automated tests cover the logic (Core) and real window behaviour (Win32). What they cannot do is press physical keys in front of the system switcher on two physical monitors. Run this checklist on a two-monitor machine before tagging a release.

Setup: build or download `MonitorSwitch.exe` and start it. In the tray menu choose **Mode → Active**. Keep `%LOCALAPPDATA%\MonitorSwitch\monitorswitch.log` open in a tail (`Get-Content -Wait`).

| # | Spec criterion | Steps | Pass when |
| --- | --- | --- | --- |
| 1 | 1 | Put windows on both monitors. Cursor on monitor 1, Alt+Tab and hold. | The switcher lists only monitor 1's windows. |
| 2 | 2 | Make one window on monitor 2 minimized and one maximized. Cursor on monitor 1, Alt+Tab, Esc. | Monitor 2 looks exactly as before, still minimized and maximized. |
| 3 | 3, 4 | Same setup, Alt+Tab to another window on monitor 1, release. | The chosen window has focus; monitor 2 is unchanged. |
| 4 | — | Alt+Shift+Tab. | Reverse order, still only monitor 1. |
| 5 | — | Quick tap Alt+Tab. | Switches to the previous window *of this monitor*. |
| 6 | 6 | An app with a non-modal owned window (e.g. Notepad's Find) on monitor 2, owner on monitor 1. Alt+Tab from monitor 1. | The Find window is hidden; the owner is listed. |
| 7 | 7 | Open a modal dialog (File → Save As) on monitor 2. Alt+Tab from monitor 1. | The modal stays visible. |
| 8 | 8 | Hold Alt+Tab, then from another terminal: `Stop-Process -Name MonitorSwitch -Force`. Start it again. | The windows come back; a balloon says so. |
| 9 | 9 | Mode → View only. Alt+Tab. | Nothing is hidden; the log has `[view-only] ... would hide N`. |
| 10 | 10 | 30 windows open. Alt+Tab. | The switcher appears without visible delay; the log line `Hid N windows in X ms` shows X < 150. |
| 11 | RF7 | Press the emergency hotkey (default Ctrl+Alt+Shift+F12) while windows are hidden. | Everything is restored. |
| 12 | RF9 | Second virtual desktop with windows. Alt+Tab on the first one. | Nothing from the other desktop is touched; switching to it shows its windows intact. |
| 13 | RF7 | Log off with the tool running. Log back in. | No window is lost. |

If an app misbehaves while hidden (pauses, loses state), add it under **Exclusions...** and note it in [decision 0003](decisions/0003-open-questions.md).
