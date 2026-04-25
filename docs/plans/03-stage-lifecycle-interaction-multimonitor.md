# Plan 03 — Stage Lifecycle, Interaction & Multi-Monitor

> Turns the mechanism from Plan 02 into a working product. Implements Phase 3 (enable/disable + Work Area), Phase 4 (click/hover/foreground interaction), and Phase 6 (multi-monitor) from `stage-manager-windows-plan.md`. After this plan, Stagehand has feature parity with the MVP slice of macOS Stage Manager.

---

## Context

Plan 02 delivered the raw mechanism: enumerating windows, parking them off-screen, bringing them back, and showing live DWM thumbnails inside a sidebar overlay. All of it was exercised manually from the harness; none of it was wired into a user-visible toggle.

This plan composes those pieces into a `StageController` with a real state machine, handles the Work Area coordination with Windows 11 Snap, wires click/hover/foreground interaction onto the sidebar, and — crucially — does all of this **monitor-aware** and **scene-aware from the start**. Multi-monitor is pulled forward from its original Phase 6 slot because retrofitting monitor awareness into a single-monitor codebase is painful; designing it in upfront is cheap. The same argument applies to **Scenes** (logical groupings of windows that belong together — the macOS-Stage-Manager primitive that motivates the whole product). Scenes do not appear in Plan 02 because Plan 02 only ships single-window mechanics; they are introduced here, where the lifecycle and state shape come together for the first time.

By the end of this plan, a user can click the tray icon, see the sidebar appear, click a thumbnail to swap windows, Alt-Tab to trigger an implicit swap, unplug a monitor and have Stagehand recover gracefully, and click the tray icon again to return the desktop to exactly its prior state.

Plans 04 and 05 then add polish (settings UI, acrylic, animations) and distribution (auto-update, release pipeline, edge cases). Nothing in those later plans should require changes to the lifecycle designed here.

---

## Goal

Ship a **usable v1 of Stagehand** where:

- Left-clicking the tray icon toggles the stage on and off.
- When enabled, a sidebar overlay appears on each managed monitor; the previously-active scene fills the remaining area; every other window is grouped into a scene and parked off-screen, each scene represented by a single sidebar tile (with a multi-window stack indicator when the scene has more than one window).
- Clicking a sidebar tile swaps the entire scene with the currently-active scene — every window in the incoming scene becomes visible together, every window in the outgoing scene moves to the sidebar together.
- Alt-Tab or taskbar clicks to a parked window are recognised as a swap of that window's scene and the overlay reshuffles accordingly.
- New windows are auto-assigned to a scene by process id (default; configurable in Plan 04). Windows can be moved between scenes via a `MoveWindowToScene` API (drag-and-drop UI in Plan 04).
- When disabled, the desktop is restored to exactly the state it was in before enabling (window positions, Z-order, Work Area).
- Multi-monitor works correctly: independent stages per monitor by default.
- All lifecycle failures are transactional — no half-enabled state.

This is Stagehand's MVP. Everything after this is polish.

---

## Acceptance Criteria

1. **Toggle round-trip.** With 6 visible windows on a single monitor (default scene grouping = `ByProcessId`, so two Notepad windows from the same process land in one scene), click tray icon once (enable), click again (disable). Every window returns to its exact prior rectangle and Z-order. Work Area is reset. No window is stranded off-screen. (Run 20 times consecutively without regression.)
2. **Click-swap latency.** Clicking a sidebar tile produces a visible swap of its entire scene (all incoming-scene windows on screen at their last positions inside the main area, all outgoing-scene windows moved to the sidebar slot) within **150 ms** perceived end-to-end (measured by frame counter in Harness debug mode).
3. **Foreground swap.** Alt-Tab to a parked window; its scene comes to the foreground (every window in the scene is visible, primary on top); the previously-active scene moves to the sidebar. No flicker, no jank.
4. **Multi-monitor independence.** On a 2-monitor setup, enable Stagehand. Each monitor shows its own sidebar with its own windows. Dragging a window from monitor A to monitor B re-registers it on B's stage within 500 ms.
5. **Hot-plug.** Unplug monitor B while Stagehand is enabled. Windows on B re-home to monitor A's stage. Replug B: the stage recreates B's sidebar with any windows that moved back.
6. **Crash restoration.** Force-kill the Stagehand process while enabled. Parked windows stay parked (visible limitation, documented). On next start, Stagehand detects the prior-run crash via a lockfile and offers to restore prior positions from a snapshot written at enable time.
7. **Work Area coexistence.** With Stagehand enabled, pressing `Win+Up` on the active window maximises it to fill only the area left of/right of the sidebar (not behind it). `Win+Left` / `Win+Right` snap halves are computed correctly.
8. **Elevated window.** Launching Task Manager does not crash Stagehand. Task Manager appears in the sidebar as a **non-parkable** entry (visually distinct in Plan 04; in this plan, logged as Warning and skipped from parking).
9. **Rollback on partial failure.** If parking fails halfway through `Enable` (simulated by a fault-injected controller), every window that was already parked is restored before surfacing the error to the user. Final state: `Disabled`.
10. **Unit tests.** The state machine and interaction handlers are covered by ≥30 unit tests.
11. **Manual checklist** at `docs/manual-tests/plan-03.md` — all steps verified.
12. **Auto-grouping by ProcessId (default).** Opening two Notepad windows produces one scene with two windows; opening one Notepad and one Calculator produces two scenes. `SceneGroupingMode = Manual` (configurable in Plan 04) produces one scene per window instead.
13. **Move window between scenes.** `IStageController.MoveWindowToScene(hwnd, targetSceneId)` moves the window from its current scene to the target; when the source scene becomes empty it is removed; when the target is `null` a new scene is created for the window. Round-trips cleanly through `StageState`.
14. **Scene-aware swap.** Swapping a 3-window scene activates all three windows together (all visible, primary on top by Z-order); swapping it back parks all three to the sidebar slot together. Snapshot file (§Design.9) survives a crash mid-swap and recovery restores the pre-swap layout.

---

## Scope

### In scope

- `StageController` with `EnableAsync` / `DisableAsync` and a proper state machine.
- `WorkAreaManager` (real implementation replacing Plan 01 stub).
- Per-monitor `SidebarOverlay` instances managed by a `StageOverlayHost`.
- **Scene data model** (`Scene`, `SceneId`, `StageState.Scenes`) and the `SceneGrouper` service that auto-assigns windows to scenes by ProcessId.
- **Scene-aware lifecycle**: `EnableAsync` parks a whole scene to one sidebar slot; `SwapAsync` operates on scenes (all windows in a scene move together); `DisableAsync` restores all windows in all scenes; `MoveWindowToScene` reshuffles scene membership without a stage-toggle.
- Interaction: click sidebar tile → swap that scene; hover → tooltip (placeholder UI, Plan 04 polishes); foreground hook → implicit scene-swap (the parked window's _scene_ becomes the active scene).
- Multi-monitor:
  - Per-monitor independent stages (default).
  - Monitor-aware enumeration.
  - `WM_DISPLAYCHANGE` handling.
  - Per-monitor Work Area.
- Snapshot-on-enable for crash recovery (records scenes, not flat windows).
- Integration of `WinEventHook` from Plan 02 to drive create/destroy/foreground/minimise events.
- Wiring tray left-click and a placeholder hotkey (`Ctrl+Alt+S` hard-coded) into the toggle.

### Out of scope

- Configurable sidebar position / width / blur → Plan 04.
- Settings UI → Plan 04.
- Smooth swap animation ("cover with a thumbnail" trick) → Plan 04. This plan produces a correct-but-abrupt swap; polish comes later.
- Virtual Desktops integration → Plan 05.
- Fullscreen / game detection → Plan 05.
- Auto-update → Plan 05.
- Acrylic/Mica, rounded corners, drop shadows → Plan 04.

---

## Design

### 1. State machine — detailed

From Plan 01 §Design.6:

```text
Disabled ──EnableAsync()──▶ Enabling ──(all steps OK)──▶ Enabled
                              │
                              └──(any step fails)──▶ Rollback ──▶ Disabled (error surfaced)

Enabled ──DisableAsync()──▶ Disabling ──(all steps OK)──▶ Disabled
                              │
                              └──(partial failure)──▶ Disabled (best-effort restore; error logged)
```

Reentrancy: `StageController` owns a `SemaphoreSlim(1,1)`; both methods take it. User actions (tray, hotkey, foreground-triggered swap) never concurrent with enable/disable.

Cancellation: both methods accept a `CancellationToken`. The tray icon passes `CancellationToken.None`; the app-shutdown path passes a linked token with a 5-second timeout, then escalates to `CancellationToken.None` for the restore pass (don't strand windows because we ran out of time).

### 1a. Scene data model

Plan 01 sketched `StageState` as a flat parked-window list. This plan replaces that with a scene-centric shape, where every parked or active window belongs to exactly one `Scene` on exactly one monitor.

```csharp
public readonly record struct SceneId(Guid Value)
{
    public static SceneId New() => new(Guid.NewGuid());
}

public sealed record Scene(
    SceneId Id,
    string Title,                             // derived from Primary's title; user-editable later (Plan 04)
    IReadOnlyList<ParkedWindow> Windows,      // 1..n, never empty (an empty Scene is removed)
    WindowIdentity Primary,                   // a member of Windows; renders as the sidebar tile's main thumbnail
    DateTimeOffset CreatedAt);                // for LRU ordering (Plan 04 soft-limit consumes this)

public sealed record ParkedWindow(
    WindowIdentity Identity,
    Rect OriginalBounds,
    string OriginalMonitorDeviceName,
    bool IsElevated);                         // populated when ElevationBoundaryException was caught (Plan 05 §Design.11)

public sealed record StageState(
    StagePhase Phase,
    IReadOnlyDictionary<string, IReadOnlyList<Scene>> ScenesByDevice,    // device name → scenes on that monitor, ordered by CreatedAt asc
    IReadOnlyDictionary<string, SceneId?> ActiveSceneByDevice,           // device name → active scene id (or null if monitor has none)
    IReadOnlyDictionary<string, Rect> SavedWorkAreasByDevice,
    IReadOnlyList<WindowIdentity> ElevatedPresent);                      // existing Plan 01 field, unchanged
```

Invariants:

- A `WindowIdentity` appears in **exactly one** `Scene` across the entire `StageState` — no duplicates across monitors or scenes.
- Every `Scene.Primary` is also in `Scene.Windows`.
- A scene with `Windows.Count == 1` is a single-window scene (the common case for unrelated apps); it renders identically to a "flat" parked window from the user's point of view, so backward-compatibility with the Plan 01 mental model holds.
- `ActiveSceneByDevice[device]` is either `null` (no active scene; main area shows wallpaper) or a scene id present in `ScenesByDevice[device]`. The active scene is the one whose windows fill the main area; all other scenes on that monitor are parked.
- `Scene` records are immutable; mutations go through `with` expressions that produce a new `StageState`. The same atomic-replacement pattern Plan 01 established for `StageState` itself.

The state's data direction stays **owned by `StageController`**: only that class produces new `StageState` snapshots. Subscribers (overlay host, settings binders) read; they never mutate.

### 2. `EnableAsync` — step-by-step

```text
1. Acquire state semaphore.
2. Phase := Enabling.
3. Enumerate monitors (per-monitor overlays will be created).
4. Enumerate manageable windows (via Plan 02 WindowEnumerator) — enumeration
   already preserves top-to-bottom Z-order from `EnumWindows`.
5. Decide active window per monitor: walk the enumeration result in order, and
   for each monitor's device name, assign the first non-minimised window whose
   `MonitorFromWindow` resolves to that monitor. If none, active = null for
   that monitor. (Do NOT use `GetWindow(GW_HWNDPREV)`; that returns the
   previous window in Z-order relative to a given HWND, not the topmost on a
   monitor. An equivalent top-down walk uses `GetTopWindow(null)` + repeated
   `GetWindow(hwnd, GW_HWNDNEXT)`.)
5a. Group windows into Scenes via `SceneGrouper.AssignToScene` (see §6a).
    - For each enumerated window, compute its scene assignment relative to the
      Scenes already built for the same monitor: same ProcessId as an existing
      scene's Primary → joined to that scene; else → new scene with this window
      as Primary.
    - The window picked in step 5 as the monitor's active window becomes the
      Primary of its scene; that scene becomes the monitor's `ActiveSceneByDevice`.
6. Write snapshot file %LOCALAPPDATA%\Stagehand\state\snapshot.json containing
   - For each scene: Id, Title, list of (Identity, original bounds, original
     monitor device name, IsElevated), Primary identity.
   - For each monitor: device name → saved Work Area + ActiveSceneId.
   (Device names are the canonical stable key — see Plan 01 §Design.6.)
7. Save original Work Areas.
8. Apply new Work Areas (reserve sidebar region per monitor).
9. For every scene that is NOT the active scene of its monitor — i.e. every
   parked scene — and for every window in that scene:
     a. WindowController.Park(hwnd, originalBounds). If
        ElevationBoundaryException is raised, mark that ParkedWindow.IsElevated
        = true, leave the window where it is (do not park), and include the
        scene in the sidebar with a shield overlay (Plan 05 §Design.11). The
        scene as a whole is still considered parked — its non-elevated windows
        are off-screen, and the elevated one floats above on its own.
     b. Add to the scene's ParkedWindow list.
10. For each monitor's active scene: for every window in the scene call
    WindowController.Resize(hwnd, mainArea) where mainArea = monitorRect minus
    sidebarRect. The scene's Primary is brought to the front via
    BringToFront(primary); other windows of the scene retain their previous
    pre-enable Z-order behind the Primary.
11. For each monitor: create SidebarOverlay, ShowOn(monitor),
    Sync(parkedScenesOnThatMonitor). Each scene renders as one tile; the tile's
    thumbnail is registered against scene.Primary; multi-window scenes get a
    stack indicator badge (Plan 04 polishes the visual).
12. Install WinEventHook for EVENT_OBJECT_CREATE | DESTROY |
    EVENT_SYSTEM_FOREGROUND | EVENT_SYSTEM_MINIMIZESTART |
    EVENT_SYSTEM_MINIMIZEEND | EVENT_OBJECT_LOCATIONCHANGE (for monitor-change
    detection).
13. Install display-change listener for WM_DISPLAYCHANGE via a hidden message
    window.
14. Phase := Enabled.
15. Release semaphore.
16. Raise StageController.Enabled event (used by tray icon to flip its state).
```

On failure at any step:

- Record the step index and the caught exception.
- Run rollback (§4) for whatever was already done.
- Phase := Disabled, release semaphore, rethrow wrapped as `StageTransitionException`.

### 3. `DisableAsync` — step-by-step

```text
1. Acquire state semaphore.
2. Phase := Disabling.
3. Uninstall display-change listener.
4. Uninstall WinEventHook.
5. For each overlay: HideAndRelease() — disposes thumbnails first, then closes the window.
6. Walk every Scene in every monitor's ScenesByDevice. For each Scene, restore
   its windows in reverse order (the scene's Primary last, so it ends on top
   within the scene's local Z-order). Across scenes, the order does not
   matter for correctness — only intra-scene order does. Skip
   ParkedWindow.IsElevated == true entries (we never moved them).
     WindowController.RestorePosition(hwnd, originalBounds).
7. For each monitor: WorkAreaManager.Restore(monitor, savedRect).
8. For each monitor's pre-enable last-active scene's Primary:
   WindowController.BringToFront(primary) — best effort, log Warning on denial.
   This restores the global topmost-window expectation the user had before
   enabling, since each monitor had exactly one foreground app per
   ActiveSceneByDevice.
9. Delete snapshot file.
10. Phase := Disabled.
11. Release semaphore.
12. Raise StageController.Disabled event.
```

Errors during disable do **not** throw. Each step is wrapped; the goal is to get as close to restored as possible. Final error (if any) is logged at Error and surfaced via tray notification, but `Phase` still transitions to `Disabled`.

### 4. Rollback

Rollback is the inverse of whatever `EnableAsync` completed. Keep a `Stack<Action>` of undo actions built during `Enable`; if any step fails, drain the stack. Each undo is best-effort; per-action errors are swallowed and logged.

This means `EnableAsync` looks like:

```csharp
var undo = new Stack<Func<Task>>();
try
{
    // step 6: write snapshot
    undo.Push(() => DeleteSnapshotAsync());

    // step 7-8: save + apply Work Areas
    foreach (var m in monitors) undo.Push(() => _workArea.Restore(m.Monitor, m.Saved));
    foreach (var m in monitors) _workArea.Apply(m.Monitor, sidebarReserved);

    // step 9: park windows
    foreach (var w in windowsToPark) {
        _controller.Park(w.Hwnd, w.Bounds);
        var captured = w;
        undo.Push(() => _controller.RestorePosition(captured.Hwnd, captured.Bounds));
    }
    // ...
}
catch (Exception ex)
{
    while (undo.TryPop(out var action))
        try { await action(); } catch (Exception inner) { _log.LogError(inner, "rollback step failed"); }
    throw new StageTransitionException(StagePhase.Disabled, StagePhase.Enabled, ex);
}
```

### 5. `WorkAreaManager`

```csharp
public interface IWorkAreaManager
{
    Rect GetCurrent(IntPtr monitor);              // HMONITOR accepted for transient calls only
    void Apply(IntPtr monitor, Rect newWorkArea);
    void Restore(IntPtr monitor, Rect savedWorkArea);
}
```

**Key discipline.** `IWorkAreaManager` takes `IntPtr monitor` (HMONITOR) because every call is transient-within-a-frame — by the time the call returns, the handle is no longer referenced. Persistent storage (StageState.SavedWorkAreasByDevice) uses device names per Plan 01 §Design.6. `StageController` translates between the two: when it needs to call `Apply`/`Restore`, it resolves the device name against the current `MonitorEnumerator` snapshot to get an HMONITOR; when it records a saved rect, it stores under the device name.

Uses `SystemParametersInfoW(SPI_SETWORKAREA, ...)` with flag `SPIF_SENDCHANGE` so Explorer repaints.

On multi-monitor: `SPI_SETWORKAREA` is per-monitor on Windows 10 1809+; confirmed by MSDN. Each call targets the monitor whose Work Area you're changing; internally this requires passing a `RECT` in screen coordinates, and the system resolves it to the intersecting monitor. We store a `Dictionary<IntPtr, Rect>` of saved rects keyed by HMONITOR.

Caveat: `SystemParametersInfoW` does not accept an HMONITOR directly; it infers the monitor from the rect. We compute the desired Work Area rect from the monitor's full bounds minus the sidebar reservation. Also, per documentation: for the system broadcast to fire correctly, `SPIF_UPDATEINIFILE` is **not** set (we don't want the change persisted across reboots).

Invariant: `Apply` is idempotent; calling it twice with the same rect is safe. `Restore` is always paired with exactly one `Apply` on the same monitor.

### 5a. `ISceneSwapExecutor`

```csharp
public sealed record SceneWindowMove(WindowIdentity Identity, Rect From, Rect To);

public sealed record SceneSwapPlan(
    SceneId IncomingSceneId,
    IReadOnlyList<SceneWindowMove> IncomingMoves,   // each window in the incoming scene: sidebar slot → main area
    SceneId? OutgoingSceneId,                       // null when no scene was active on this monitor
    IReadOnlyList<SceneWindowMove> OutgoingMoves,   // each window in the outgoing scene: main area → sidebar slot
    string TargetDeviceName,
    WindowIdentity IncomingPrimary);                // brought to front after the moves complete

public enum AnimationSpeed { Off, Fast, Normal, Slow }

public interface ISceneSwapExecutor
{
    Task RunAsync(SceneSwapPlan plan, AnimationSpeed speed, CancellationToken ct);
}
```

Plan 03 ships the default `InstantSceneSwapExecutor` (performs the `SetWindowPos` calls in a single batch + a sidebar re-sync; no animation). Plan 04 replaces the DI registration with `SwapAnimationController`, which shares the contract and adds the fake-thumbnail animation + queue management described in Plan 04 §Design.7.

`InstantSceneSwapExecutor.RunAsync`:

```text
1. For each move in OutgoingMoves: SetWindowPos(move.Identity.Hwnd, move.To, NOZORDER | NOACTIVATE).
2. For each move in IncomingMoves: SetWindowPos(move.Identity.Hwnd, move.To, NOZORDER | NOACTIVATE).
3. BringToFront(IncomingPrimary) — best effort.
4. Return.
```

A scene swap moves up to 2N windows where N is the larger of the two scenes' window counts. Even with 10-window scenes (rare), the 20 `SetWindowPos` calls run in sub-millisecond budget — the executor stays under the 1 ms ceiling that allowed Plan 03 §Design.7 to release the semaphore before the work.

No queue — under `Off`, swaps happen synchronously and the StageController's swap semaphore alone is enough. The queue contract kicks in only with the animated executor in Plan 04.

Plan 04's `SwapAnimationController` animates only the two scene **tiles** (incoming Primary in the incoming-scene's tile rect; outgoing Primary in the outgoing-scene's destination tile rect). The non-Primary windows of each scene are still moved synchronously underneath the cover of the tile animation; from the user's point of view, the entire scene appears to glide as one unit.

### 6. `StageOverlayHost`

```csharp
public interface IStageOverlayHost
{
    IReadOnlyList<IntPtr> ManagedMonitors { get; }
    void CreateForMonitor(IntPtr monitor);
    void DisposeForMonitor(IntPtr monitor);
    void SyncMonitor(IntPtr monitor, IReadOnlyList<WindowSnapshot> windows);
    void DisposeAll();
    event EventHandler<SceneClickedEventArgs>? SceneClicked;
}

public sealed record SceneClickedEventArgs(
    SceneId Scene,
    WindowIdentity ClickedWindow,   // the underlying window inside the tile (Plan 04 drag handlers use this)
    string DeviceName);
```

Maintains a `Dictionary<IntPtr, SidebarOverlay>` keyed by HMONITOR. Raises `SceneClicked` when a user clicks a tile inside any overlay; payload includes the resolved `SceneId`, the underlying clicked `WindowIdentity`, and the source monitor's device name.

Click detection: `SidebarOverlay` installs a `WndProc` hook. On `WM_LBUTTONUP`, it hit-tests the click against the last-layout's per-tile rects, and raises `SceneClicked` (the `WindowIdentity` that the user actually clicked is reported alongside the resolved `SceneId`, in case Plan 04's drag-and-drop or middle-click handling wants the per-window granularity). The overlay is `WS_EX_NOACTIVATE`, so the click doesn't steal focus from the active app.

### 6a. `SceneGrouper`

```csharp
public enum SceneGroupingMode
{
    ByProcessId,    // default: windows of the same process share a scene
    Manual,         // every new window starts its own scene; user moves them via Plan 04 drag-and-drop
}

public interface ISceneGrouper
{
    SceneAssignment AssignToScene(WindowSnapshot newWindow,
                                  IReadOnlyList<Scene> existingScenesOnSameMonitor,
                                  SceneGroupingMode mode);
}

public sealed record SceneAssignment(SceneId TargetScene, bool CreatedNewScene);
```

`AssignToScene` semantics:

- **`ByProcessId`**: walk `existingScenesOnSameMonitor`; if any scene's Primary has the same `ProcessId` as `newWindow.ProcessId`, return that scene id with `CreatedNewScene = false`. Otherwise mint a new `SceneId.New()` and return with `CreatedNewScene = true`.
- **`Manual`**: always return a new `SceneId.New()` with `CreatedNewScene = true`.
- Identity-based dedupe is **not** the grouper's concern — the caller (`StageController`) is responsible for never asking the grouper about a window that's already a member of any scene.

The grouper is a stateless pure function over the inputs; it takes no `ISettingsService` dependency. The caller passes the current `SceneGroupingMode` (sourced from settings via Plan 04's `StageBehaviorBinder`). This keeps it trivially testable and lets the same grouper be reused inside `MoveWindowToSceneAsync` for the "drop on empty area = new scene" case.

`MoveWindowToSceneAsync(window, targetScene)`:

1. Take state semaphore.
2. Find `window`'s current scene; remove it from there. If the source scene becomes empty, remove it from `ScenesByDevice`. If `window` was the source scene's `Primary`, promote the next member to Primary.
3. If `targetScene` is a non-null `SceneId`, append the window to that scene's `Windows` and validate the scene's monitor matches `window`'s current monitor (else log Warning and treat target as `null`).
4. If `targetScene` is `null`, mint a new scene with `window` as its only member and Primary, on `window`'s current monitor.
5. Re-`Sync` the affected monitor's overlay.
6. Release semaphore.

This is the API the Plan 04 drag-and-drop UI calls. No window movement happens — only the logical state and overlay tiles update; the parked window stays parked, the active window stays active.

### 7. Interaction handlers

Three interaction paths, all converging on `StageController.SwapAsync(SceneId target, string deviceName)`:

1. **Click a sidebar tile.** `StageOverlayHost.SceneClicked` → `StageController.SwapAsync(sceneId, device)`.
2. **Alt-Tab / taskbar click.** `WinEventHook.Fired(EVENT_SYSTEM_FOREGROUND)` → if the new foreground HWND is in any parked Scene, resolve to that scene's id via `StageState`, call `StageController.SwapAsync(sceneId, device)`.
3. **Hotkey** (hard-coded `Ctrl+Alt+S` in this plan, configurable in Plan 04) → toggles enable/disable.

The public API is therefore:

```csharp
Task SwapAsync(SceneId target, string deviceName, CancellationToken ct);
Task SwapByWindowAsync(WindowIdentity windowFromForegroundHook, CancellationToken ct); // resolves to scene internally
Task MoveWindowToSceneAsync(WindowIdentity window, SceneId? targetScene, CancellationToken ct); // null target = new scene
```

`SwapAsync` sequence:

```text
1. Acquire swap semaphore (separate from enable/disable to allow swaps during Enabled).
2. Validate Phase == Enabled and target is a parked scene on the given monitor;
   if not, no-op with Warning.
3. Build a SceneSwapPlan:
     outgoingScene = scene at ActiveSceneByDevice[device] (may be null if nothing was active)
     incomingScene = target scene (resolved via ScenesByDevice[device])
     outgoingTo = sidebar slot for outgoingScene's tile on that monitor
     incomingTo = main area rect for that monitor
     incomingZOrder = incomingScene.Primary on top, then other Windows by their
       previous pre-enable Z-order
4. Update state IMMEDIATELY (logical swap, before any real-window movement):
     ActiveSceneByDevice[device] := incomingScene.Id
     (the parked-vs-active distinction is purely the ActiveSceneByDevice
      mapping; Scenes themselves do not change identity or membership.)
5. Release swap semaphore.                              ← release BEFORE animation
6. Call ISceneSwapExecutor.RunAsync(plan, animationSpeed, ct).
   Plan 04 replaces the default executor (InstantSwapExecutor extended for
   scenes) with the animated SwapAnimationController; both share this
   contract. The executor owns its own serialisation queue: if a second
   Swap arrives while an animation is running, the executor coalesces — at
   most one "next" swap is queued; further incoming swaps are dropped with
   a Debug log. The state semaphore is NOT held during the animation.
7. After RunAsync completes:
     Sync overlay on that monitor with the new active-scene mapping
     (incoming scene's tile is removed from the sidebar; outgoing scene's
     tile appears in the slot the incoming scene vacated).
     BringToFront(incomingScene.Primary) — best effort. Other windows of the
     incoming scene rely on their relative Z-order from step 6's
     SetWindowPos calls.
```

**Why release the semaphore before the animation.** Holding the swap semaphore across an awaited animation (~250 ms in Plan 04's "Normal" speed) causes every subsequent user click within that window to pile up behind it, and a user who spam-clicks sees a cascade of animations instead of the product feeling responsive. The split is:

- **State mutation** is semaphored (atomic, <1 ms). No other Swap can observe an intermediate state.
- **Real-window movement + animation** runs without the semaphore. Concurrency is managed by `ISwapExecutor`, which enforces "one animation at a time, at most one queued".

This means a second click during an animation causes exactly one more swap (not two). Plan 04 §Design.7 owns the executor's queue logic.

**What about the outgoing window's real position between steps 4 and 7?** Between the state update (step 4) and the executor completing (step 7), the real windows have not moved yet. The outgoing window is still on-screen at its pre-click position; the sidebar thumbnail for it has not been registered. The animation layer — for the Plan 04 animated executor — displays interpolated DWM thumbnails of both windows, so the user sees the transition. The instant executor (default in Plan 03, pre-polish) simply does `SetWindowPos` for both windows synchronously and the sidebar re-syncs; this produces the abrupt-but-correct v1 behaviour.

Note: swap does **not** restore a window to its pre-enable bounds — it places it in the main area. Only `DisableAsync` restores pre-enable bounds.

Hover tooltip: `WM_MOUSEMOVE` hit-tests; after 500 ms dwell, shows a native tooltip (`System.Windows.Controls.ToolTip` positioned manually) with the window title. Plan 04 polishes this into an app-icon + title card.

### 8. Multi-monitor

Two modes, hard-coded for this plan (Plan 04 makes this configurable):

- **Independent** (default): each monitor has its own stage. Each window belongs to exactly one monitor's stage at a time. When a window is dragged to another monitor, it's re-homed.
- **PrimaryOnly** (not implemented in this plan; scaffolded): only the primary monitor gets a stage; windows on other monitors are ignored by Stagehand.

Monitor identity: `MONITORINFOEXW.szDevice` (device name) is the stable identifier across hot-plug. HMONITORs are not stable across hot-plug events. We store device names in the snapshot and re-resolve to HMONITORs on display changes.

Hot-plug handling:

- A hidden message window (`MessageOnlyWindow`) receives `WM_DISPLAYCHANGE`.
- On receipt: re-enumerate monitors, diff against `ManagedMonitors`.
  - For removed monitors: dispose their overlay; re-home their windows to the primary monitor's stage.
  - For added monitors: create a new overlay; enumerate windows on that monitor; add them to its stage.

Cross-monitor window drag:

- `EVENT_OBJECT_LOCATIONCHANGE` is noisy; we subscribe but coalesce.
- On location change, re-query `MonitorFromWindow` for the parked/active HWNDs. If a window's HMONITOR changed, move its registration to the new monitor's overlay.
- Parked windows physically live off-screen; their `MonitorFromWindow` reflects where they were moved by the user (e.g. dragged in from the sidebar). We detect that by comparing the sidebar coordinate to the reported monitor.

### 9. Snapshot / crash recovery

`%LOCALAPPDATA%\Stagehand\state\snapshot.json`:

```json
{
  "schemaVersion": 1, // snapshot schema is independent of settings schema; v1 here refers to the snapshot format only
  "createdAtUtc": "2026-04-24T14:22:11Z",
  "processId": 4128,
  "processStartTimeUtcTicks": 638481234567890123,
  "monitors": [
    {
      "deviceName": "\\\\.\\DISPLAY1",
      "savedWorkArea": { "x": 0, "y": 0, "w": 2560, "h": 1400 },
      "activeSceneId": "0d2f3a18-2c09-4a4f-8b19-2a7d2d0d6311"
    }
  ],
  "scenes": [
    {
      "id": "0d2f3a18-2c09-4a4f-8b19-2a7d2d0d6311",
      "monitorDeviceName": "\\\\.\\DISPLAY1",
      "title": "Visual Studio",
      "primary": {
        "hwnd": 132456,
        "processStartTimeUtcTicks": 638400000000000000
      },
      "createdAtUtc": "2026-04-24T14:22:11Z",
      "windows": [
        {
          "identity": {
            "hwnd": 132456,
            "processStartTimeUtcTicks": 638400000000000000
          },
          "originalBounds": { "x": 100, "y": 100, "w": 1200, "h": 800 },
          "originalMonitor": "\\\\.\\DISPLAY1",
          "isElevated": false
        }
      ]
    }
  ]
}
```

The `scenes` array is the authoritative state — flat-list "windows" no longer exists at the top level. A single-window scene serialises as a one-element `windows` array. Plan 05's Crash-Recovery prompt uses `scenes.length` and the sum of `scenes[*].windows.length` to phrase the user prompt ("Restore N stages with M windows?").

On app startup:

1. If `snapshot.json` exists AND its `processId` is no longer running as a Stagehand process, there was a crash. Log Warning.
2. Offer via tray balloon: _"Stagehand crashed while managing N stages with M windows. Restore previous positions?"_ where N = `scenes.length`, M = sum of `scenes[*].windows.length`. Two buttons: **Restore** / **Discard**.
3. **Restore**: read snapshot, call `WindowController.RestorePosition` for every still-existing HWND (identity check: HWND + process start time match); restore Work Areas. Delete snapshot.
4. **Discard**: delete snapshot; windows stay where they are.
5. Snapshot is deleted on clean `DisableAsync` (step 9 of §3).

Not offered in this plan: automatic recovery on startup. The user must confirm. Plan 04 may add an "auto-restore on crash" setting.

### 10. Integration with `WinEventHook`

Hooks installed on `Enable`, uninstalled on `Disable`:

| Event                         | Handler                                                                                     |
| ----------------------------- | ------------------------------------------------------------------------------------------- |
| `EVENT_OBJECT_CREATE`         | If passes filter & within 500 ms of enable: add to current monitor's parked list + overlay. |
| `EVENT_OBJECT_DESTROY`        | Remove from parked list + overlay; if active, pick next window as active.                   |
| `EVENT_SYSTEM_FOREGROUND`     | If HWND is parked: trigger `SwapAsync`.                                                     |
| `EVENT_SYSTEM_MINIMIZESTART`  | If parked: already off-screen, but sync overlay thumbnail opacity to 120 (dimmed).          |
| `EVENT_SYSTEM_MINIMIZEEND`    | Restore opacity to 255.                                                                     |
| `EVENT_OBJECT_LOCATIONCHANGE` | Coalesce → re-home on monitor change.                                                       |

All handlers run on the UI thread (Plan 02 §Design.5 guarantees marshalling). All state mutations go through `StageController` methods that take the state semaphore. Handlers log Debug with the event id + hwnd.

### 11. Tray and hotkey wiring

`TrayIconHost.LeftClick` (Plan 01 logged a TODO):

```csharp
public async Task HandleLeftClickAsync()
{
    if (_stage.IsEnabled) await _stage.DisableAsync(CancellationToken.None);
    else await _stage.EnableAsync(CancellationToken.None);
}
```

Hotkey: `HotkeyService.Register(new HotkeySpec(modifiers: Ctrl | Alt, key: S))` on startup (hard-coded here). On firing, calls the same handler. Plan 04 replaces with a configurable hotkey bound to settings.

---

## Subtasks

### S1 — `WorkAreaManager` real implementation

**Files**: `app/src/App.Interop/WorkAreaManager.cs` (replace stub), `app/src/App.Interop/MonitorEnumerator.cs` (new).

**API**: §Design.5.

**Tests**: `WorkAreaManagerTests` with fake `INativeWindowApi`: `Apply` calls `SystemParametersInfoW` with the correct rect + flags; `Restore` round-trips; multi-monitor maps correctly.

**Claude Code prompt**:

> Implement `WorkAreaManager` per Plan 03 §Design.5. Add `MonitorEnumerator.EnumerateAll()` wrapping `EnumDisplayMonitors` + `GetMonitorInfoW`, returning `MonitorDescriptor { Hmonitor, DeviceName, FullBounds, WorkArea }` (raw Win32-adjacent record, per Plan 01 §17). Register the real class in `ServiceConfiguration`.

### S2 — `StageOverlayHost` (scene-aware)

**Files**: `app/src/App.Shell/Overlay/StageOverlayHost.cs`, `IStageOverlayHost.cs`. Extends `SidebarOverlay` from Plan 02 with a click `WndProc` hook and scene-tile rendering.

**API**: §Design.6. The host's `SyncMonitor(monitor, scenes)` takes `IReadOnlyList<Scene>` instead of a flat window list — each scene becomes one tile. The tile's thumbnail is registered against `scene.Primary`; multi-window scenes get a small badge with the window count rendered at the tile's lower-right corner (16×16, "+N" style; Plan 04 polishes the visual).

**Tests**: `StageOverlayHostTests` — managing creation/disposal lifecycle against a fake overlay factory; click hit-testing math (tile-level, not window-level); badge rendered iff scene.Windows.Count > 1; `SceneClicked` event reports both the resolved `SceneId` and the underlying `WindowIdentity` of the click target inside the tile.

**Claude Code prompt**:

> Implement `StageOverlayHost` per Plan 03 §Design.6. The host owns a dictionary of `SidebarOverlay` instances keyed by HMONITOR. `SyncMonitor(monitor, scenes)` renders one tile per scene; the tile's live thumbnail comes from `scene.Primary`; multi-window scenes get a count badge. Extend `SidebarOverlay` with a click `WndProc` hook that hit-tests against the per-tile rects from the last layout pass and raises `SceneClicked(SceneId, WindowIdentity)` on the host.

### S3 — `StageController` + `Scene` data model + `InstantSceneSwapExecutor`

**Files**:

- `app/src/App.Core/Stage/Scene.cs`, `SceneId.cs`, `ParkedWindow.cs`, `StageState.cs` (extend Plan 01's skeleton)
- `app/src/App.Core/Stage/StageController.cs` (replace stub)
- `app/src/App.Core/Stage/ISceneSwapExecutor.cs`, `InstantSceneSwapExecutor.cs`, `SceneSwapPlan.cs`, `SceneWindowMove.cs`, `AnimationSpeed.cs`.

**API**:

```csharp
public sealed class StageController : IStageController
{
    public bool IsEnabled => _state.Phase == StagePhase.Enabled;
    public StageState Snapshot => _state;
    public event EventHandler? Enabled;
    public event EventHandler? Disabled;
    public Task EnableAsync(CancellationToken ct);
    public Task DisableAsync(CancellationToken ct);
    public Task SwapAsync(SceneId target, string deviceName, CancellationToken ct);
    public Task SwapByWindowAsync(WindowIdentity foregroundWindow, CancellationToken ct);
    public Task MoveWindowToSceneAsync(WindowIdentity window, SceneId? targetScene, CancellationToken ct);
}
```

**Dependencies** (via DI): `IWindowEnumerator`, `IWindowController`, `IWorkAreaManager`, `IStageOverlayHost`, `IWinEventHookFactory`, `ISceneGrouper`, `ISceneSwapExecutor`, `ISettingsService`, `UiDispatcher`, `IClock`, `ISnapshotStore`, `ILogger<StageController>`.

**Tests**: `StageControllerTests` with fakes for all dependencies. ≥25 cases:

- Happy enable single-window scenes: all steps called in order, final state Enabled.
- Happy enable with multi-window scenes (3-window scene from same process): one tile per scene; all 3 windows of the active scene resized to main area; primary on top.
- Happy disable: rollback in reverse order, every scene's windows restored.
- Enable fails at step 9 (parking the second window of a scene): rollback runs; state becomes Disabled; throws `StageTransitionException`.
- Concurrent Enable + Enable: second awaits first.
- `Disable` after failed `Enable`: no-op (already Disabled).
- SwapAsync when Disabled: logs Warning, no-op.
- SwapAsync with target = parked single-window scene: full swap (one window in, one window out).
- SwapAsync with target = parked 3-window scene: all 3 windows of incoming scene placed in main area, all windows of outgoing scene moved to sidebar slot, incoming primary BroughtToFront.
- SwapByWindowAsync with foreground HWND that is in a parked 2-window scene: resolves to scene, full scene swap.
- SwapByWindowAsync with foreground HWND not in any parked scene: no-op.
- `ElevationBoundaryException` during park: that ParkedWindow.IsElevated = true, scene still listed, stage still enables.
- Cancellation mid-Enable: rollback runs cleanly.
- MoveWindowToSceneAsync to existing scene: window moves; source scene shrinks (or removed if was last window); overlay re-syncs.
- MoveWindowToSceneAsync with null target: new scene is created, window becomes its sole member and Primary.
- MoveWindowToSceneAsync where source scene's Primary is moved out: next window of source promoted to Primary.
- MoveWindowToSceneAsync where target scene is on a different monitor: rejected with Warning, treated as null (= new scene on window's current monitor).

**Claude Code prompt**:

> Implement `Scene`, `SceneId`, `ParkedWindow`, the new `StageState` shape, and `StageController.EnableAsync`/`DisableAsync`/`SwapAsync`/`SwapByWindowAsync`/`MoveWindowToSceneAsync` per Plan 03 §Design.1a, §Design.1–4, §Design.6a, and §Design.7. Implement `InstantSceneSwapExecutor` per §Design.5a. Use a `Stack<Func<Task>>` for rollback. Gate state transitions with a `SemaphoreSlim(1,1)`. Raise `Enabled` / `Disabled` events on successful transitions. Write the 25+ unit tests listed.

### S3a — `SceneGrouper`

**Files**: `app/src/App.Services/Stage/SceneGrouper.cs`, `ISceneGrouper.cs`, `SceneGroupingMode.cs`, `SceneAssignment.cs`.

**API**: §Design.6a.

**Tests**: `SceneGrouperTests` — pure-function tests with no fakes:

- `ByProcessId` mode, no existing scenes → new scene.
- `ByProcessId` mode, existing scene with same ProcessId on the monitor → joined.
- `ByProcessId` mode, existing scene with same ProcessId on a _different_ monitor → new scene (monitor scoping is the caller's job, but the test pins the contract).
- `ByProcessId` mode, multiple existing scenes match ProcessId (shouldn't happen in practice) → returns the first match.
- `Manual` mode with any inputs → new scene.

**Claude Code prompt**:

> Implement `SceneGrouper` per Plan 03 §Design.6a. Stateless pure function. The five test cases above. The grouper does not depend on `ISettingsService` — the calling `StageController` reads `SceneGroupingMode` from settings and passes it.

### S4 — Interaction handlers

**Files**: `app/src/App.Shell/Interaction/StageInteractionCoordinator.cs`.

**API**:

```csharp
internal sealed class StageInteractionCoordinator : IDisposable
{
    public StageInteractionCoordinator(
        IStageController stage,
        IStageOverlayHost host,
        IWinEventHookFactory hooks,
        IWindowEnumerator enumerator,
        UiDispatcher ui,
        ILogger<StageInteractionCoordinator> log);

    public void Start();
    public void Stop();
}
```

`Start` subscribes to `host.SceneClicked` and installs the WinEventHook. `Stop` reverses.

**Tests**: coordinator tests with fakes verifying each interaction path (click → SwapAsync, foreground event for parked window → SwapAsync, foreground event for non-parked window → no-op, minimise/unminimise → opacity change).

**Claude Code prompt**:

> Implement `StageInteractionCoordinator` per Plan 03 §Design.7, §10. It is the only class that talks to both the overlay host and the hook; `StageController` exposes the state but does not subscribe to hooks. Write the event-handler unit tests.

### S5 — Monitor awareness and hot-plug

**Files**: `app/src/App.Shell/Monitors/DisplayChangeListener.cs`, `app/src/App.Shell/Monitors/MonitorChangeCoordinator.cs`.

**API**:

```csharp
internal sealed class DisplayChangeListener : IDisposable
{
    public event EventHandler? DisplayChanged;
    public DisplayChangeListener();
    public void Dispose();
}

internal sealed class MonitorChangeCoordinator
{
    public MonitorChangeCoordinator(IStageController stage, IStageOverlayHost host, MonitorEnumerator monitors, ILogger<MonitorChangeCoordinator> log);
    public Task OnDisplayChangedAsync();
}
```

`DisplayChangeListener` uses a `MessageOnlyWindow` (hidden HWND with its own `WndProc`) to catch `WM_DISPLAYCHANGE`.

`MonitorChangeCoordinator.OnDisplayChangedAsync`:

1. Take stage semaphore.
2. Re-enumerate monitors by device name.
3. Diff against current `host.ManagedMonitors`.
4. For removed: move their parked windows to the primary stage; dispose their overlay.
5. For added: create overlay; re-enumerate windows; assign by monitor.

**Tests**: `MonitorChangeCoordinatorTests` with scripted monitor add/remove; verifies re-homing logic.

**Claude Code prompt**:

> Implement `DisplayChangeListener` (hidden message-only window catching WM_DISPLAYCHANGE) and `MonitorChangeCoordinator` per Plan 03 §Design.8. The coordinator takes the stage semaphore to serialise with enable/disable/swap.

### S6 — Snapshot store and crash recovery

**Files**: `app/src/App.Services/Snapshot/ISnapshotStore.cs`, `SnapshotStore.cs`, `SnapshotFile.cs`.

**API**:

```csharp
public interface ISnapshotStore
{
    SnapshotFile? ReadIfStale();  // returns the file iff it exists AND its PID is no longer a running Stagehand
    Task WriteAsync(SnapshotFile file, CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
    bool Exists { get; }
}
```

Atomic writes (same pattern as `SettingsService`).

`ReadIfStale`:

- Open the file, parse (malformed JSON → log Warning, return null — never throws).
- `Process.GetProcessById(pid)`:
  - Throws `ArgumentException` (pid not found) → stale.
  - Throws any other exception (insufficient access, process ended race) → treat as stale and log at `Warning`.
  - Returns a Process handle:
    - Try to read `Process.StartTime.ToUniversalTime().Ticks`.
    - If that access throws (system processes, access denied) → treat as stale.
    - Else compare against the stored `processStartTimeUtcTicks` with a **±20 000-tick tolerance** (= 2 ms). Rationale: `Process.StartTime` is precise to ~15 ms on Windows; JSON round-tripping via `DateTimeOffset.UtcTicks` preserves full 100 ns precision, but the _original_ capture already had ms-level jitter. Anything outside ±2 ms means the pid was reused by a genuinely different process → stale.
- Stale → return content. Fresh → return null.

**Tests**: `SnapshotStoreTests` covering write/read/delete round-trip (with both single-window scenes and multi-window scenes serialised + deserialised); stale-detection variants (missing process, recycled PID, same process); `IsElevated` flag round-trips for elevated parked entries.

**Claude Code prompt**:

> Implement `SnapshotStore` per Plan 03 §Design.9 + §S6. The serialised shape is the scene-centric JSON in §Design.9 — the top-level array is `scenes`, not `windows`. Unit-test write/read round-trip for both single-window and 3-window scenes, plus the three stale-detection paths and the `isElevated` round-trip. The `ReadIfStale` method must not throw on malformed JSON — log Warning and return null.

### S7 — Crash-recovery prompt on startup

**Files**: `app/src/App.Shell/Recovery/CrashRecoveryCoordinator.cs`.

**API**:

```csharp
internal sealed class CrashRecoveryCoordinator
{
    public CrashRecoveryCoordinator(ISnapshotStore store, IWindowController controller, IWorkAreaManager workArea, TrayIconHost tray, ILogger<CrashRecoveryCoordinator> log);
    public Task RunAsync(CancellationToken ct);
}
```

Called from `App.OnStartup` after DI setup. Logic per §Design.9 steps 1-4. Uses `H.NotifyIcon.Wpf` balloon API for the prompt.

**Tests**: `CrashRecoveryCoordinatorTests` with fake tray + snapshot store.

**Claude Code prompt**:

> Implement `CrashRecoveryCoordinator` per Plan 03 §Design.9. Uses the tray to show a balloon with "Restore" / "Discard" buttons. On "Restore", call `WindowController.RestorePosition` for each still-valid identity and restore Work Areas.

### S8 — Tray and hotkey wiring

**Files**: `app/src/App.Shell/Tray/TrayIconHost.cs` (extend), `app/src/App.Services/Hotkey/HotkeyService.cs` (replace stub).

**API**: Plan 01 already declared the tray and hotkey interfaces. This subtask only adds wiring.

**Tests**:

- `TrayIconHostTests`: left-click toggles stage.
- `HotkeyServiceTests`: `RegisterHotKey` + `UnregisterHotKey` round-trip against a fake `NativeWindowApi`; failure to register logs and does not throw.

**Claude Code prompt**:

> Wire `TrayIconHost.LeftClick` to `StageController.EnableAsync`/`DisableAsync` per Plan 03 §Design.11. Implement `HotkeyService` with `RegisterHotKey` / `UnregisterHotKey` Win32 calls; register a hard-coded `Ctrl+Alt+S` in `App.OnStartup`. Plan 04 replaces the hard-coded spec.

### S9 — Hover tooltip (placeholder)

**Files**: `app/src/App.Shell/Overlay/HoverTooltipBehavior.cs`.

**API**: attached to `SidebarOverlay`; shows a simple tooltip with window title after 500 ms dwell.

**Tests**: none (Plan 04 tests the polished version).

**Claude Code prompt**:

> Implement `HoverTooltipBehavior` per Plan 03 §Design.7. Use a `DispatcherTimer` for dwell detection. Show a `ToolTip` positioned next to the thumbnail with the window title. No icon yet; Plan 04 polishes.

### S10 — End-to-end integration & manual checklist

**Files**: `docs/manual-tests/plan-03.md`, updated `tests/App.Harness` adding an **Enable Stage** / **Disable Stage** button pair that drives `StageController` directly.

**Checklist**:

1. Single monitor, 6 windows from 5 distinct processes (e.g. 2 Notepads, 1 Calculator, 1 Edge, 1 Explorer, 1 Spotify). Click **Enable Stage**. Expect: 5 sidebar tiles (the two Notepads share a scene); active scene fills main area; the Notepad scene's tile shows a "+1" badge for the second Notepad. Click **Disable Stage**. Expect: all 6 windows back in prior rectangles.
2. Repeat 20 times without degradation.
3. Click a single-window scene tile. Expect: swap within 150 ms; one window in, one window out.
4. Click the multi-window Notepad scene tile. Expect: both Notepads visible in the main area at their pre-enable positions, primary on top; the previously-active scene moves to the sidebar as one tile.
5. Alt-Tab to a parked window in a multi-window scene. Expect: Stagehand swaps the entire scene (both Notepads come forward together); sidebar updates.
6. Dual monitor. Enable. Expect: two sidebars, independent stages, scenes scoped per monitor.
7. Drag a parked thumbnail's source window across monitors (by Alt-Tabbing to it then moving). Expect: it re-homes (and its scene moves with it).
8. Unplug monitor B. Expect: its sidebar disappears; its scenes appear in A's sidebar (preserving scene grouping).
9. Replug monitor B. Expect: its sidebar reappears.
10. Launch Task Manager elevated. Expect: warning logged, Task Manager appears in its own scene's tile with a shield overlay; clicking it focuses but does not park.
11. Force-kill Stagehand while enabled. Restart. Expect: tray balloon "Restore N stages with M windows?" prompt; click Restore → windows return to pre-enable positions.
12. Press `Win+Up` on the active scene's primary window. Expect: maximises only to the main area (not behind sidebar).
13. **Move a window between scenes (API smoke test).** From the harness, call `MoveWindowToSceneAsync(secondNotepadHwnd, null)` to split the multi-window Notepad scene. Expect: two single-window scenes ("Notepad" and "Notepad" again) appear in the sidebar; the badge disappears from the original tile.

**Claude Code prompt**:

> Add the Enable/Disable buttons to the Harness per Plan 03 §S10. Author `docs/manual-tests/plan-03.md` with the 11-step checklist.

---

## Risks & Mitigations

| Risk                                                                             | Impact                    | Mitigation                                                                                                                                                                                 |
| -------------------------------------------------------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Rollback itself fails                                                            | Stranded windows          | Every rollback action is wrapped and logged; user is told to use the crash-recovery flow on next start.                                                                                    |
| Snapshot is stale because process start time parsing throws on permission denied | Lost recovery             | Fall back to treating any snapshot with unreachable PID as stale. Log Warning.                                                                                                             |
| `SetForegroundWindow` denials cause focus to land on sidebar                     | Input lost                | `SidebarOverlay` is `WS_EX_NOACTIVATE`; if focus still lands there, the `AttachThreadInput` fallback in `WindowController.BringToFront` is retried once. After one retry, give up and log. |
| Display-change storms during docking/undocking                                   | Thrash                    | Coalesce `WM_DISPLAYCHANGE` with a 200 ms debounce in `DisplayChangeListener`.                                                                                                             |
| Alt-Tab during enable transition                                                 | Phase mismatch            | Swap semaphore separate from state semaphore; swaps are rejected when Phase != Enabled.                                                                                                    |
| Window moves off all monitors (`MonitorFromWindow` returns null)                 | Crash on sync             | Treat null monitor as "primary"; log Warning.                                                                                                                                              |
| `EVENT_OBJECT_LOCATIONCHANGE` fires thousands of times during drag               | UI stalls                 | Debounce 100 ms per HWND; only act on `end`-of-drag (no location change for 200 ms).                                                                                                       |
| Work Area change races with another app that also sets it (e.g. custom taskbar)  | Fight for the rect        | On enable, record the rect we observed; on disable, restore to _that_ rect (not a re-queried current value). If another app has also modified it since, log Warning.                       |
| Parked window's process dies                                                     | Orphan thumbnail          | `EVENT_OBJECT_DESTROY` handler cleans up; `WindowController.RestorePosition` is a no-op on dead HWND.                                                                                      |
| Active-monitor assignment during enable picks a minimised window                 | Nothing visible on screen | Skip minimised windows when picking active; treat as parked. If all windows on a monitor are minimised, no active window; sidebar still shows them.                                        |

---

## Verification

1. **Unit tests.** ≥30 new cases across `StageControllerTests`, `StageInteractionCoordinatorTests`, `MonitorChangeCoordinatorTests`, `SnapshotStoreTests`, `CrashRecoveryCoordinatorTests`, `HotkeyServiceTests`, `WorkAreaManagerTests`. CI green.
2. **Soak.** Toggle enable/disable 20 times with 6 windows open; verify restoration is exact each time (measured by comparing pre- and post-bounds via harness logs).
3. **Manual checklist.** All 11 steps in `docs/manual-tests/plan-03.md` verified on a real dual-monitor rig.
4. **Crash recovery.** Force-kill Stagehand while enabled; restart; confirm balloon; confirm restore.
5. **Work Area coexistence.** `Win+Up` and Snap Layouts respect the reserved sidebar area.
6. **No regressions.** Plan 01 and Plan 02 manual checklists still pass.

---

## References

- `stage-manager-windows-plan.md` — §"Coexistence With Windows 11 Snap and Wallpapers", §"Core Windows APIs", Phase 3, Phase 4, Phase 6.
- Plan 01 §Design — state model, error handling, threading.
- Plan 02 §Design — `SidebarOverlay`, `DwmThumbnail`, `WindowEnumerator`, `WinEventHook`.
- `SystemParametersInfoW` SPI_SETWORKAREA — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow>
- `EnumDisplayMonitors` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors>
- `GetMonitorInfoW` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmonitorinfow>
- `WM_DISPLAYCHANGE` — <https://learn.microsoft.com/windows/win32/gdi/wm-displaychange>
- `RegisterHotKey` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey>
- `GetWindow` GW_HWNDPREV (Z-order walk) — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindow>
- Message-only windows — <https://learn.microsoft.com/windows/win32/winmsg/window-features#message-only-windows>
