# Plan 03 — Manual Test Checklist

> Acceptance criteria for **Plan 03 — Stage Lifecycle, Interaction & Multi-Monitor**. Run through this list on a real desktop before declaring Plan 03 done.

The checklist exercises the full Plan-03 surface (state machine + scene grouping + click/foreground/hotkey paths + multi-monitor + crash recovery) through the **Harness app** (`tests/App.Harness`) where possible, and through the production app (`app/src/App.Shell`) for the tray-icon path.

> **Numbering note.** Step numbers (1–13) match the **acceptance criteria** in `docs/plans/03-stage-lifecycle-interaction-multimonitor.md` §Acceptance Criteria 1-on-1. They appear out of order here because we group the steps physically (single-monitor first, then multi-monitor, then elevation) rather than by acceptance-criterion index. So expect to see step 5 followed by 12, 13, then 6–9.

## Prerequisites

- Windows 11 (the production target).
- PowerShell 7+ in your PATH (`pwsh`). The helper scripts live in
  `scripts/` and require it.
- At least one monitor; **two monitors** are required for steps 6–9.
- A clean session: close Stagehand if it was already running.
- Open these test apps before each run: 2 Notepad windows (same process), 1 Calculator, 1 Edge, 1 File Explorer, 1 additional app of your choice (Spotify, VS Code, etc.) — total 6 windows from 5 distinct processes.

## Helper scripts (run from the repo root)

```powershell
./scripts/build-release.ps1    # Build everything (CI parity)
./scripts/harness.ps1          # Launch the harness (Enable/Disable Stage buttons + Hook Log)
./scripts/run.ps1              # Launch the production app (tray icon + hotkey)
./scripts/run.ps1 --diagnostics    # …and open the log folder on startup
./scripts/check.ps1            # Build + tests + 4 format gates (run before committing)
```

Logs land in `%LOCALAPPDATA%\Stagehand\logs\` for the production app and
`%TEMP%\stagehand-harness-logs\` for the harness.

## Single-monitor checklist

### 1. Toggle round-trip (basic)

1. Launch the harness: `./scripts/harness.ps1`.
2. Click **Refresh** — the list should show your 6 windows (Plan-02 enumerator).
3. Click **Enable Stage (Plan 03)**.
4. **Expect:** 5 sidebar tiles appear (the two Notepads are merged into one scene); the previously-active window fills the main area; every other scene is parked off-screen.
5. Click **Disable Stage (Plan 03)**.
6. **Expect:** all 6 windows return to their pre-enable rectangles; sidebar overlay disappears; the work area is restored (taskbar reaches the full edge).

### 2. Soak (regression-resistance)

Repeat step 1 **20 times** without restarting the harness. Verify no degradation: each iteration's restoration must place every window at its exact pre-enable rectangle.

### 3. Click-swap (single-window scene)

1. Enable Stage.
2. Click any sidebar tile that represents a single-window scene.
3. **Expect:** that scene's window comes to the foreground and fills the main area; the previously-active scene is parked.

### 4. Click-swap (multi-window scene)

1. Enable Stage with the two Notepad windows running.
2. Click the Notepad scene's tile in the sidebar.
3. **Expect:** **both** Notepad windows are visible together in the main area at their pre-enable positions; the primary is on top; the previously-active scene moves to the sidebar as one tile.

### 5. Foreground-triggered swap (Alt-Tab)

1. Enable Stage.
2. Press **Alt-Tab** to a parked window.
3. **Expect:** Stagehand resolves the foreground window to its scene and swaps the entire scene to the front (multi-window scenes move all members together). The sidebar tile of the previously-active scene appears in the freed slot.

### 12. Win+Up Snap respects sidebar

1. Enable Stage.
2. With the active scene's primary window focused, press **Win+Up**.
3. **Expect:** the window maximises only into the main area (left edge starts at the sidebar's right edge — 200 px from the monitor's left); does not paint behind the sidebar.

### 13. Move-window-to-scene API smoke test

1. Enable Stage with the two Notepad windows in one scene.
2. Open **Diagnostics** (or use the harness's Hook Log) to capture the IDs.
3. Use the harness button or a follow-up code path to call
   `IStageController.MoveWindowToSceneAsync(secondNotepadIdentity, targetScene: null)`.
4. **Expect:** two single-window scenes ("Notepad" + "Notepad") in the sidebar; the multi-window badge is gone.

> _Plan 03 ships the API; the harness button surfaces in Plan 04. Test via custom probe in the meantime._

## Multi-monitor checklist (skip if only one display attached)

### 6. Independent stages per monitor

1. With two monitors connected, launch the production app (`./scripts/run.ps1`) and enable Stage from its tray icon.
2. **Expect:** one sidebar overlay per monitor; each monitor's parked scenes are scoped to that monitor (windows on monitor A appear in A's sidebar only).

### 7. Cross-monitor window drag

1. With Stage enabled, drag a window from monitor A to monitor B (Alt-Tab to it first; then move it).
2. **Expect:** the window's scene re-homes — within 500 ms it moves to monitor B's sidebar (or main area if it becomes B's active scene).

### 8. Hot-unplug

1. With Stage enabled on both monitors, physically unplug monitor B.
2. **Expect:** B's sidebar disappears; B's scenes still exist (Plan 04 will re-home them; for Plan 03 they may stay parked off-screen). No crash. Production app's log shows a `MonitorChangeCoordinator.Reconcile` entry.

### 9. Hot-replug

1. After step 8, plug B back in.
2. **Expect:** B's sidebar reappears within ~500 ms; the previously-managed monitor count returns to 2.

## Crash recovery

### 11. Crash + restore

1. Launch the production app via `./scripts/run.ps1` and enable Stage from the tray icon.
2. Force-kill the Stagehand process: `taskkill /F /IM Stagehand.exe` or via Task Manager (its name is `App.Shell.exe` until distribution renames it in Plan 05 — kill that one).
3. **Expect:** parked windows stay parked off-screen (visible Plan-03 limitation; documented).
4. Restart the production app via `./scripts/run.ps1`.
5. **Expect:** the log shows `Crash recovery: prior Stagehand left N stages with M windows; auto-restoring`. Within a second or two, every parked window returns to its pre-enable rectangle and the work area is restored. The snapshot file is deleted.

> _Plan 03 ships an auto-restore path. Plan 04 wraps this in a tray-balloon **Restore / Discard** prompt._

## Elevation boundary

### 10. Task Manager (elevated)

1. Open **Task Manager**.
2. Launch the production app via `./scripts/run.ps1` and enable Stage.
3. **Expect:** the log shows `StageController: HWND 0x… runs at a higher integrity level; left in place, marked IsElevated`. Stage continues to enable; Task Manager floats above the sidebar without being parked.
4. Disable Stage. The Task Manager window stays where it was.

## Tray-icon left-click + hotkey

In addition to the harness buttons:

- **Tray left-click** toggles Stage on/off.
- **Ctrl+Alt+S** (hard-coded in Plan 03) does the same.
- Both paths funnel through `IStageController.EnableAsync` / `DisableAsync` and obey the same state-machine rules.

## Known Plan-03 limitations (deferred to Plan 04 / Plan 05)

- No tray-balloon **Restore / Discard** prompt on crash (auto-restore only).
- No per-monitor scene re-homing on hot-unplug (scenes from a detached monitor stay parked).
- No animated swap (instant `SetWindowPos`).
- No multi-window scene `+N` badge (visual polish).
- Sidebar position / width / animation speed not user-configurable (hard-coded 200 px on the left).
- No drag-and-drop UI for `MoveWindowToScene` (API only).
