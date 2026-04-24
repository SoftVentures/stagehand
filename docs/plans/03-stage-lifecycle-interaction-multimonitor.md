# Plan 03 — Stage Lifecycle, Interaction & Multi-Monitor

> Turns the mechanism from Plan 02 into a working product. Implements Phase 3 (enable/disable + Work Area), Phase 4 (click/hover/foreground interaction), and Phase 6 (multi-monitor) from `stage-manager-windows-plan.md`. After this plan, Stagehand has feature parity with the MVP slice of macOS Stage Manager.

---

## Context

Plan 02 delivered the raw mechanism: enumerating windows, parking them off-screen, bringing them back, and showing live DWM thumbnails inside a sidebar overlay. All of it was exercised manually from the harness; none of it was wired into a user-visible toggle.

This plan composes those pieces into a `StageController` with a real state machine, handles the Work Area coordination with Windows 11 Snap, wires click/hover/foreground interaction onto the sidebar, and — crucially — does all of this **monitor-aware from the start**. Multi-monitor is pulled forward from its original Phase 6 slot because retrofitting monitor awareness into a single-monitor codebase is painful; designing it in upfront is cheap.

By the end of this plan, a user can click the tray icon, see the sidebar appear, click a thumbnail to swap windows, Alt-Tab to trigger an implicit swap, unplug a monitor and have Stagehand recover gracefully, and click the tray icon again to return the desktop to exactly its prior state.

Plans 04 and 05 then add polish (settings UI, acrylic, animations) and distribution (auto-update, release pipeline, edge cases). Nothing in those later plans should require changes to the lifecycle designed here.

---

## Goal

Ship a **usable v1 of Stagehand** where:

- Left-clicking the tray icon toggles the stage on and off.
- When enabled, a sidebar overlay appears on each managed monitor; the previously-active window fills the remaining area; all other manageable windows are parked off-screen and represented by live thumbnails.
- Clicking a sidebar thumbnail swaps it with the currently-active window.
- Alt-Tab or taskbar clicks to a parked window are recognised as a swap and the overlay reshuffles accordingly.
- When disabled, the desktop is restored to exactly the state it was in before enabling (window positions, Z-order, Work Area).
- Multi-monitor works correctly: independent stages per monitor by default.
- All lifecycle failures are transactional — no half-enabled state.

This is Stagehand's MVP. Everything after this is polish.

---

## Acceptance Criteria

1. **Toggle round-trip.** With 6 visible windows on a single monitor, click tray icon once (enable), click again (disable). Every window returns to its exact prior rectangle and Z-order. Work Area is reset. No window is stranded off-screen. (Run 20 times consecutively without regression.)
2. **Click-swap latency.** Clicking a sidebar thumbnail produces a visible swap (incoming window on screen, outgoing in the sidebar) within **150 ms** perceived end-to-end (measured by frame counter in Harness debug mode).
3. **Foreground swap.** Alt-Tab to a parked window; the window comes to the foreground and fills the main area; the previously-active window moves to the sidebar. No flicker, no jank.
4. **Multi-monitor independence.** On a 2-monitor setup, enable Stagehand. Each monitor shows its own sidebar with its own windows. Dragging a window from monitor A to monitor B re-registers it on B's stage within 500 ms.
5. **Hot-plug.** Unplug monitor B while Stagehand is enabled. Windows on B re-home to monitor A's stage. Replug B: the stage recreates B's sidebar with any windows that moved back.
6. **Crash restoration.** Force-kill the Stagehand process while enabled. Parked windows stay parked (visible limitation, documented). On next start, Stagehand detects the prior-run crash via a lockfile and offers to restore prior positions from a snapshot written at enable time.
7. **Work Area coexistence.** With Stagehand enabled, pressing `Win+Up` on the active window maximises it to fill only the area left of/right of the sidebar (not behind it). `Win+Left` / `Win+Right` snap halves are computed correctly.
8. **Elevated window.** Launching Task Manager does not crash Stagehand. Task Manager appears in the sidebar as a **non-parkable** entry (visually distinct in Plan 04; in this plan, logged as Warning and skipped from parking).
9. **Rollback on partial failure.** If parking fails halfway through `Enable` (simulated by a fault-injected controller), every window that was already parked is restored before surfacing the error to the user. Final state: `Disabled`.
10. **Unit tests.** The state machine and interaction handlers are covered by ≥30 unit tests.
11. **Manual checklist** at `docs/manual-tests/plan-03.md` — all steps verified.

---

## Scope

### In scope

- `StageController` with `EnableAsync` / `DisableAsync` and a proper state machine.
- `WorkAreaManager` (real implementation replacing Plan 01 stub).
- Per-monitor `SidebarOverlay` instances managed by a `StageOverlayHost`.
- Interaction: click thumbnail → swap; hover → tooltip (placeholder UI, Plan 04 polishes); foreground hook → implicit swap.
- Multi-monitor:
  - Per-monitor independent stages (default).
  - Monitor-aware enumeration.
  - `WM_DISPLAYCHANGE` handling.
  - Per-monitor Work Area.
- Snapshot-on-enable for crash recovery.
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
6. Write snapshot file %LOCALAPPDATA%\Stagehand\state\snapshot.json containing
   - For each window: Identity, original bounds, original monitor device name.
   - For each monitor: device name → saved Work Area.
   (Device names are the canonical stable key — see Plan 01 §Design.6.)
7. Save original Work Areas.
8. Apply new Work Areas (reserve sidebar region per monitor).
9. For every non-active window:
     a. WindowController.Park(hwnd, originalBounds) — if it raises ElevationBoundaryException, log Warning, skip parking but include in sidebar as non-parkable.
     b. Add to Parked list.
10. For each monitor's active window: WindowController.Resize(activeHwnd, mainArea) where mainArea = monitorRect minus sidebarRect.
11. For each monitor: create SidebarOverlay, ShowOn(monitor), Sync(parkedWindowsOnThatMonitor).
12. Install WinEventHook for EVENT_OBJECT_CREATE | DESTROY | EVENT_SYSTEM_FOREGROUND | EVENT_SYSTEM_MINIMIZESTART | EVENT_SYSTEM_MINIMIZEEND | EVENT_OBJECT_LOCATIONCHANGE (for monitor-change detection).
13. Install display-change listener for WM_DISPLAYCHANGE via a hidden message window.
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
6. For each window in Parked (in reverse order, so the topmost-at-enable-time ends up top now):
     WindowController.RestorePosition(hwnd, originalBounds).
7. For each monitor: WorkAreaManager.Restore(monitor, savedRect).
8. WindowController.BringToFront(lastActiveHwnd) — best effort, log Warning on denial.
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

### 5a. `ISwapExecutor`

```csharp
public sealed record SwapPlan(
    WindowSnapshot Incoming, Rect IncomingFrom, Rect IncomingTo,
    WindowSnapshot Outgoing, Rect OutgoingFrom, Rect OutgoingTo,
    string TargetDeviceName);

public enum AnimationSpeed { Off, Fast, Normal, Slow }

public interface ISwapExecutor
{
    Task RunAsync(SwapPlan plan, AnimationSpeed speed, CancellationToken ct);
}
```

Plan 03 ships the default `InstantSwapExecutor` (performs two `SetWindowPos` calls + a sidebar re-sync; no animation). Plan 04 replaces the DI registration with `SwapAnimationController`, which shares the contract and adds the fake-thumbnail animation + queue management described in Plan 04 §Design.7.

`InstantSwapExecutor.RunAsync`:

```text
1. SetWindowPos(outgoing, to = outgoingTo, NOZORDER | NOACTIVATE)
2. SetWindowPos(incoming, to = incomingTo, NOZORDER | NOACTIVATE)
3. Return.
```

No queue — under `Off`, swaps happen synchronously and the StageController's swap semaphore alone is enough (since step 6 is <1 ms). The queue contract kicks in only with the animated executor in Plan 04.

### 6. `StageOverlayHost`

```csharp
public interface IStageOverlayHost
{
    IReadOnlyList<IntPtr> ManagedMonitors { get; }
    void CreateForMonitor(IntPtr monitor);
    void DisposeForMonitor(IntPtr monitor);
    void SyncMonitor(IntPtr monitor, IReadOnlyList<WindowSnapshot> windows);
    void DisposeAll();
    event EventHandler<ThumbnailClickedEventArgs>? ThumbnailClicked;
}
```

Maintains a `Dictionary<IntPtr, SidebarOverlay>` keyed by HMONITOR. Raises `ThumbnailClicked` when a user clicks a thumbnail inside any overlay; payload includes `WindowIdentity` and source monitor.

Click detection: `SidebarOverlay` installs a `WndProc` hook. On `WM_LBUTTONUP`, it hit-tests the click against `ThumbnailPlacement.DestinationRect` from the last layout pass, and raises an event. The overlay is `WS_EX_NOACTIVATE`, so the click doesn't steal focus from the active app.

### 7. Interaction handlers

Three interaction paths, all converging on `StageController.SwapAsync(WindowIdentity target, IntPtr monitor)`:

1. **Click a thumbnail.** `StageOverlayHost.ThumbnailClicked` → `StageController.SwapAsync(id, mon)`.
2. **Alt-Tab / taskbar click.** `WinEventHook.Fired(EVENT_SYSTEM_FOREGROUND)` → if the new foreground HWND is in `Parked`, `StageController.SwapAsync(id, mon)`.
3. **Hotkey** (hard-coded `Ctrl+Alt+S` in this plan, configurable in Plan 04) → toggles enable/disable.

`SwapAsync` sequence:

```text
1. Acquire swap semaphore (separate from enable/disable to allow swaps during Enabled).
2. Validate Phase == Enabled; if not, no-op with Warning.
3. Build a SwapPlan:
     outgoing = ActiveHwndByDevice[device]
     incoming = target
     outgoingTo = sidebar slot for outgoing on that monitor
     incomingTo = main area rect for that monitor
4. Update state IMMEDIATELY (logical swap, before any real-window movement):
     ActiveHwndByDevice[device] := incoming
     Parked := Parked.Remove(incoming).Add(outgoing)
5. Release swap semaphore.                              ← release BEFORE animation
6. Call ISwapExecutor.RunAsync(swapPlan, animationSpeed, ct).
   Plan 04 replaces the default ISwapExecutor (InstantSwapExecutor) with the
   animated SwapAnimationController; both share this contract.
   The executor owns its own serialisation queue: if a second Swap arrives
   while an animation is running, the executor coalesces — at most one
   "next" swap is queued; further incoming swaps are dropped with a Debug
   log. The state semaphore is NOT held during the animation.
7. After RunAsync completes:
     Sync overlay on that monitor with the new Parked list (thumbnail for
     outgoing appears in its sidebar slot).
     BringToFront(incoming) — best effort.
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
      "savedWorkArea": { "x": 0, "y": 0, "w": 2560, "h": 1400 }
    }
  ],
  "windows": [
    {
      "identity": {
        "hwnd": 132456,
        "processStartTimeUtcTicks": 638400000000000000
      },
      "originalBounds": { "x": 100, "y": 100, "w": 1200, "h": 800 },
      "originalMonitor": "\\\\.\\DISPLAY1"
    }
  ]
}
```

On app startup:

1. If `snapshot.json` exists AND its `processId` is no longer running as a Stagehand process, there was a crash. Log Warning.
2. Offer via tray balloon: _"Stagehand crashed while managing your windows. Restore previous positions?"_ Two buttons: **Restore** / **Discard**.
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

### S2 — `StageOverlayHost`

**Files**: `app/src/App.Shell/Overlay/StageOverlayHost.cs`, `IStageOverlayHost.cs`. Extends `SidebarOverlay` from Plan 02 with a click `WndProc` hook.

**API**: §Design.6.

**Tests**: `StageOverlayHostTests` — managing creation/disposal lifecycle against a fake overlay factory; click hit-testing math.

**Claude Code prompt**:

> Implement `StageOverlayHost` per Plan 03 §Design.6. The host owns a dictionary of `SidebarOverlay` instances keyed by HMONITOR. `SyncMonitor` delegates to the overlay. Extend `SidebarOverlay` with a click `WndProc` hook that hit-tests against the last layout's destination rects and raises `ThumbnailClicked` on the host.

### S3 — `StageController.EnableAsync` + `DisableAsync` + rollback + `InstantSwapExecutor`

**Files**: `app/src/App.Core/Stage/StageController.cs` (replace stub), `app/src/App.Core/Stage/ISwapExecutor.cs`, `app/src/App.Core/Stage/InstantSwapExecutor.cs`, `app/src/App.Core/Stage/SwapPlan.cs`, `app/src/App.Core/Stage/AnimationSpeed.cs`.

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
    public Task SwapAsync(WindowIdentity target, IntPtr monitor, CancellationToken ct);
}
```

**Dependencies** (via DI): `IWindowEnumerator`, `IWindowController`, `IWorkAreaManager`, `IStageOverlayHost`, `IWinEventHookFactory`, `UiDispatcher`, `IClock`, `ISnapshotStore`, `ILogger<StageController>`.

**Tests**: `StageControllerTests` with fakes for all dependencies. ≥20 cases:

- Happy enable: all steps called in order, final state Enabled.
- Happy disable: rollback in reverse order.
- Enable fails at step 9 (parking): rollback runs; state becomes Disabled; throws `StageTransitionException`.
- Concurrent Enable + Enable: second awaits first.
- `Disable` after failed `Enable`: no-op (already Disabled).
- SwapAsync when Disabled: logs Warning, no-op.
- SwapAsync with target in Parked: swaps successfully.
- `ElevationBoundaryException` during park: skipped, logged, stage still enables.
- Cancellation mid-Enable: rollback runs cleanly.

**Claude Code prompt**:

> Implement `StageController.EnableAsync`, `DisableAsync`, `SwapAsync` per Plan 03 §Design.1–4 and §7. Use a `Stack<Func<Task>>` for rollback. Gate state transitions with a `SemaphoreSlim(1,1)`. Raise `Enabled` / `Disabled` events on successful transitions. Write the 20+ unit tests listed.

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

`Start` subscribes to `host.ThumbnailClicked` and installs the WinEventHook. `Stop` reverses.

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

**Tests**: `SnapshotStoreTests` covering write/read/delete round-trip; stale-detection variants (missing process, recycled PID, same process).

**Claude Code prompt**:

> Implement `SnapshotStore` per Plan 03 §Design.9 + §S6. Unit-test write/read round-trip and the three stale-detection paths. The `ReadIfStale` method must not throw on malformed JSON — log Warning and return null.

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

1. Single monitor, 6 windows. Click **Enable Stage**. Expect: one window fills the screen minus sidebar; other 5 show as thumbnails. Click **Disable Stage**. Expect: all windows back in prior rectangles.
2. Repeat 20 times without degradation.
3. Click a sidebar thumbnail. Expect: swap within 150 ms.
4. Alt-Tab to a parked window. Expect: Stagehand recognises the swap; sidebar updates.
5. Dual monitor. Enable. Expect: two sidebars, independent stages.
6. Drag a parked thumbnail's source window across monitors (by Alt-Tabbing to it then moving). Expect: it re-homes.
7. Unplug monitor B. Expect: its sidebar disappears; its windows appear in A's sidebar.
8. Replug monitor B. Expect: its sidebar reappears.
9. Launch Task Manager elevated. Expect: warning logged, Task Manager appears in the sidebar but cannot be parked; clicking it focuses but does not swap.
10. Force-kill Stagehand while enabled. Restart. Expect: tray balloon "Restore?" prompt; click Restore → windows return to pre-enable positions.
11. Press `Win+Up` on the active window. Expect: maximises only to the main area (not behind sidebar).

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
