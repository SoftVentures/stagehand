using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Time;
using App.Interop;
using App.Interop.Errors;
using Microsoft.Extensions.Logging;

namespace App.Core.Stage;

/// <summary>
/// Production <see cref="IStageController"/>. Owns the authoritative
/// <see cref="StageState"/>, drives the <see cref="StagePhase"/> state
/// machine with full transactional rollback on failure, and brokers all
/// public mutation paths (enable / disable / swap / move-window).
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency model.</b> Two semaphores: <c>_stateGate</c> serialises
/// <see cref="EnableAsync"/> / <see cref="DisableAsync"/> /
/// <see cref="MoveWindowToSceneAsync"/>. <c>_swapGate</c> serialises
/// <see cref="SwapAsync"/> state-mutation; the executor runs without
/// either gate (Plan 03 §Design.7).
/// </para>
/// <para>
/// <b>State direction.</b> Only this class produces new <see cref="StageState"/>
/// snapshots; subscribers (<c>StageInteractionCoordinator</c>, harness)
/// read via <see cref="CurrentState"/> or the <see cref="StateChanged"/>
/// event.
/// </para>
/// </remarks>
public sealed class StageController : IStageController, IDisposable
{
    /// <summary>
    /// Sidebar reservation in physical pixels — left edge of every monitor.
    /// Plan 04 promotes this to a setting; Plan 03 hard-codes 200 px.
    /// </summary>
    internal const int SidebarReservationPx = 200;

    private static readonly Action<ILogger, Exception?> s_logEnableStart = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(7001, nameof(StageController) + ".EnableStart"),
        "StageController.EnableAsync starting."
    );

    private static readonly Action<ILogger, int, int, Exception?> s_logEnableComplete =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(7002, nameof(StageController) + ".EnableComplete"),
            "StageController.EnableAsync complete: {SceneCount} scenes across {MonitorCount} monitors."
        );

    private static readonly Action<ILogger, Exception?> s_logDisableStart = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(7003, nameof(StageController) + ".DisableStart"),
        "StageController.DisableAsync starting."
    );

    private static readonly Action<ILogger, Exception?> s_logDisableComplete = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(7004, nameof(StageController) + ".DisableComplete"),
        "StageController.DisableAsync complete."
    );

    private static readonly Action<ILogger, Exception?> s_logEnableFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(7005, nameof(StageController) + ".EnableFailed"),
        "StageController.EnableAsync failed; running rollback."
    );

    private static readonly Action<ILogger, Exception?> s_logRollbackStep = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(7006, nameof(StageController) + ".RollbackStep"),
        "Rollback step threw; continuing."
    );

    private static readonly Action<ILogger, Exception?> s_logDisableStep = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(7007, nameof(StageController) + ".DisableStep"),
        "DisableAsync step threw; continuing best-effort restore."
    );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logElevatedSkip =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Information,
            new EventId(7008, nameof(StageController) + ".ElevatedSkip"),
            "StageController: HWND 0x{Hwnd:X} runs at a higher integrity level; left in place, marked IsElevated."
        );

    private static readonly Action<ILogger, Exception?> s_logSwapInactive = LoggerMessage.Define(
        LogLevel.Debug,
        new EventId(7009, nameof(StageController) + ".SwapInactive"),
        "StageController.SwapAsync ignored: controller is not in Enabled phase."
    );

    private static readonly Action<ILogger, string, Exception?> s_logSwapTargetMissing =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(7010, nameof(StageController) + ".SwapTargetMissing"),
            "StageController.SwapAsync: target scene not found on device '{Device}'; ignored."
        );

    private static readonly Action<ILogger, string, string, Exception?> s_logMoveCrossMonitor =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(7011, nameof(StageController) + ".MoveCrossMonitor"),
            "MoveWindowToSceneAsync: target scene lives on '{Dest}' but window is on '{Source}'; falling back to a new scene on the source monitor."
        );

    private static readonly Action<ILogger, Exception?> s_logSubscriberFault = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(7012, nameof(StageController) + ".SubscriberFault"),
        "A StageController.StateChanged subscriber threw; continuing."
    );

    private readonly IWindowEnumerator _enumerator;
    private readonly IWindowFilter _filter;
    private readonly IWindowController _windows;
    private readonly IWorkAreaManager _workArea;
    private readonly IStageOverlayHost _overlay;
    private readonly ISceneGrouper _grouper;
    private readonly ISceneSwapExecutor _executor;
    private readonly ISnapshotStore _snapshot;
    private readonly MonitorEnumerator _monitors;
    private readonly IClock _clock;
    private readonly ILogger<StageController> _log;

    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _swapGate = new(1, 1);

    private StageState _state = StageState.Empty;

    // Pending StateChanged event payloads queued from inside the state /
    // swap semaphore. Drained by RaisePendingEvents() AFTER the semaphore
    // is released, so a subscriber that calls back into EnableAsync /
    // DisableAsync / SwapAsync can re-acquire the semaphore without
    // deadlocking. Plan-03 §Design.7 spelled out this discipline only for
    // the swap path; we apply it uniformly.
    private readonly Queue<StageState> _pendingNotifications = new();

    /// <summary>Production constructor.</summary>
    public StageController(
        IWindowEnumerator enumerator,
        IWindowFilter filter,
        IWindowController windows,
        IWorkAreaManager workArea,
        IStageOverlayHost overlay,
        ISceneGrouper grouper,
        ISceneSwapExecutor executor,
        ISnapshotStore snapshot,
        MonitorEnumerator monitors,
        IClock clock,
        ILogger<StageController> log
    )
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _workArea = workArea ?? throw new ArgumentNullException(nameof(workArea));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _grouper = grouper ?? throw new ArgumentNullException(nameof(grouper));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Releases the internal semaphores. Does NOT run rollback —
    /// if the controller is disposed while <see cref="StagePhase.Enabled"/>
    /// (e.g. an unhandled dispatcher exception terminates the app), real
    /// windows stay parked off-screen and the snapshot remains on disk.
    /// The next launch's <c>CrashRecoveryCoordinator</c> picks the snapshot
    /// up and restores cleanly. A best-effort restore in Dispose was
    /// considered and rejected: it would race with finalisation, hide
    /// real failures, and duplicate the well-tested recovery path.
    /// </summary>
    public void Dispose()
    {
        _stateGate.Dispose();
        _swapGate.Dispose();
    }

    /// <inheritdoc />
    public StageState CurrentState => _state;

    /// <inheritdoc />
    public bool IsEnabled => _state.Phase == StagePhase.Enabled;

    /// <inheritdoc />
    public event EventHandler<StageState>? StateChanged;

    /// <inheritdoc />
    public async Task EnableAsync(CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.Phase != StagePhase.Disabled)
            {
                return; // idempotent
            }

            s_logEnableStart(_log, null);
            SetState(_state with { Phase = StagePhase.Enabling });

            var undo = new Stack<Func<Task>>();
            try
            {
                StageState newState = await EnableCoreAsync(undo, ct).ConfigureAwait(false);
                SetState(newState with { Phase = StagePhase.Enabled });
                s_logEnableComplete(
                    _log,
                    CountAllScenes(newState),
                    newState.ScenesByDevice.Count,
                    null
                );
            }
            catch (Exception ex)
            {
                s_logEnableFailed(_log, ex);
                await DrainRollbackAsync(undo).ConfigureAwait(false);
                SetState(StageState.Empty);
                throw new StageTransitionException(StagePhase.Disabled, StagePhase.Enabled, ex);
            }
        }
        finally
        {
            _stateGate.Release();
        }
        // Subscribers may legitimately call back into the controller; raise
        // the queued StateChanged events AFTER releasing the gate.
        RaisePendingEvents();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Plan §Design.3 step 3 (uninstall display-change listener) and step 4
    /// (uninstall WinEventHook) are intentionally NOT performed here. Both
    /// hooks live in DI singletons in the Shell layer
    /// (<c>StageInteractionCoordinator</c> owns the foreground hook;
    /// <c>App.xaml.cs</c> hosts the <c>DisplayChangeListener</c>) and
    /// outlive enable/disable cycles. Their handlers are no-ops while
    /// <see cref="StagePhase.Disabled"/> because <see cref="SwapAsync"/> /
    /// <see cref="MonitorChangeCoordinator.Reconcile"/> short-circuit on
    /// <see cref="IsEnabled"/>. App shutdown disposes them.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "DisableAsync is the recovery path — it must not propagate failures, only log them and proceed best-effort."
    )]
    public async Task DisableAsync(CancellationToken ct)
    {
        StageState? snapshotForBringToFront = null;
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.Phase != StagePhase.Enabled)
            {
                return; // idempotent
            }

            // Capture the active-scene mapping BEFORE clearing state so we
            // can BringToFront each monitor's primary after restore (Plan
            // §Design.3 step 8).
            snapshotForBringToFront = _state;

            s_logDisableStart(_log, null);
            SetState(_state with { Phase = StagePhase.Disabling });

            // Step 5: dispose overlays (UI thread; the overlay implementation
            // marshals if needed).
            try
            {
                _overlay.DisposeAll();
            }
            catch (Exception ex)
            {
                s_logDisableStep(_log, ex);
            }

            // Step 6: restore every window in every scene.
            foreach (KeyValuePair<string, ImmutableList<Scene>> pair in _state.ScenesByDevice)
            {
                foreach (Scene scene in pair.Value)
                {
                    // Restore non-primary first, primary last so the scene's
                    // primary ends on top within its local Z-order.
                    var ordered = new List<ParkedWindow>(scene.Windows.Count);
                    foreach (ParkedWindow pw in scene.Windows)
                    {
                        if (pw.Identity == scene.Primary)
                        {
                            continue;
                        }
                        ordered.Add(pw);
                    }
                    foreach (ParkedWindow pw in scene.Windows)
                    {
                        if (pw.Identity == scene.Primary)
                        {
                            ordered.Add(pw);
                        }
                    }

                    foreach (ParkedWindow pw in ordered)
                    {
                        if (pw.IsElevated)
                        {
                            continue; // never moved it; nothing to restore
                        }
                        try
                        {
                            _windows.RestorePosition(pw.Identity.Hwnd, pw.OriginalBounds);
                        }
                        catch (Exception ex)
                        {
                            s_logDisableStep(_log, ex);
                        }
                    }
                }
            }

            // Step 7: restore work areas.
            try
            {
                _workArea.RestoreAll();
            }
            catch (Exception ex)
            {
                s_logDisableStep(_log, ex);
            }

            // Step 8: bring each monitor's pre-disable active-scene primary
            // back to the foreground so the user lands on the same window
            // they had focused before Stagehand was enabled. Best-effort —
            // the focus-stealing guard or process death is not fatal here.
            if (snapshotForBringToFront is { } pre)
            {
                foreach (KeyValuePair<string, SceneId?> pair in pre.ActiveSceneByDevice)
                {
                    if (pair.Value is not { } activeId)
                    {
                        continue;
                    }
                    if (!pre.ScenesByDevice.TryGetValue(pair.Key, out ImmutableList<Scene>? scenes))
                    {
                        continue;
                    }
                    Scene? activeScene = null;
                    foreach (Scene s in scenes)
                    {
                        if (s.Id == activeId)
                        {
                            activeScene = s;
                            break;
                        }
                    }
                    if (activeScene is null)
                    {
                        continue;
                    }
                    try
                    {
                        _windows.BringToFront(activeScene.Primary.Hwnd);
                    }
                    catch (Exception ex)
                    {
                        s_logDisableStep(_log, ex);
                    }
                }
            }

            // Step 9: delete snapshot.
            try
            {
                await _snapshot.DeleteAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                s_logDisableStep(_log, ex);
            }

            SetState(StageState.Empty);
            s_logDisableComplete(_log, null);
        }
        finally
        {
            _stateGate.Release();
        }
        RaisePendingEvents();
    }

    /// <inheritdoc />
    public async Task SwapAsync(SceneId target, string deviceName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceName);

        SceneSwapPlan? plan;
        await _swapGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.Phase != StagePhase.Enabled)
            {
                s_logSwapInactive(_log, null);
                return;
            }

            if (
                !_state.ScenesByDevice.TryGetValue(deviceName, out ImmutableList<Scene>? scenes)
                || scenes.All(s => s.Id != target)
            )
            {
                s_logSwapTargetMissing(_log, deviceName, null);
                return;
            }

            SceneId? currentActive = _state.ActiveSceneByDevice.TryGetValue(
                deviceName,
                out SceneId? a
            )
                ? a
                : null;

            if (currentActive is { } cur && cur == target)
            {
                return; // already active
            }

            Rect activeWorkArea = _state.SavedWorkAreasByDevice.TryGetValue(
                deviceName,
                out SavedWorkArea saved
            )
                ? ShrinkLeft(saved.OriginalWorkArea, SidebarReservationPx)
                : new Rect(int.MinValue / 2, int.MinValue / 2, int.MaxValue, int.MaxValue);
            plan = BuildSwapPlan(target, currentActive, deviceName, scenes, activeWorkArea);

            // Atomic logical swap: ActiveSceneByDevice updated immediately.
            ImmutableDictionary<string, SceneId?> updatedActive =
                _state.ActiveSceneByDevice.SetItem(deviceName, target);
            SetState(_state with { ActiveSceneByDevice = updatedActive });
        }
        finally
        {
            _swapGate.Release();
        }
        RaisePendingEvents();

        // Animation/move runs without the swap semaphore (Plan §Design.7).
        await _executor.RunAsync(plan, AnimationSpeed.Off, ct).ConfigureAwait(false);

        // Re-sync the affected monitor's overlay with the new parked-scene set.
        ResyncOverlayForDevice(deviceName);
    }

    /// <inheritdoc />
    public Task SwapByWindowAsync(WindowIdentity foregroundWindow, CancellationToken ct)
    {
        // Resolve scene + device by walking ScenesByDevice.
        foreach (KeyValuePair<string, ImmutableList<Scene>> pair in _state.ScenesByDevice)
        {
            foreach (Scene scene in pair.Value)
            {
                foreach (ParkedWindow pw in scene.Windows)
                {
                    if (pw.Identity == foregroundWindow)
                    {
                        return SwapAsync(scene.Id, pair.Key, ct);
                    }
                }
            }
        }
        return Task.CompletedTask; // not in any parked scene; no-op
    }

    /// <inheritdoc />
    public async Task MoveWindowToSceneAsync(
        WindowIdentity window,
        SceneId? targetScene,
        CancellationToken ct
    )
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Find the window's current scene.
            string? sourceDevice = null;
            Scene? sourceScene = null;
            foreach (KeyValuePair<string, ImmutableList<Scene>> pair in _state.ScenesByDevice)
            {
                foreach (Scene s in pair.Value)
                {
                    foreach (ParkedWindow pw in s.Windows)
                    {
                        if (pw.Identity == window)
                        {
                            sourceDevice = pair.Key;
                            sourceScene = s;
                            break;
                        }
                    }
                    if (sourceScene is not null)
                    {
                        break;
                    }
                }
                if (sourceScene is not null)
                {
                    break;
                }
            }

            if (sourceScene is null || sourceDevice is null)
            {
                return; // window not tracked
            }

            ParkedWindow movedPw = sourceScene.Windows.First(pw => pw.Identity == window);

            StageState newState = _state;

            // 1. Remove from source.
            ImmutableList<ParkedWindow> sourceRemainder = sourceScene.Windows.Remove(movedPw);
            ImmutableList<Scene> sourceMonitorScenes = newState.ScenesByDevice[sourceDevice];

            if (sourceRemainder.IsEmpty)
            {
                // Source scene becomes empty — remove it entirely.
                ImmutableList<Scene> updatedSourceList = sourceMonitorScenes.Remove(sourceScene);
                newState = newState with
                {
                    ScenesByDevice = newState.ScenesByDevice.SetItem(
                        sourceDevice,
                        updatedSourceList
                    ),
                    ActiveSceneByDevice = ResetActiveIfMatches(
                        newState.ActiveSceneByDevice,
                        sourceDevice,
                        sourceScene.Id
                    ),
                };
            }
            else
            {
                // Promote next member to primary if the moved one was primary.
                WindowIdentity newPrimary =
                    movedPw.Identity == sourceScene.Primary
                        ? sourceRemainder[0].Identity
                        : sourceScene.Primary;
                Scene updatedSource = new(
                    sourceScene.Id,
                    sourceScene.Title,
                    sourceRemainder,
                    newPrimary,
                    sourceScene.CreatedAt
                )
                {
                    ProcessName = sourceScene.ProcessName,
                };
                ImmutableList<Scene> updatedSourceList = sourceMonitorScenes.Replace(
                    sourceScene,
                    updatedSource
                );
                newState = newState with
                {
                    ScenesByDevice = newState.ScenesByDevice.SetItem(
                        sourceDevice,
                        updatedSourceList
                    ),
                };
            }

            // 2. Append to target (or mint a new scene).
            if (targetScene is { } targetId)
            {
                Scene? destScene = null;
                string? destDevice = null;
                foreach (KeyValuePair<string, ImmutableList<Scene>> pair in newState.ScenesByDevice)
                {
                    foreach (Scene s in pair.Value)
                    {
                        if (s.Id == targetId)
                        {
                            destScene = s;
                            destDevice = pair.Key;
                            break;
                        }
                    }
                    if (destScene is not null)
                    {
                        break;
                    }
                }

                // Per Plan §Design.6a step 3: if the target scene lives on a
                // different monitor than the moved window, the merge is
                // rejected (a window's scene must always be on its current
                // monitor). Treat as `null` target — a new scene on the
                // source device.
                var crossMonitor =
                    destScene is not null
                    && destDevice is not null
                    && !string.Equals(destDevice, sourceDevice, StringComparison.OrdinalIgnoreCase);
                if (crossMonitor)
                {
                    s_logMoveCrossMonitor(_log, sourceDevice, destDevice!, null);
                }

                if (destScene is null || destDevice is null || crossMonitor)
                {
                    // Target not found OR cross-monitor — fall through to
                    // "new scene" semantics on the source device.
                    Scene fresh = new(
                        SceneId.New(),
                        FallbackTitleForPid(movedPw.Identity.ProcessId),
                        [movedPw],
                        movedPw.Identity,
                        _clock.UtcNow
                    );
                    newState = newState with
                    {
                        ScenesByDevice = newState.ScenesByDevice.SetItem(
                            sourceDevice,
                            newState.ScenesByDevice[sourceDevice].Add(fresh)
                        ),
                    };
                }
                else
                {
                    Scene merged = new(
                        destScene.Id,
                        destScene.Title,
                        destScene.Windows.Add(movedPw),
                        destScene.Primary,
                        destScene.CreatedAt
                    )
                    {
                        ProcessName = destScene.ProcessName,
                    };
                    newState = newState with
                    {
                        ScenesByDevice = newState.ScenesByDevice.SetItem(
                            destDevice,
                            newState.ScenesByDevice[destDevice].Replace(destScene, merged)
                        ),
                    };
                }
            }
            else
            {
                // null target → new scene on source's monitor.
                Scene fresh = new(
                    SceneId.New(),
                    FallbackTitleForPid(movedPw.Identity.ProcessId),
                    [movedPw],
                    movedPw.Identity,
                    _clock.UtcNow
                );
                newState = newState with
                {
                    ScenesByDevice = newState.ScenesByDevice.SetItem(
                        sourceDevice,
                        newState.ScenesByDevice[sourceDevice].Add(fresh)
                    ),
                };
            }

            SetState(newState);

            // Re-sync overlay for the affected device(s).
            ResyncOverlayForDevice(sourceDevice);
        }
        finally
        {
            _stateGate.Release();
        }
        RaisePendingEvents();
    }

    /// <inheritdoc />
    public async Task IngestNewWindowsAsync(CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state.Phase != StagePhase.Enabled)
            {
                return;
            }

            // Tracked identities across all current scenes.
            var tracked = new HashSet<WindowIdentity>();
            foreach (KeyValuePair<string, ImmutableList<Scene>> pair in _state.ScenesByDevice)
            {
                foreach (Scene scene in pair.Value)
                {
                    foreach (ParkedWindow pw in scene.Windows)
                    {
                        tracked.Add(pw.Identity);
                    }
                }
            }

            IReadOnlyList<MonitorDescriptor> monitors = _monitors.EnumerateAll();
            Dictionary<IntPtr, string> hmonToDevice = new();
            foreach (MonitorDescriptor m in monitors)
            {
                hmonToDevice[m.Hmonitor] = m.DeviceName;
            }

            IReadOnlyList<WindowSnapshot> currentWindows = _enumerator.GetManageableWindows();
            ImmutableDictionary<string, ImmutableList<Scene>> scenesByDevice =
                _state.ScenesByDevice;
            var changedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WindowSnapshot w in currentWindows)
            {
                if (!_filter.IsManageable(w))
                {
                    continue;
                }
                var identity = new WindowIdentity(w.Hwnd, w.ProcessId, w.ProcessStartTimeUtcTicks);
                if (tracked.Contains(identity))
                {
                    continue;
                }
                if (!hmonToDevice.TryGetValue(w.Monitor, out string? device))
                {
                    continue;
                }

                ImmutableList<Scene> deviceScenes = scenesByDevice.TryGetValue(
                    device,
                    out ImmutableList<Scene>? existing
                )
                    ? existing
                    : ImmutableList<Scene>.Empty;

                SceneAssignment assignment = _grouper.AssignToScene(
                    w,
                    deviceScenes,
                    SceneGroupingMode.ByProcessId
                );
                ParkedWindow newPw = new(identity, w.Bounds, device, IsElevated: false);

                if (assignment.CreatedNewScene)
                {
                    deviceScenes = deviceScenes.Add(
                        new Scene(
                            assignment.TargetScene,
                            SafeTitle(w.Title, w.ProcessName),
                            [newPw],
                            newPw.Identity,
                            _clock.UtcNow
                        )
                        {
                            ProcessName = w.ProcessName,
                        }
                    );
                }
                else
                {
                    var idx = -1;
                    for (var i = 0; i < deviceScenes.Count; i++)
                    {
                        if (deviceScenes[i].Id == assignment.TargetScene)
                        {
                            idx = i;
                            break;
                        }
                    }
                    if (idx < 0)
                    {
                        continue;
                    }
                    Scene es = deviceScenes[idx];
                    deviceScenes = deviceScenes.SetItem(
                        idx,
                        new Scene(es.Id, es.Title, es.Windows.Add(newPw), es.Primary, es.CreatedAt)
                        {
                            ProcessName = es.ProcessName,
                        }
                    );
                }

                scenesByDevice = scenesByDevice.SetItem(device, deviceScenes);
                changedDevices.Add(device);

                // Park the new window only if it joined a non-active scene.
                // When it joined the device's currently-active scene (e.g. the
                // user opened a second window of the foreground app), it has
                // to stay at OriginalBounds — parking it would hide a window
                // that the user just summoned.
                SceneId? activeIdForDevice = _state.ActiveSceneByDevice.TryGetValue(
                    device,
                    out SceneId? aa
                )
                    ? aa
                    : null;
                var joinedActive =
                    !assignment.CreatedNewScene && assignment.TargetScene == activeIdForDevice;
                if (!joinedActive)
                {
                    try
                    {
                        _windows.Park(identity.Hwnd, w.Bounds);
                    }
                    catch (Win32InteropException) { }
                    catch (ElevationBoundaryException) { }
                }
            }

            if (changedDevices.Count == 0)
            {
                return;
            }

            SetState(_state with { ScenesByDevice = scenesByDevice });

            foreach (string device in changedDevices)
            {
                ResyncOverlayForDevice(device);
            }
        }
        finally
        {
            _stateGate.Release();
        }
        RaisePendingEvents();
    }

    // ---------------------------------------------------------------------
    // EnableAsync core
    // ---------------------------------------------------------------------

    private async Task<StageState> EnableCoreAsync(Stack<Func<Task>> undo, CancellationToken ct)
    {
        IReadOnlyList<MonitorDescriptor> monitors = _monitors.EnumerateAll();

        // Build hmonitor → device-name map.
        Dictionary<IntPtr, string> hmonToDevice = new();
        foreach (MonitorDescriptor m in monitors)
        {
            hmonToDevice[m.Hmonitor] = m.DeviceName;
        }

        // Per-device builders for the new state.
        Dictionary<string, ImmutableList<Scene>.Builder> scenesByDevice = new(
            StringComparer.OrdinalIgnoreCase
        );
        Dictionary<string, SceneId?> activeSceneByDevice = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SavedWorkArea> savedWorkAreas = new(StringComparer.OrdinalIgnoreCase);

        foreach (MonitorDescriptor m in monitors)
        {
            scenesByDevice[m.DeviceName] = ImmutableList.CreateBuilder<Scene>();
            activeSceneByDevice[m.DeviceName] = null;
            savedWorkAreas[m.DeviceName] = new SavedWorkArea(m.DeviceName, m.WorkArea);
        }

        // Step 4 + 5 + 5a: enumerate windows in Z-order top-down, filter by
        // IWindowFilter (the enumerator returns RAW snapshots — only the
        // filter applies the 6 manageability rules), then group into scenes.
        IReadOnlyList<WindowSnapshot> allWindows = _enumerator.GetManageableWindows();
        foreach (WindowSnapshot w in allWindows)
        {
            if (!_filter.IsManageable(w))
            {
                continue;
            }
            if (!hmonToDevice.TryGetValue(w.Monitor, out string? device))
            {
                continue; // window on no known monitor
            }

            ImmutableList<Scene>.Builder builder = scenesByDevice[device];
            ImmutableList<Scene> existing = builder.ToImmutable();
            SceneAssignment assignment = _grouper.AssignToScene(
                w,
                existing,
                SceneGroupingMode.ByProcessId
            );

            ParkedWindow pw = new(
                new WindowIdentity(w.Hwnd, w.ProcessId, w.ProcessStartTimeUtcTicks),
                w.Bounds,
                device,
                IsElevated: false
            );

            if (assignment.CreatedNewScene)
            {
                Scene scene = new(
                    assignment.TargetScene,
                    SafeTitle(w.Title, w.ProcessName),
                    [pw],
                    pw.Identity,
                    _clock.UtcNow
                )
                {
                    ProcessName = w.ProcessName,
                };
                builder.Add(scene);

                if (activeSceneByDevice[device] is null && !IsMinimised(w))
                {
                    activeSceneByDevice[device] = scene.Id;
                }
            }
            else
            {
                var idx = builder.FindIndex(s => s.Id == assignment.TargetScene);
                if (idx < 0)
                {
                    continue; // shouldn't happen — grouper returned non-existent id
                }
                Scene existingScene = builder[idx];
                Scene updated = new(
                    existingScene.Id,
                    existingScene.Title,
                    existingScene.Windows.Add(pw),
                    existingScene.Primary,
                    existingScene.CreatedAt
                )
                {
                    ProcessName = existingScene.ProcessName,
                };
                builder[idx] = updated;
            }
        }

        // Step 7 + 8: capture + apply work areas.
        // The RestoreAll-undo is pushed BEFORE the loop so that a partial
        // failure mid-loop (e.g. SetWorkArea throws on monitor 2 after
        // monitor 1 succeeded) still has its undo on the rollback stack.
        // WorkAreaManager tracks per-device originals internally, so
        // RestoreAll is the right rollback regardless of how far the loop
        // got. (Plan §Design.4 — push undo as soon as the step has any
        // partial effect.)
        undo.Push(() =>
        {
            _workArea.RestoreAll();
            return Task.CompletedTask;
        });
        foreach (MonitorDescriptor m in monitors)
        {
            Rect newWA = ShrinkLeft(m.WorkArea, SidebarReservationPx);
            _workArea.SetWorkArea(m.Hmonitor, newWA);
        }

        // Step 9 + 10: park non-active scenes; resize active scene's windows to main area.
        Dictionary<string, ImmutableList<Scene>> finalScenesByDevice = new(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (MonitorDescriptor m in monitors)
        {
            ImmutableList<Scene>.Builder builder = scenesByDevice[m.DeviceName];
            SceneId? activeId = activeSceneByDevice[m.DeviceName];

            Rect activeWorkArea = ShrinkLeft(m.WorkArea, SidebarReservationPx);
            for (var i = 0; i < builder.Count; i++)
            {
                Scene scene = builder[i];
                if (scene.Id == activeId)
                {
                    // Active scene: keep window sizes (Apple Stage Manager
                    // pattern — surfacing a window must not resize it), but
                    // shift any window whose pre-Enable bounds overlap the
                    // newly-reserved sidebar slice into the post-shrink work
                    // area. A maximised window reflows automatically; a
                    // floating window pinned to x=0 would otherwise sit
                    // half-hidden under the sidebar.
                    foreach (ParkedWindow pw in scene.Windows)
                    {
                        if (pw.IsElevated)
                        {
                            continue;
                        }
                        Rect translated = TranslateIntoWorkArea(pw.OriginalBounds, activeWorkArea);
                        if (translated == pw.OriginalBounds)
                        {
                            continue;
                        }
                        try
                        {
                            _windows.Resize(pw.Identity.Hwnd, translated);
                        }
                        catch (Win32InteropException)
                        {
                            // best effort
                        }
                        catch (ElevationBoundaryException)
                        {
                            // best effort
                        }
                    }
                    try
                    {
                        _windows.BringToFront(scene.Primary.Hwnd);
                    }
                    catch (Win32InteropException)
                    {
                        // best effort
                    }
                }
                else
                {
                    foreach (ParkedWindow pw in scene.Windows)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            _windows.Park(pw.Identity.Hwnd, pw.OriginalBounds);
                            ParkedWindow capturedPw = pw;
                            undo.Push(() =>
                            {
                                _windows.RestorePosition(
                                    capturedPw.Identity.Hwnd,
                                    capturedPw.OriginalBounds
                                );
                                return Task.CompletedTask;
                            });
                        }
                        catch (Win32InteropException)
                        {
                            // skip; window probably died between enumeration and park
                        }
                        catch (ElevationBoundaryException)
                        {
                            builder[i] = ReplaceParkedFlag(scene, pw, isElevated: true);
                            scene = builder[i];
                            s_logElevatedSkip(_log, pw.Identity.Hwnd, null);
                        }
                    }
                }
            }
            finalScenesByDevice[m.DeviceName] = builder.ToImmutable();
        }

        // Build new StageState.
        ImmutableDictionary<string, ImmutableList<Scene>> scenesDict =
            ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, finalScenesByDevice);
        ImmutableDictionary<string, SceneId?> activeDict = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            activeSceneByDevice
        );
        ImmutableDictionary<string, SavedWorkArea> savedDict = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            savedWorkAreas
        );

        StageState newState = new(
            StagePhase.Enabling,
            scenesDict,
            activeDict,
            savedDict,
            ImmutableList<WindowIdentity>.Empty,
            IsPaused: false
        );

        // Step 6: write snapshot. We pass the `Enabled`-shaped state since
        // the snapshot represents what we just achieved.
        // The undo is pushed BEFORE the write call so that a partial-write
        // failure (temp file created, File.Replace then fails) still
        // produces a Delete on rollback. SnapshotStore.WriteAsync also
        // cleans up its own .tmp on failure (defence in depth).
        undo.Push(() => _snapshot.DeleteAsync(CancellationToken.None));
        await _snapshot
            .WriteAsync(newState with { Phase = StagePhase.Enabled }, ct)
            .ConfigureAwait(false);

        // Step 11: create overlays + sync.
        foreach (MonitorDescriptor m in monitors)
        {
            _overlay.CreateForMonitor(m.Hmonitor);
            IntPtr capturedH = m.Hmonitor;
            undo.Push(() =>
            {
                _overlay.DisposeForMonitor(capturedH);
                return Task.CompletedTask;
            });

            ImmutableList<Scene> monitorScenes = finalScenesByDevice[m.DeviceName];
            SceneId? active = activeSceneByDevice[m.DeviceName];
            List<Scene> parked = monitorScenes.Where(s => s.Id != active).ToList();
            _overlay.SyncMonitor(m.Hmonitor, parked);
        }

        return newState;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static SceneSwapPlan BuildSwapPlan(
        SceneId target,
        SceneId? currentActive,
        string deviceName,
        ImmutableList<Scene> scenes,
        Rect activeWorkArea
    )
    {
        Scene incoming = scenes.First(s => s.Id == target);
        Scene? outgoing = currentActive is { } cur ? scenes.FirstOrDefault(s => s.Id == cur) : null;

        Rect parkRect = new(-32000, -32000, 1, 1);

        // Each incoming-scene window goes back to its own OriginalBounds —
        // that's where the user had it before Stage was enabled. Apple Stage
        // Manager pattern: surfacing a window means restoring it to where it
        // lived, not slamming it to a fullscreen "main area". Translate (no
        // resize) so a window that was pinned to x=0 doesn't surface half-
        // hidden under the sidebar reservation.
        var incomingMoves = new List<SceneWindowMove>(incoming.Windows.Count);
        foreach (ParkedWindow pw in incoming.Windows)
        {
            if (pw.IsElevated)
            {
                continue;
            }
            Rect destination = TranslateIntoWorkArea(pw.OriginalBounds, activeWorkArea);
            incomingMoves.Add(new SceneWindowMove(pw.Identity, parkRect, destination));
        }

        var outgoingMoves = new List<SceneWindowMove>();
        if (outgoing is not null)
        {
            foreach (ParkedWindow pw in outgoing.Windows)
            {
                if (pw.IsElevated)
                {
                    continue;
                }
                outgoingMoves.Add(new SceneWindowMove(pw.Identity, pw.OriginalBounds, parkRect));
            }
        }

        return new SceneSwapPlan(
            IncomingSceneId: target,
            IncomingMoves: incomingMoves,
            OutgoingSceneId: currentActive,
            OutgoingMoves: outgoingMoves,
            TargetDeviceName: deviceName,
            IncomingPrimary: incoming.Primary
        );
    }

    private void ResyncOverlayForDevice(string deviceName)
    {
        if (!_state.ScenesByDevice.TryGetValue(deviceName, out ImmutableList<Scene>? scenes))
        {
            return;
        }
        SceneId? active = _state.ActiveSceneByDevice.TryGetValue(deviceName, out SceneId? a)
            ? a
            : null;
        var parked = scenes.Where(s => s.Id != active).ToList();

        // Resolve hmonitor for this device name.
        IReadOnlyList<MonitorDescriptor> mons = _monitors.EnumerateAll();
        foreach (MonitorDescriptor m in mons)
        {
            if (string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                _overlay.SyncMonitor(m.Hmonitor, parked);
                return;
            }
        }
    }

    /// <summary>
    /// Updates the authoritative state and queues a StateChanged
    /// notification. Call from inside <c>_stateGate</c> / <c>_swapGate</c>;
    /// the event is NOT raised synchronously — it fires after the
    /// semaphore is released, via <see cref="RaisePendingEvents"/>.
    /// </summary>
    private void SetState(StageState newState)
    {
        _state = newState;
        _pendingNotifications.Enqueue(newState);
    }

    /// <summary>
    /// Drains queued StateChanged notifications. Must be called <em>after</em>
    /// any state-mutating semaphore has been released so a subscriber that
    /// calls back into the controller cannot deadlock.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A misbehaving subscriber must not be allowed to abort the post-mutation event drain."
    )]
    private void RaisePendingEvents()
    {
        // Snapshot the queue to a local list under the assumption that
        // we're already off the semaphore — concurrent writers can't enqueue
        // here (state mutations are serialised) but we still copy defensively.
        StageState[] snapshot;
        lock (_pendingNotifications)
        {
            snapshot = [.. _pendingNotifications];
            _pendingNotifications.Clear();
        }
        EventHandler<StageState>? handler = StateChanged;
        if (handler is null)
        {
            return;
        }
        foreach (StageState s in snapshot)
        {
            try
            {
                handler.Invoke(this, s);
            }
            catch (Exception ex)
            {
                s_logSubscriberFault(_log, ex);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Rollback steps are best-effort; one failing rollback must not abort the rest."
    )]
    private async Task DrainRollbackAsync(Stack<Func<Task>> undo)
    {
        while (undo.TryPop(out Func<Task>? action))
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                s_logRollbackStep(_log, ex);
            }
        }
    }

    private static int CountAllScenes(StageState s)
    {
        var total = 0;
        foreach (KeyValuePair<string, ImmutableList<Scene>> pair in s.ScenesByDevice)
        {
            total += pair.Value.Count;
        }
        return total;
    }

    private static bool IsMinimised(WindowSnapshot w) => (w.Style & 0x20000000L) != 0; // WS_MINIMIZE

    private static Rect ShrinkLeft(Rect r, int px) =>
        new(r.X + px, r.Y, Math.Max(0, r.Width - px), r.Height);

    /// <summary>
    /// Returns <paramref name="bounds"/> shifted (without resizing) so it
    /// fits inside <paramref name="workArea"/>. If the window is wider /
    /// taller than the work area its left/top edge anchors to the work
    /// area's left/top; the overhang is accepted as-is. The Apple Stage
    /// Manager pattern is "never resize the user's window" — translating is
    /// the only acceptable correction when the sidebar reservation pushes a
    /// window's left edge into the parked-tile column.
    /// </summary>
    internal static Rect TranslateIntoWorkArea(Rect bounds, Rect workArea)
    {
        var x = bounds.X;
        if (x < workArea.X)
        {
            x = workArea.X;
        }
        else if (x + bounds.Width > workArea.X + workArea.Width)
        {
            x = Math.Max(workArea.X, workArea.X + workArea.Width - bounds.Width);
        }

        var y = bounds.Y;
        if (y < workArea.Y)
        {
            y = workArea.Y;
        }
        else if (y + bounds.Height > workArea.Y + workArea.Height)
        {
            y = Math.Max(workArea.Y, workArea.Y + workArea.Height - bounds.Height);
        }

        return new Rect(x, y, bounds.Width, bounds.Height);
    }

    private static string SafeTitle(string title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title;

    private static string FallbackTitleForPid(int processId) =>
        "Window pid " + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Scene ReplaceParkedFlag(Scene scene, ParkedWindow target, bool isElevated)
    {
        ImmutableList<ParkedWindow> updated = scene.Windows.Replace(
            target,
            target with
            {
                IsElevated = isElevated,
            }
        );
        return new Scene(scene.Id, scene.Title, updated, scene.Primary, scene.CreatedAt)
        {
            ProcessName = scene.ProcessName,
        };
    }

    private static ImmutableDictionary<string, SceneId?> ResetActiveIfMatches(
        ImmutableDictionary<string, SceneId?> active,
        string device,
        SceneId removed
    )
    {
        if (active.TryGetValue(device, out SceneId? cur) && cur == removed)
        {
            return active.SetItem(device, null);
        }
        return active;
    }
}
