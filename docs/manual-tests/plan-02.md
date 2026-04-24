# Plan 02 — Core Window Mechanics

This checklist verifies the behaviour added by
[`docs/plans/02-core-window-mechanics.md`](../plans/02-core-window-mechanics.md): the window
enumerator and filter, the window controller, DWM thumbnails, the WinEvent hook, and the sidebar
overlay. Run it from the manual harness (`tests/App.Harness`) against a real desktop session —
these behaviours touch the compositor and user input, so automated xUnit coverage cannot reach
them. The nine steps mirror §S10 of Plan 02.

## Environment

- Windows build: <fill in> (e.g. Windows 11 23H2 build 22631.xxxx)
- Monitor count and layout: <fill in>
- DPI scaling per monitor: <fill in>
- Display language / locale: <fill in>
- Other apps required: Calculator (for step 4), `regedit` launched **as administrator** (step 9),
  any second GUI app for alt-tab (step 7).

## Steps

| #   | Step                                                                                                                                                                | Expected                                                                                                                        | Observed | Pass/Fail |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | -------- | --------- |
| 1   | Launch the Harness. Click **Refresh**.                                                                                                                              | A list of visible top-level windows matching what is on screen; no desktop / taskbar entries.                                   |          |           |
| 2   | Click **Show Sidebar**.                                                                                                                                             | Transparent strip appears on the left edge; live thumbnails fill it; wallpaper still visible behind.                            |          |           |
| 3   | Resize one of the source windows.                                                                                                                                   | The matching thumbnail resizes in real time, without flicker.                                                                   |          |           |
| 4   | Open a new window (e.g. Calculator).                                                                                                                                | A new thumbnail appears in the sidebar within 500 ms.                                                                           |          |           |
| 5   | Close a tracked window.                                                                                                                                             | Its thumbnail disappears from the sidebar within 500 ms.                                                                        |          |           |
| 6   | Select a window in the ListBox, click **Park**, then click **Restore**.                                                                                             | After **Park** the window vanishes from the screen but its thumbnail stays live; **Restore** returns it to its prior rectangle. |          |           |
| 7   | Click **Register hook**, then alt-tab between apps.                                                                                                                 | `EVENT_SYSTEM_FOREGROUND` events log to the textbox for each switch.                                                            |          |           |
| 8   | Let the Harness run for 10 minutes with the sidebar visible.                                                                                                        | GDI and user-object counters in the Harness footer remain stable (±5) across the whole window.                                  |          |           |
| 9   | Launch `regedit` via right-click → **Run as administrator** (Task Manager alone no longer elevates on modern Windows). Select it in the ListBox and click **Park**. | `ElevationBoundaryException` is logged; the Harness does not crash and remains responsive.                                      |          |           |

## Sign-off

- [ ] All steps pass on Windows 11 build <fill in>
- [ ] Soak step 8 shows no GDI / user-object growth
- [ ] Elevation step 9 produces `ElevationBoundaryException` without crashing
- [ ] Results pasted into the pull-request description per Plan 02 §Verification
