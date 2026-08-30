namespace App.Interop.Internal;

/// <summary>
/// Thin, testable seam over the Win32 / DWM window APIs consumed by the
/// Plan 02 window-mechanics layer.
/// </summary>
/// <remarks>
/// <para>
/// Every method on this interface maps to one or two native calls in
/// <see cref="NativeMethods"/> and translates failures into
/// <see cref="App.Interop.Errors.Win32InteropException"/> with the original
/// <c>GetLastError</c> code preserved. No method silently swallows a
/// native failure.
/// </para>
/// <para>
/// The seam is <c>internal</c> on purpose — the native surface is not part
/// of Stagehand's public API. The Tests assembly has access via
/// <c>InternalsVisibleTo</c> (see <c>AssemblyInfo.cs</c>) so the higher
/// layers (<c>WindowEnumerator</c>, <c>WindowController</c>) can be tested
/// against a fake without booting a real Win32 window.
/// </para>
/// </remarks>
internal interface INativeWindowApi
{
    /// <summary>
    /// Enumerates every top-level window currently on the desktop, in the
    /// order returned by <c>EnumWindows</c>.
    /// </summary>
    /// <returns>Raw HWND values. Never <see langword="null"/>.</returns>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>EnumWindows</c> failed.
    /// </exception>
    IReadOnlyList<IntPtr> EnumTopLevel();

    /// <summary>Reads the current window title.</summary>
    /// <returns>The title, or <see cref="string.Empty"/> when the window has none.</returns>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>GetWindowText</c> failed with a non-zero Win32 error code.
    /// </exception>
    string GetWindowText(IntPtr hwnd);

    /// <summary>Reads the Win32 window-class name.</summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>GetClassName</c> returned zero.
    /// </exception>
    string GetClassName(IntPtr hwnd);

    /// <summary>True when the window's <c>WS_VISIBLE</c> bit is set (ignores cloaking).</summary>
    bool IsWindowVisible(IntPtr hwnd);

    /// <summary>
    /// True when DWM reports the window as cloaked (<c>DWMWA_CLOAKED</c> non-zero).
    /// UWP windows on inactive virtual desktops are cloaked.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmGetWindowAttribute</c> failed.
    /// </exception>
    bool IsCloaked(IntPtr hwnd);

    /// <summary>
    /// Returns <c>GetAncestor(hwnd, GA_ROOT)</c> — the top-level window that
    /// owns <paramref name="hwnd"/>'s chain of parents.
    /// </summary>
    IntPtr GetAncestorRoot(IntPtr hwnd);

    /// <summary>
    /// Returns the <c>GWL_STYLE</c> bits as a 64-bit value (matches
    /// <c>GetWindowLongPtr</c> width).
    /// </summary>
    long GetWindowStyle(IntPtr hwnd);

    /// <summary>Returns the <c>GWL_EXSTYLE</c> bits.</summary>
    long GetWindowExStyle(IntPtr hwnd);

    /// <summary>
    /// Returns the owner window HWND (<c>GWL_HWNDPARENT</c>). <c>IntPtr.Zero</c>
    /// when the window is un-owned.
    /// </summary>
    IntPtr GetWindowOwner(IntPtr hwnd);

    /// <summary>
    /// Reads the window bounds in physical pixels via <c>GetWindowRect</c>.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>GetWindowRect</c> failed.
    /// </exception>
    Rect GetWindowRect(IntPtr hwnd);

    /// <summary>
    /// Returns the process id that owns <paramref name="hwnd"/> via
    /// <c>GetWindowThreadProcessId</c>.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>GetWindowThreadProcessId</c> failed.
    /// </exception>
    int GetProcessId(IntPtr hwnd);

    /// <summary>
    /// Returns the owning process's creation time, in UTC ticks, for use as
    /// part of <c>WindowIdentity</c>.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// Opening the process handle or reading its times failed. The caller
    /// should treat that window as unmanageable.
    /// </exception>
    long GetProcessStartTimeUtcTicks(int processId);

    /// <summary>
    /// Returns the HMONITOR the window is currently associated with, using
    /// <c>MONITOR_DEFAULTTONEAREST</c>. Never returns <c>IntPtr.Zero</c> on a
    /// system with at least one active display.
    /// </summary>
    IntPtr MonitorFromWindow(IntPtr hwnd);

    /// <summary>
    /// Moves / resizes <paramref name="hwnd"/> to <paramref name="rect"/>.
    /// The caller chooses Z-order / activation semantics via
    /// <paramref name="flags"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the OS accepted the call. <see langword="false"/>
    /// when Windows refused (e.g. the target window belongs to a higher-integrity
    /// process) — callers inspect <c>GetLastError</c> via the returned
    /// <see cref="App.Interop.Errors.Win32InteropException"/> to distinguish
    /// <c>ERROR_ACCESS_DENIED</c> from generic failures. This method itself does
    /// NOT throw on a <see langword="false"/> return; the caller decides whether
    /// to raise <c>ElevationBoundaryException</c> or treat it as soft failure.
    /// </returns>
    bool SetWindowPos(IntPtr hwnd, Rect rect, SetWindowPosFlags flags);

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the foreground. Returns
    /// <see langword="false"/> when Windows's focus-stealing guard blocks the
    /// call; the caller is expected to try the <c>AttachThreadInput</c> dance
    /// (see <c>WindowController.BringToFront</c>).
    /// </summary>
    bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// Returns the HWND of the current foreground window, or
    /// <see cref="IntPtr.Zero"/> if there is none (e.g. during a desktop switch).
    /// Used by <c>WindowController.BringToFront</c> as the short-circuit when
    /// the target is already foreground.
    /// </summary>
    IntPtr GetForegroundWindow();

    /// <summary>
    /// Returns the calling thread's Win32 thread id. Never fails.
    /// Consumed by <c>WindowController.BringToFront</c> to feed
    /// <see cref="AttachThreadInput"/>.
    /// </summary>
    uint GetCurrentThreadId();

    /// <summary>
    /// Returns the last Win32 error code captured for the calling thread by
    /// the most recent <c>SetLastError=true</c> P/Invoke. Callers use this to
    /// inspect failure codes from methods that return <see langword="false"/>
    /// without throwing (e.g. <see cref="SetWindowPos"/>) — notably to
    /// distinguish <c>ERROR_ACCESS_DENIED (5)</c> (elevation boundary) from
    /// generic failures.
    /// </summary>
    int GetLastErrorCode();

    /// <summary>
    /// Returns the owning thread id of <paramref name="hwnd"/> and, as an
    /// <c>out</c> parameter, the owning process id. Mirrors the Win32
    /// signature exactly so <c>WindowController.BringToFront</c> can feed the
    /// thread id to <see cref="AttachThreadInput"/> without a second call.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>GetWindowThreadProcessId</c> returned zero (the HWND is invalid).
    /// </exception>
    uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    /// <summary>
    /// Calls <c>ShowWindow</c> with the supplied <paramref name="nCmdShow"/>
    /// command (e.g. <c>SW_SHOWNA</c> to assert <c>WS_VISIBLE</c> without
    /// stealing focus). Used by <c>WindowController.RestorePosition</c> to
    /// recover UWP windows that DWM cloaked while parked off-screen — those
    /// stay invisible after a plain <c>SetWindowPos</c> back to the original
    /// rect because the cloak bit is independent of the window rect.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the window was previously visible,
    /// <see langword="false"/> if previously hidden. Stagehand does not act on
    /// the prior state today, but the bool surface mirrors the Win32 contract
    /// for symmetry with other seam methods.
    /// </returns>
    bool ShowWindow(IntPtr hwnd, int nCmdShow);

    /// <summary>
    /// True when <paramref name="hwnd"/> is currently maximised
    /// (<c>WS_MAXIMIZE</c> set, equivalent to <c>IsZoomed</c>).
    /// Callers that intend to <c>SetWindowPos</c> a maximised window must
    /// first issue <c>ShowWindow(SW_RESTORE)</c> — otherwise Windows treats
    /// the new bounds as the "restored" rect and snaps the window straight
    /// back to its prior maximised geometry on the next paint.
    /// </summary>
    bool IsZoomed(IntPtr hwnd);

    /// <summary>
    /// True when <paramref name="hwnd"/> is currently minimised
    /// (<c>IsIconic</c>). <c>GetWindowRect</c> returns the off-screen iconic
    /// position (typically <c>(-32000, -32000, …)</c>) for such windows —
    /// not useful for layout, restore, or thumbnail aspect ratio.
    /// </summary>
    bool IsIconic(IntPtr hwnd);

    /// <summary>
    /// Returns the rect the window occupies (or will occupy after
    /// <c>SW_RESTORE</c>) — i.e. <c>GetWindowPlacement.rcNormalPosition</c>
    /// when the window is minimised, otherwise <c>GetWindowRect</c>.
    /// Callers that need a stable, layout-meaningful bounding rect (sidebar
    /// thumbnails, restore-on-disable) read this instead of
    /// <see cref="GetWindowRect"/>.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// Both <c>GetWindowPlacement</c> and the fallback <c>GetWindowRect</c>
    /// failed.
    /// </exception>
    Rect GetRestoredBounds(IntPtr hwnd);

    /// <summary>
    /// Attaches or detaches the input-processing mechanism of two threads.
    /// Used by <c>WindowController.BringToFront</c> to side-step the
    /// foreground-lock timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contract: <b>returns <see langword="true"/> on success, <see langword="false"/>
    /// on failure, never throws.</b> A failing detach must not be able to mask an
    /// already-in-flight exception inside a <c>finally</c> block; a failing attach
    /// is surfaced by the caller itself (after inspecting <see cref="GetLastErrorCode"/>)
    /// rather than by this seam throwing on its own.
    /// </para>
    /// </remarks>
    bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
}
