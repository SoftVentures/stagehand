# Plan 02 — Core Window Mechanics

> Implements Phase 1 (window enumeration + control) and Phase 2 (sidebar overlay + live DWM thumbnails) from `stage-manager-windows-plan.md`. This is where the core insight — "park windows off-screen, show live DWM thumbnails in a sidebar" — becomes working code. Depends on the scaffolding and architectural rules established in Plan 01.

---

## Context

Plan 01 produced a complete skeleton: interfaces, DI container, logging, a tray icon and an empty Settings window. Every Interop interface is present but its implementation throws `NotImplementedException`.

This plan replaces those stubs with real, production-grade implementations for the parts of the system that talk to the OS. Two concerns are intentionally bundled:

1. **Window enumeration and control** — enumerating visible top-level windows, deciding which count as "manageable apps", reading/writing their positions, bringing them to the foreground, and observing lifecycle changes via `SetWinEventHook`.
2. **Sidebar overlay with live thumbnails** — a transparent, topmost WPF overlay window into which DWM renders live miniatures of the windows Stage Manager is managing.

Bundling them is deliberate. The overlay is useless without windows to show; windows cannot be shown without a correctly filtered enumerator. Validating the two together, end-to-end, against real processes is the only way to know the mechanism actually works — and that validation is the gate for Plan 03.

No enable/disable logic in this plan. No Work Area adjustment. No click handling that _moves_ windows around. Those are Plan 03.

---

## Goal

Deliver a test harness in which, with a single button click, the user sees:

- a transparent sidebar docked to the left edge of the primary monitor,
- live DWM thumbnails of every manageable top-level window on the system stacked vertically inside it,
- the thumbnails continuing to update in real time as the source windows redraw, resize, or close,

while behind the scenes:

- all native resources are wrapped in `SafeHandle` / `IDisposable` with no leaks,
- every filter rule from `stage-manager-windows-plan.md` §"Window Filter" is implemented and unit-tested,
- `WindowController.Park / Restore / Resize / BringToFront` work against real windows.

No StageController yet; Plan 03 composes these pieces into the enable/disable flow.

---

## Acceptance Criteria

1. `tests/App.Harness` launches a WPF window with a **"Show Sidebar"** button that, when clicked, displays the sidebar overlay with one thumbnail per manageable window.
2. Resizing Notepad (or any other real window) resizes its thumbnail in real time without flicker.
3. Closing a source window removes its thumbnail within **500 ms** (driven by `EVENT_OBJECT_DESTROY`).
4. Opening a new window adds a thumbnail within **500 ms** (driven by `EVENT_OBJECT_CREATE`, filtered).
5. Running the harness for 10 minutes shows **no growth in GDI handles or user-object counts** (measured in Task Manager's Details column or via `performance monitor`).
6. `WindowController.Park(hwnd)` moves the target window off-screen and `RestorePosition(hwnd)` returns it to its original bounds. `Resize` and `BringToFront` work against a running Notepad.
7. **All six filter rules** from `stage-manager-windows-plan.md` §"Window Filter" are covered by unit tests that mock `INativeWindowApi`. ≥15 test cases total.
8. `ThumbnailLayoutEngine` is a pure function covered by ≥10 unit tests (single window, many windows, sidebar on left vs. right, tall vs. wide thumbnails, aspect-ratio preservation, empty list, single monitor, two monitors with different DPI).
9. No behavioural regression to Plan 01: CI still green, no new warnings, no new exceptions surfaced to the UI.
10. A manual-test checklist is written to `docs/manual-tests/plan-02.md`.

---

## Scope

### In scope

- `WindowEnumerator` + filter rules.
- `WindowController` (Park, Restore, Resize, BringToFront — including `AttachThreadInput` fallback).
- `WinEventHook` + `WinEventHookThread` (the STA thread Plan 01 stubbed).
- `DwmThumbnail` wrapper + `DwmThumbnailFactory`.
- `ThumbnailLayoutEngine` (pure geometry).
- `SidebarOverlay` WPF window (appearance + positioning; interactions come in Plan 03).
- `INativeWindowApi` seam to keep Core/Services tests away from Win32.
- Expanded Harness exercising all of the above.

### Out of scope

- Enable/disable flow, Work Area, stage state machine → Plan 03.
- Click-to-swap, hover tooltip, foreground hook → Plan 03.
- Multi-monitor overlays (harness uses primary monitor only; overlay is _monitor-aware_ but Plan 03 adds the second overlay instance) → Plan 03.
- Settings-bound configuration of sidebar width, position, blur → Plan 04 (hard-coded defaults here).
- Acrylic / Mica / rounded corners / shadows → Plan 04.
- Swap animation ("cover with a thumbnail" trick) → Plan 04.

---

## Design

### 1. Data model — `WindowSnapshot` (extends Plan 01 stub)

```csharp
public sealed record WindowSnapshot(
    IntPtr Hwnd,
    string Title,
    string ClassName,
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    Rect Bounds,
    IntPtr Monitor)
{
    public WindowIdentity Identity => new(Hwnd, ProcessStartTimeUtcTicks);
}

public readonly record struct WindowIdentity(IntPtr Hwnd, long ProcessStartTimeUtcTicks);
```

`WindowIdentity` is the stable key. HWNDs are recycled by Windows when a window is destroyed; pairing the HWND with the process start time guards against mistaking a new window for an old one. All caches (saved positions, thumbnail registrations) key on `WindowIdentity`, not raw HWND.

### 2. Native seam — `INativeWindowApi`

```csharp
internal interface INativeWindowApi
{
    IReadOnlyList<IntPtr> EnumTopLevel();
    string GetWindowText(IntPtr hwnd);
    string GetClassName(IntPtr hwnd);
    bool IsWindowVisible(IntPtr hwnd);
    bool IsCloaked(IntPtr hwnd);
    IntPtr GetAncestorRoot(IntPtr hwnd);
    long GetWindowStyle(IntPtr hwnd);
    long GetWindowExStyle(IntPtr hwnd);
    IntPtr GetWindowOwner(IntPtr hwnd);
    Rect GetWindowRect(IntPtr hwnd);
    int GetProcessId(IntPtr hwnd);
    long GetProcessStartTimeUtcTicks(int processId);
    IntPtr MonitorFromWindow(IntPtr hwnd);
    bool SetWindowPos(IntPtr hwnd, Rect rect, SetWindowPosFlags flags);
    bool SetForegroundWindow(IntPtr hwnd);
    uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
}
```

`NativeWindowApi` (the only non-test implementation) wraps `NativeMethods.*` and translates failures to `Win32InteropException`. Tests inject a `FakeNativeWindowApi` or an NSubstitute substitute. This seam is **internal** — `InternalsVisibleTo` grants the Tests assembly access.

### 3. `WindowEnumerator` and filter rules

```csharp
public sealed class WindowEnumerator : IWindowEnumerator
{
    private readonly INativeWindowApi _api;
    private readonly IWindowFilter _filter;
    private readonly ILogger<WindowEnumerator> _log;

    public IReadOnlyList<WindowSnapshot> GetManageableWindows()
    {
        var handles = _api.EnumTopLevel();
        var snapshots = new List<WindowSnapshot>(handles.Count);
        foreach (var h in handles)
        {
            var snap = TryBuildSnapshot(h);
            if (snap is not null && _filter.IsManageable(snap))
                snapshots.Add(snap);
        }
        return snapshots;
    }

    private WindowSnapshot? TryBuildSnapshot(IntPtr hwnd) { /* defensive — swallow per-window exceptions, log Debug */ }
}
```

`WindowFilter.IsManageable(WindowSnapshot)` applies, in order, the six rules from `stage-manager-windows-plan.md` §"Window Filter":

1. `IsWindowVisible(hwnd)`.
2. Not cloaked (`DWMWA_CLOAKED` returns 0).
3. `GetAncestor(hwnd, GA_ROOT) == hwnd`.
4. Non-empty title OR (`WS_EX_APPWINDOW` set, or (`WS_EX_TOOLWINDOW` not set AND no owner)).
5. Class-name exclusion list: `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `NotifyIconOverflowWindow`, `Windows.UI.Core.CoreWindow` (with no real app association), `Wallpaper`, `wallpaper_engine`, `WallpaperEngine*`, and — added here — `Stage Manager — Overlay` (our own overlay window class) and `Stage Manager — Settings`.
6. Process-level exclusion list from `ISettingsService.Current.ExcludedApps` (empty in Plan 01; populated by Plan 04). Matching: by process name (case-insensitive) first, then by window class name.

`WindowFilter` takes `ISettingsService` and subscribes to `Changed` to invalidate an internal cache. The enumerator itself is stateless; the filter caches only the exclusion-list lookup.

**Unit-test matrix** (≥15 cases):

- Rule 1: invisible window excluded.
- Rule 2: cloaked UWP window excluded; non-cloaked UWP window included.
- Rule 3: owned dialog excluded.
- Rule 4a: empty title + tool-window excluded.
- Rule 4b: empty title + `WS_EX_APPWINDOW` included.
- Rule 4c: non-empty title + owner=null included.
- Rule 5: each class name in the exclusion list excluded (one test each → 8 cases).
- Rule 6: user-excluded process excluded.
- Combination: a window passing rules 1–4 but failing 5 is excluded.
- Combination: a window passing rules 1–5 but failing 6 is excluded.
- Stable identity: same HWND queried twice returns equal `WindowIdentity`.

### 4. `WindowController`

```csharp
public sealed class WindowController : IWindowController
{
    public void Park(IntPtr hwnd, Rect originalBounds);      // SetWindowPos to (-32000, -32000), same size
    public void RestorePosition(IntPtr hwnd, Rect bounds);   // SetWindowPos back to originalBounds
    public void Resize(IntPtr hwnd, Rect bounds);            // SetWindowPos to the new rect (active window sizing)
    public void BringToFront(IntPtr hwnd);                   // SetForegroundWindow with AttachThreadInput fallback
}
```

Rules:

- All four methods assert they are called on the UI thread (`UiDispatcher.AssertOnUiThread` in Debug).
- `Park` uses flags `SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOREDRAW`. Same for `RestorePosition`. Z-order and focus are left to `BringToFront`.
- `Resize` uses `SWP_NOZORDER | SWP_NOACTIVATE` (no `NOREDRAW`; the app should repaint at the new size).
- `BringToFront` implements the standard `AttachThreadInput` workaround:

  ```text
  current = GetForegroundWindow()
  if current == target: return
  currentThread = GetCurrentThreadId()
  targetThread  = GetWindowThreadProcessId(target, out _)
  if targetThread == currentThread: SetForegroundWindow(target); return
  AttachThreadInput(currentThread, targetThread, true)
  try: SetForegroundWindow(target)
  finally: AttachThreadInput(currentThread, targetThread, false)
  ```

  If `SetForegroundWindow` still fails, raise `Win32InteropException` — Plan 03 catches and logs Warning (the desktop is still consistent; only focus is off).

- If Windows refuses to move an elevated window (`SetWindowPos` returns false, `GetLastError == 5 (ACCESS_DENIED)`), raise `ElevationBoundaryException`. Plan 03's `StageController` catches and skips that window while marking it visually in the overlay (Plan 04 polish).

### 5. `WinEventHook` + `WinEventHookThread`

`SetWinEventHook` requires a thread with a message loop, and callbacks are delivered on that thread. We do **not** install hooks on the UI thread — a poorly-behaved event source could starve it.

`WinEventHookThread` (completed here; Plan 01 shipped a stub):

- Single STA thread with a manual message pump (`GetMessage` loop).
- Started on first hook installation, disposed on app shutdown.
- Exposes `Task InvokeAsync(Action)` so hook wrappers can run Win32 calls on the hook thread when needed (e.g. unhooking on shutdown).

`WinEventHook : IDisposable`:

```csharp
public sealed class WinEventHook : IDisposable
{
    public event EventHandler<WinEventArgs>? Fired; // marshalled to UI thread before invocation
    public WinEventHook(WinEventSpec spec, UiDispatcher ui, WinEventHookThread thread, ILogger<WinEventHook> log);
    public void Dispose();
}

public sealed record WinEventSpec(
    uint EventMin,
    uint EventMax,
    uint Flags = WINEVENT_OUTOFCONTEXT,
    uint IdProcess = 0,          // 0 = all processes; non-zero scopes the hook to one process
    uint IdThread = 0,           // 0 = all threads; non-zero scopes further
    TimeSpan? CoalesceWindow = null); // null = no coalescing; else: drop repeats of same (eventId, hwnd) within window

public sealed record WinEventArgs(uint EventId, IntPtr Hwnd, int ObjectId, int ChildId, uint Thread, uint Time);
```

**Selectivity is a first-class parameter.** `EVENT_OBJECT_LOCATIONCHANGE` fires on every mouse-drag pixel across every window in the session — thousands per second on a modest desktop. Installing a global hook for it starves the hook thread. `WinEventSpec` therefore exposes `IdProcess` and `IdThread` so callers can scope hooks to a specific process when they only care about one window's moves, and a `CoalesceWindow` so high-frequency events (location-change, foreground-thrash) can be deduplicated at the hook layer without every caller rolling its own debounce. Plan 03 uses this: it installs a narrow `EVENT_OBJECT_LOCATIONCHANGE` hook per-parked-HWND (via `IdProcess`) rather than one global hook. See Plan 03 §Design.10 for the matrix of hook specs the StageController installs.

**Marshalling rule.** The raw callback stores the event in an in-memory ring buffer keyed by `(EventId, Hwnd)`; if `CoalesceWindow` is set and a matching entry was posted less than `CoalesceWindow` ago, the new event is dropped. Otherwise the entry's timestamp is refreshed and a UI-thread continuation (`Dispatcher.InvokeAsync`) is posted to drain it. Bursts during Explorer restart are absorbed.

**Coalescing scope is per hook instance.** Two independently-installed `WinEventHook` instances that happen to overlap on event range (e.g. Plan 03's `StageController` and Plan 05's `FullscreenMonitor` both subscribing to `EVENT_SYSTEM_FOREGROUND`) have **independent** ring buffers and independent coalescing. The hook layer never globally dedupes across subscribers — doing so would let one handler silently consume events meant for another.

**Resilience: unhook after thread shutdown.** `Dispose` posts an `UnhookWinEvent` call to `WinEventHookThread.InvokeAsync`. If the hook thread has already been stopped (app shutdown race), the `InvokeAsync` call fails; `Dispose` catches `ObjectDisposedException`/`InvalidOperationException`, logs at `Warning`, and returns without rethrowing. OS-level cleanup happens on process exit regardless.

**Explorer restart.** We register for `TaskbarCreated` (a broadcast message) on the hook thread and re-install all currently-active hooks on receipt.

### 6. `DwmThumbnail` wrapper and factory

```csharp
public sealed class DwmThumbnail : IDisposable
{
    public IntPtr Source { get; }
    public IntPtr Destination { get; }
    public Size SourceSize { get; }
    public void UpdateDestinationRect(Rect destRect, byte opacity = 255);
    public void SetSourceCrop(Rect? cropInSourcePixels);
    public void Dispose();
}

public interface IDwmThumbnailFactory
{
    DwmThumbnail Register(IntPtr sourceHwnd, IntPtr destinationHwnd);
}
```

Implementation notes:

- Calls `DwmRegisterThumbnail` to get the thumbnail handle (`HTHUMBNAIL`). Wrapped by `DwmThumbnailSafeHandle` from Plan 01.
- `UpdateDestinationRect` builds a `DWM_THUMBNAIL_PROPERTIES` with `dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY` (`true`) and calls `DwmUpdateThumbnailProperties`.
- On the first update, query `DwmQueryThumbnailSourceSize(handle, out SIZE size)` to fill `SourceSize` — used by the layout engine for aspect ratio.
- Disposing calls `DwmUnregisterThumbnail` on the safe handle.
- All methods assert UI-thread affinity.

Destination HWND: the sidebar overlay. DWM draws the thumbnail directly into the overlay's window surface; we do **not** render thumbnail pixels in WPF.

### 7. `ThumbnailLayoutEngine`

Pure, no state, fully testable.

```csharp
public sealed class ThumbnailLayoutEngine : IThumbnailLayoutEngine
{
    public IReadOnlyList<ThumbnailPlacement> Compute(LayoutRequest request);
}

public sealed record LayoutRequest(
    Rect SidebarBounds,             // destination area inside the overlay
    double ThumbnailSpacing,        // px between stacked thumbnails
    double OuterPadding,            // px between overlay edges and thumbnails
    double MaxThumbnailHeight,      // cap for any single thumbnail
    IReadOnlyList<(WindowIdentity Identity, Size SourceSize)> Windows);

public sealed record ThumbnailPlacement(WindowIdentity Identity, Rect DestinationRect);
```

Algorithm:

1. Usable height `H = SidebarBounds.Height - 2*OuterPadding - (N-1)*ThumbnailSpacing` where `N = Windows.Count`.
2. Provisional per-thumbnail height `h = min(MaxThumbnailHeight, H / N)`.
3. For each source, compute thumbnail width `w = h * (sourceWidth / sourceHeight)`; cap `w` at `SidebarBounds.Width - 2*OuterPadding` and re-derive height if capped.
4. Centre each thumbnail horizontally within the sidebar.
5. Stack vertically with `ThumbnailSpacing`; centre the stack vertically if total stack height < usable height.
6. Empty `Windows` → return empty list.

Unit tests (≥10): enumerated in §Acceptance Criteria and in the Test Matrix below.

### 8. `SidebarOverlay`

One WPF window per managed monitor. Plan 03 creates multiple; this plan creates one (primary monitor).

```csharp
public sealed partial class SidebarOverlay : Window
{
    public SidebarOverlay(IDwmThumbnailFactory thumbnails, IThumbnailLayoutEngine layout, ILogger<SidebarOverlay> log);
    public SidebarEdge Edge { get; set; }                 // Left | Right — Plan 04 makes configurable
    public double SidebarWidth { get; set; } = 200.0;     // default, Plan 04 configurable
    public void ShowOn(IntPtr monitor);
    public void HideAndRelease();
    public void Sync(IReadOnlyList<WindowSnapshot> windows); // registers/updates/unregisters thumbnails
}
```

XAML:

```xml
<Window x:Class="App.Shell.Overlay.SidebarOverlay"
        WindowStyle="None"
        AllowsTransparency="True"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        Background="#00000000">
    <!-- intentionally empty Content — DWM paints directly into the window surface -->
</Window>
```

Window styles applied in code-behind after `SourceInitialized`:

- `WS_EX_NOACTIVATE` (prevent clicks from stealing focus — Plan 03 wires click handling via hit-test without activation).
- `WS_EX_TOOLWINDOW` (keep out of Alt+Tab).
- Per-monitor DPI awareness honoured via `PerMonitorDpiHelpers`.

`ShowOn(monitor)`:

- Resolve the `OverlayMonitor` record for the HMONITOR (via the `MonitorEnumerator` Plan 03 introduces; until then, Plan 02 uses an inline helper that calls `GetMonitorInfoW` and builds an ad-hoc `OverlayMonitor`).
- Position window flush to `Edge` (Left by default) with configured `SidebarWidth`, full monitor height (in device-independent units — WPF handles the DPI scale via `OverlayMonitor.DpiScale`).
- Call `Show()`.

`Sync(windows)` (called after every window-event burst on the UI thread):

1. Diff incoming window list against a private `Dictionary<WindowIdentity, DwmThumbnail>`.
2. For removed: `Dispose()` and remove.
3. For added: `IDwmThumbnailFactory.Register(source, this.Handle)` and add.
4. Compute new `LayoutRequest` and call `ThumbnailLayoutEngine.Compute`.
5. For each placement, call `DwmThumbnail.UpdateDestinationRect(placement.DestinationRect)`.

Perf note: a full `Sync` runs in <1 ms for up to 50 windows; empirically measured during the acceptance 10-minute run.

### 9. Harness expansion

`tests/App.Harness/MainWindow.xaml`:

- A `ListBox` showing the current `WindowEnumerator.GetManageableWindows()` result (identity, title, class, bounds).
- A **Refresh** button that re-runs the enumerator.
- A **Show Sidebar** button that toggles a `SidebarOverlay`.
- A **Park** / **Restore** button pair that operate on the ListBox-selected window (round-trip sanity check for `WindowController`).
- A **Register hook** button that installs a `WinEventHook` for `EVENT_OBJECT_CREATE | EVENT_OBJECT_DESTROY | EVENT_SYSTEM_FOREGROUND` and logs each event to a textbox. (This proves the hook thread + marshalling works.)
- A live **GDI / User-object** counter (read via `GetGuiResources` on the current process) for the 10-minute leak test.

The harness project is WPF but **not** shipped; `<IsPackable>false`, excluded from the release workflow.

### 10. UI-thread affinity & dispose order

- `TrayIconHost` dispose order extended: the overlay (when Plan 03 creates it) is disposed before `WinEventHookThread`.
- `SidebarOverlay.HideAndRelease()` disposes thumbnails first, then `Close()`s the window.
- `DwmThumbnail.Dispose` is idempotent; double-dispose is logged at Trace.

---

## Subtasks

### S1 — Native seam and `INativeWindowApi`

**Files**: `app/src/App.Interop/Internal/INativeWindowApi.cs`, `NativeWindowApi.cs`, `SetWindowPosFlags.cs`. Add `[assembly: InternalsVisibleTo("App.Tests")]` to the Interop project.

**API**: §Design.2.

**Tests**: `NativeWindowApiSmokeTests` — one test per method that invokes it against the harness's own HWND (obtained from `Process.GetCurrentProcess().MainWindowHandle` once the harness is set up). These are integration tests gated on `[Trait("Category","Interop")]` and run manually, not in CI.

**Claude Code prompt**:

> Create `INativeWindowApi` and `NativeWindowApi` per Plan 02 §S1 using the P/Invoke signatures already present in `NativeMethods.*.cs` from Plan 01. Wrap every native failure as `Win32InteropException(apiName, Marshal.GetLastWin32Error())`. Add `InternalsVisibleTo` for the Tests assembly.

### S2 — `WindowFilter`

**Files**: `app/src/App.Core/Windows/WindowFilter.cs`, `app/src/App.Core/Windows/WindowClassExclusions.cs` (compile-time list of class names).

**API**:

```csharp
public sealed class WindowFilter : IWindowFilter
{
    public WindowFilter(ISettingsService settings, ILogger<WindowFilter> log);
    public bool IsManageable(WindowSnapshot w);
}
```

**Tests**: `tests/App.Tests/Windows/WindowFilterTests.cs` covering all rules (≥15 cases; see §Design.3).

**Claude Code prompt**:

> Implement `WindowFilter` per Plan 02 §Design.3. `WindowClassExclusions` is an `ImmutableHashSet<string>` keyed case-insensitive. Write all ≥15 unit tests listed in Plan 02 §Design.3, using NSubstitute for `ISettingsService`. The filter must NOT take `INativeWindowApi`; it operates on `WindowSnapshot` only.

### S3 — `WindowEnumerator`

**Files**: `app/src/App.Interop/WindowEnumerator.cs`.

**API**: §Design.3.

**Details**: Replace the `NotImplementedWindowEnumerator` stub from Plan 01. Update the DI registration in `ServiceConfiguration` to use the real class.

**Tests**: `WindowEnumeratorTests` with a `FakeNativeWindowApi` that returns a scripted list of HWNDs and metadata. Tests:

- Enumerator returns every window that the filter accepts.
- Per-window exception swallowed (e.g. window closed mid-enumeration): enumerator logs Debug and continues.
- Stable identity across two calls when the same HWND is still alive.

**Claude Code prompt**:

> Implement `WindowEnumerator` per Plan 02 §S3. Replace Plan 01's `NotImplementedWindowEnumerator` registration. The enumerator owns the "build snapshot from raw HWND" logic; `WindowFilter` only accepts the built snapshot.

### S4 — `WindowController`

**Files**: `app/src/App.Interop/WindowController.cs`.

**API**: §Design.4.

**Tests**: `WindowControllerTests` with `INativeWindowApi` substitute. Tests:

- `Park` calls `SetWindowPos` with flags `NOZORDER | NOACTIVATE | NOREDRAW` and rect origin `(-32000, -32000)`.
- `RestorePosition` uses the passed rect; does not query saved state (that's `StageController`'s job).
- `Resize` uses `NOZORDER | NOACTIVATE`.
- `BringToFront` happy path (same thread), `AttachThreadInput` path (different thread), failure path throws `Win32InteropException`.
- `Park` on elevated window (`SetWindowPos` returns false, `GetLastError == 5`) throws `ElevationBoundaryException`.

**Claude Code prompt**:

> Implement `WindowController` per Plan 02 §Design.4. Include the `AttachThreadInput` foreground fallback. Add the five test cases listed.

### S5 — `WinEventHookThread` and `WinEventHook`

**Files**: `app/src/App.Shell/Threading/WinEventHookThread.cs` (replace stub), `app/src/App.Interop/WinEventHook.cs`, `app/src/App.Interop/IWinEventHookFactory.cs`, `WinEventHookFactory.cs`.

**API**: §Design.5.

**Tests**:

- `WinEventHookThreadTests`: thread starts on demand, runs a message loop, `InvokeAsync` round-trips, shuts down cleanly, post-shutdown `InvokeAsync` throws the expected exception type (consumed by `WinEventHook.Dispose`).
- `WinEventHookTests`:
  - Fires `Fired` on the UI thread (asserted via `UiDispatcher.AssertOnUiThread`).
  - Coalescing: bursts of the same `(EventId, Hwnd)` within `CoalesceWindow` are dropped; events with different `Hwnd` or `EventId` are NOT coalesced.
  - Coalescing independence: two `WinEventHook` instances on the same event range each receive every event (no cross-instance dedupe).
  - Selectivity: when `IdProcess` is set, events from other processes are never seen (validated by scripting the callback dispatcher).
  - Re-installs on Explorer restart (simulated by firing `TaskbarCreated`).
  - Dispose after thread shutdown: logs `Warning`, does NOT throw.

**Claude Code prompt**:

> Implement `WinEventHookThread` (a manual message-pump STA thread) and `WinEventHook` per Plan 02 §Design.5. The hook callback must never touch UI state directly; marshal via `UiDispatcher.Post`. Implement the coalescing ring buffer PER INSTANCE. Honor `IdProcess` and `IdThread` by passing them to `SetWinEventHook`. `Dispose` must be safe to call after `WinEventHookThread` has stopped — catch the expected exception and log Warning. Write the seven test scenarios listed.

### S6 — `DwmThumbnail` + factory

**Files**: `app/src/App.Interop/DwmThumbnail.cs`, `DwmThumbnailFactory.cs`.

**API**: §Design.6.

**Tests**: the wrapper itself is too thin to unit-test meaningfully; coverage comes from the harness. However, add `DwmThumbnailLifecycleTests` that register + dispose 1000 times with a fake `NativeDwmApi` seam and verify exactly one `Register` / `Unregister` pair per instance.

**Claude Code prompt**:

> Implement `DwmThumbnail` and `DwmThumbnailFactory` per Plan 02 §Design.6. Introduce an internal `INativeDwmApi` seam mirroring the `INativeWindowApi` pattern so the factory is testable without real DWM. Write the lifecycle test described.

### S7 — `ThumbnailLayoutEngine`

**Files**: `app/src/App.Core/Layout/ThumbnailLayoutEngine.cs`, `IThumbnailLayoutEngine.cs`, `LayoutRequest.cs`, `ThumbnailPlacement.cs`.

**API**: §Design.7.

**Tests** (≥10): empty list; single window matching sidebar aspect ratio; single window wider-than-sidebar (height-capped); single window taller-than-sidebar (width-capped); 5 equal windows stacked; 20 windows (height-limited); spacing and padding honoured; left vs. right edge identical layout (since sidebar width is sidebar-local); `MaxThumbnailHeight` cap; verify centring when stack shorter than sidebar.

**Claude Code prompt**:

> Implement `ThumbnailLayoutEngine` per Plan 02 §Design.7 as a pure function. Cover with the ≥10 unit tests listed in §Acceptance Criteria and §Design.7.

### S8 — `SidebarOverlay`

**Files**: `app/src/App.Shell/Overlay/SidebarOverlay.xaml{.cs}`, `app/src/App.Shell/Overlay/PerMonitorDpiHelpers.cs`, `app/src/App.Shell/Overlay/OverlayMonitor.cs`.

**API**: §Design.8.

**Details**:

- Apply `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` after `SourceInitialized`.
- Use `HwndSource.FromHwnd(this).AddHook(WndProc)` to react to `WM_DPICHANGED`.
- Monitor positioning uses `EnumDisplayMonitors` via an extension on the native seam.
- Background colour `#00000000`; no visible chrome. A 1px left-edge accent line is drawn with a WPF `Border` for debuggability — removed in Plan 04 polish.

**Tests**: none (integration via harness).

**Claude Code prompt**:

> Create `SidebarOverlay` per Plan 02 §Design.8. Register/update/unregister thumbnails in `Sync`. Use `ThumbnailLayoutEngine` for geometry. Apply the extended window styles after `SourceInitialized`. Handle `WM_DPICHANGED` by recomputing layout.

### S9 — Harness expansion

**Files**: `tests/App.Harness/MainWindow.xaml{.cs}`, `Harness/WindowListViewModel.cs`.

**API**: N/A (internal).

**Details**: §Design.9.

**Claude Code prompt**:

> Expand the Harness per Plan 02 §Design.9. Wire buttons to the real enumerator, controller, hook, and overlay. Add the live GDI/user-object counter using `NativeMethods.User32.GetGuiResources`.

### S10 — Manual test checklist

**Files**: `docs/manual-tests/plan-02.md`.

**Details**:

1. Launch Harness. Click **Refresh**. Expect: a list of visible top-level windows matching what's on screen; no desktop/taskbar entries.
2. Click **Show Sidebar**. Expect: transparent strip appears on the left edge; live thumbnails fill it; wallpaper still visible behind.
3. Resize a source window. Expect: the thumbnail resizes in real time.
4. Open a new window (e.g. Calculator). Expect: a new thumbnail appears within 500 ms.
5. Close a window. Expect: its thumbnail disappears within 500 ms.
6. Click a ListBox entry, click **Park**. Expect: the window vanishes from the screen but its thumbnail is still live. Click **Restore**. Expect: the window returns to its prior rectangle.
7. Click **Register hook**. Alt-tab between apps. Expect: `EVENT_SYSTEM_FOREGROUND` events log to the textbox.
8. Let the Harness run 10 minutes with the sidebar visible. Expect: GDI and user-object counters stable (±5).
9. Launch an elevated app. Task Manager on modern Windows actually starts non-elevated unless UAC is triggered, so use `regedit` launched via right-click → **Run as administrator** (or any other app known to require admin). Try **Park** on it. Expect: `ElevationBoundaryException` logged; no crash.

**Claude Code prompt**:

> Author `docs/manual-tests/plan-02.md` per the checklist in Plan 02 §S10.

---

## Risks & Mitigations

| Risk                                                                                   | Impact                   | Mitigation                                                                                                                                          |
| -------------------------------------------------------------------------------------- | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| DWM thumbnail updates flicker when source window resizes                               | Poor UX                  | Always pass `DWM_TNP_VISIBLE` and update rect in a single `DwmUpdateThumbnailProperties` call (not two). Never unregister-and-reregister on change. |
| Hook callback on hook thread deadlocks with UI thread                                  | App hang                 | Hook callback never blocks; always `Post` (not `Invoke`) to UI dispatcher.                                                                          |
| Explorer restart drops our hooks                                                       | Events stop firing       | Listen for `TaskbarCreated` and re-install.                                                                                                         |
| HWND reuse after a process dies + restarts                                             | Wrong thumbnail shown    | Key caches on `WindowIdentity` (HWND + process start time), not HWND. Drop cache entries whose identity changed.                                    |
| Thumbnails leak GDI objects on rapid open/close                                        | Resource exhaustion      | `SafeHandle` ensures `DwmUnregisterThumbnail`; 10-minute soak test in the checklist.                                                                |
| Windows API returns success but with zero-sized source (`DwmQueryThumbnailSourceSize`) | Divide-by-zero in layout | `DwmThumbnail.SourceSize` clamps to min 1x1; layout engine treats zero-size windows as 1:1 placeholders.                                            |
| Sidebar overlay steals focus on show                                                   | User types into sidebar  | `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` applied post-`SourceInitialized`; asserted in manual checklist step 2.                                      |
| `AttachThreadInput` fallback causes focus ping-pong with stubborn apps                 | Jittery foreground       | `BringToFront` is best-effort. Plan 03's StageController logs warnings but does not retry in a loop.                                                |
| Wallpaper Engine window classes vary across versions                                   | Wrong filter             | Match on a case-insensitive `StartsWith("WallpaperEngine")` rather than exact equality; document in code comment.                                   |
| Per-monitor DPI changes while sidebar visible                                          | Wrong thumbnail sizes    | Handle `WM_DPICHANGED` in the overlay; re-invoke `Sync`.                                                                                            |

---

## Verification

1. **Unit tests green.** `WindowFilterTests`, `WindowEnumeratorTests`, `WindowControllerTests`, `WinEventHookThreadTests`, `WinEventHookTests`, `DwmThumbnailLifecycleTests`, `ThumbnailLayoutEngineTests` — all pass. ≥40 test cases across these classes.
2. **CI green.** No new warnings. `dotnet format` green. Coverage on `App.Core` ≥ the Plan 01 baseline.
3. **Manual: harness checklist** at `docs/manual-tests/plan-02.md` — all nine steps verified on a real machine, results pasted into the PR description.
4. **Soak test**: 10-minute harness run with sidebar visible shows no handle growth.
5. **Elevation boundary**: parking an elevated app raises `ElevationBoundaryException` and leaves the system in a consistent state.

---

## References

- `stage-manager-windows-plan.md` — §"How It Actually Works", §"Window Filter", §"Core Windows APIs", Phase 1, Phase 2.
- Plan 01 §Design — threading, error handling, DI.
- `EnumWindows` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows>
- `SetWindowPos` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos>
- `SetForegroundWindow` restrictions — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow>
- `AttachThreadInput` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-attachthreadinput>
- `SetWinEventHook` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook>
- `DwmRegisterThumbnail` — <https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail>
- `DwmUpdateThumbnailProperties` — <https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmupdatethumbnailproperties>
- `DwmGetWindowAttribute` (DWMWA_CLOAKED) — <https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute>
- `GetGuiResources` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguiresources>
- Explorer restart / `TaskbarCreated` — <https://learn.microsoft.com/windows/win32/shell/taskbar#taskbar-creation-notification>
