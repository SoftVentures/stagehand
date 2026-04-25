using System;
using App.Interop.Errors;
using App.Interop.Internal;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Production <see cref="IWindowController"/> that routes all position, size,
/// and focus mutations through the <see cref="INativeWindowApi"/> seam so the
/// behaviour can be unit-tested without booting a real Win32 window.
/// </summary>
/// <remarks>
/// <para>
/// Every public method runs on the WPF UI thread and asserts that affinity in
/// Debug (the assertion is elided in Release). Win32 window-management APIs are
/// thread-sensitive: <c>SetWindowPos</c> and <c>SetForegroundWindow</c> must be
/// called from a thread with a message loop, and the DWM thumbnail layer relies
/// on the same thread owning the destination surface.
/// </para>
/// <para>
/// Error-handling rules per Plan 02 §Design.4:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <c>SetWindowPos</c> returning <see langword="false"/> is inspected via
///       <see cref="INativeWindowApi.GetLastErrorCode"/>. A Win32 error code of
///       <c>5 (ERROR_ACCESS_DENIED)</c> is translated to
///       <see cref="ElevationBoundaryException"/> — the caller (Plan 03's
///       <c>StageController</c>) skips that window and marks it in the overlay.
///       Any other non-zero code becomes <see cref="Win32InteropException"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="BringToFront"/> implements the standard
///       <c>AttachThreadInput</c> workaround for Windows's foreground-lock rule.
///       A persistent failure after the dance raises
///       <see cref="Win32InteropException"/>; Plan 03 catches and logs at
///       Warning because only focus is off — the desktop is still consistent.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class WindowController : IWindowController
{
    /// <summary>
    /// Off-screen parking origin. The virtual-screen bounding box never spans
    /// this far negative even on a six-monitor video wall, so a parked window
    /// is guaranteed invisible regardless of monitor layout.
    /// </summary>
    private const int ParkOriginX = -32000;

    /// <summary>See <see cref="ParkOriginX"/>.</summary>
    private const int ParkOriginY = -32000;

    /// <summary><c>ERROR_ACCESS_DENIED</c> — Windows refusing an action across the integrity boundary.</summary>
    private const int ErrorAccessDenied = 5;

    private readonly INativeWindowApi _api;
    private readonly UiDispatcher _ui;
    private readonly ILogger<WindowController> _log;

    private static readonly Action<ILogger, IntPtr, int, int, Exception?> LogParked =
        LoggerMessage.Define<IntPtr, int, int>(
            LogLevel.Debug,
            new EventId(1, nameof(Park)),
            "Parked window {Hwnd} (size {Width}x{Height})."
        );

    private static readonly Action<ILogger, IntPtr, int, int, int, int, Exception?> LogRestored =
        LoggerMessage.Define<IntPtr, int, int, int, int>(
            LogLevel.Debug,
            new EventId(2, nameof(RestorePosition)),
            "Restored window {Hwnd} to ({X},{Y},{Width}x{Height})."
        );

    private static readonly Action<ILogger, IntPtr, int, int, Exception?> LogResized =
        LoggerMessage.Define<IntPtr, int, int>(
            LogLevel.Debug,
            new EventId(3, nameof(Resize)),
            "Resized window {Hwnd} to {Width}x{Height}."
        );

    private static readonly Action<ILogger, IntPtr, uint, uint, Exception?> LogAttachPath =
        LoggerMessage.Define<IntPtr, uint, uint>(
            LogLevel.Debug,
            new EventId(4, nameof(BringToFront)),
            "BringToFront({Hwnd}) using AttachThreadInput dance: current={CurrentThread}, target={TargetThread}."
        );

    private static readonly Action<ILogger, IntPtr, int, Exception?> LogAttachFailed =
        LoggerMessage.Define<IntPtr, int>(
            LogLevel.Warning,
            new EventId(5, nameof(BringToFront)),
            "BringToFront({Hwnd}): AttachThreadInput(attach=true) returned false (Win32 error {Error}); skipping foreground call."
        );

    /// <summary>Initializes a new <see cref="WindowController"/>.</summary>
    /// <param name="api">Native Win32 seam. Must not be <see langword="null"/>.</param>
    /// <param name="ui">UI dispatcher used to assert thread affinity in Debug.</param>
    /// <param name="log">Logger for diagnostic messages.</param>
    internal WindowController(INativeWindowApi api, UiDispatcher ui, ILogger<WindowController> log)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Public constructor used by DI. Resolves the production native seam
    /// itself so the shell composition root does not need to register the
    /// internal <see cref="INativeWindowApi"/>.
    /// </summary>
    /// <param name="ui">UI dispatcher.</param>
    /// <param name="log">Logger.</param>
    public WindowController(UiDispatcher ui, ILogger<WindowController> log)
        : this(new NativeWindowApi(), ui, log) { }

    /// <inheritdoc />
    public void Park(IntPtr hwnd, Rect parkingRect)
    {
        _ui.AssertOnUiThread();

        // Preserve original size; only the origin is forced off-screen. Flags
        // mirror §Design.4: no Z-order change, no activation, no redraw burst
        // (the parked window is immediately invisible anyway).
        var dest = new Rect(ParkOriginX, ParkOriginY, parkingRect.Width, parkingRect.Height);
        SetWindowPosOrThrow(
            hwnd,
            dest,
            SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate | SetWindowPosFlags.NoRedraw,
            nameof(Park)
        );
        LogParked(_log, hwnd, parkingRect.Width, parkingRect.Height, null);
    }

    /// <inheritdoc />
    public void RestorePosition(IntPtr hwnd, Rect original)
    {
        _ui.AssertOnUiThread();

        // No NoRedraw on restore: Plan 02 §Design.4 originally specified the
        // same flags as Park, but the symmetry was wrong. Park hides, Restore
        // must show — and a `SetWindowPos` without a redraw burst on a
        // previously off-screen window leaves the destination region
        // un-painted on real machines (observed with WhatsApp Desktop and
        // other UWP apps). The Plan-02 markdown has been updated to match.
        SetWindowPosOrThrow(
            hwnd,
            original,
            SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate,
            nameof(RestorePosition)
        );

        // UWP / minimize-to-tray apps frequently set DWMWA_CLOAKED while
        // parked off-screen — the cloak bit is independent of the window
        // rect, so SetWindowPos alone leaves the window invisible to DWM
        // even though its bounds are now on-screen. ShowWindow with
        // SW_SHOWNA (= show without activation) clears the cloak without
        // stealing focus from whatever the user is currently doing. The
        // call is a no-op for non-cloaked windows.
        if (_api.IsCloaked(hwnd))
        {
            _ = _api.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
        }

        LogRestored(_log, hwnd, original.X, original.Y, original.Width, original.Height, null);
    }

    /// <inheritdoc />
    public void Resize(IntPtr hwnd, Rect target)
    {
        _ui.AssertOnUiThread();

        // No NoRedraw here: the app should repaint its chrome at the new size.
        SetWindowPosOrThrow(
            hwnd,
            target,
            SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate,
            nameof(Resize)
        );
        LogResized(_log, hwnd, target.Width, target.Height, null);
    }

    /// <inheritdoc />
    public void BringToFront(IntPtr hwnd)
    {
        _ui.AssertOnUiThread();

        // Short-circuit: already foreground → nothing to do. Avoids the
        // AttachThreadInput churn for the common case of clicking a window
        // that's already focused.
        var currentForeground = _api.GetForegroundWindow();
        if (currentForeground == hwnd)
        {
            return;
        }

        var currentThread = _api.GetCurrentThreadId();
        var targetThread = _api.GetWindowThreadProcessId(hwnd, out _);

        // Same thread → no attach needed. SetForegroundWindow on your own
        // thread isn't subject to the focus-lock timeout.
        if (targetThread == currentThread)
        {
            if (!_api.SetForegroundWindow(hwnd))
            {
                throw new Win32InteropException(
                    _api.GetLastErrorCode(),
                    $"SetForegroundWindow({hwnd}) failed (same-thread path)."
                );
            }
            return;
        }

        // Cross-thread path: attach input queues, call SetForegroundWindow,
        // detach in finally regardless of outcome. Leaking an attached input
        // queue would hang the target thread's foreground switches.
        //
        // AttachThreadInput returns bool (never throws, see INativeWindowApi):
        //   - Attach failure: log and bail out without calling SetForegroundWindow.
        //     No corresponding detach is needed because the attach never succeeded.
        //   - Detach in `finally`: best-effort per Microsoft docs. Return value is
        //     intentionally ignored; throwing from `finally` would mask an already-
        //     in-flight exception from the `try` block.
        LogAttachPath(_log, hwnd, currentThread, targetThread, null);
        if (!_api.AttachThreadInput(currentThread, targetThread, attach: true))
        {
            LogAttachFailed(_log, hwnd, _api.GetLastErrorCode(), null);
            return;
        }

        bool ok;
        var err = 0;
        try
        {
            ok = _api.SetForegroundWindow(hwnd);
            if (!ok)
            {
                err = _api.GetLastErrorCode();
            }
        }
        finally
        {
            // Best-effort per Microsoft docs: a failing detach must not mask an
            // in-flight exception from the try block.
            _ = _api.AttachThreadInput(currentThread, targetThread, attach: false);
        }

        if (!ok)
        {
            throw new Win32InteropException(
                err,
                $"SetForegroundWindow({hwnd}) failed even after AttachThreadInput dance."
            );
        }
    }

    /// <summary>
    /// Wraps <see cref="INativeWindowApi.SetWindowPos"/> with the elevation /
    /// generic-failure translation policy described on the type's remarks.
    /// </summary>
    private void SetWindowPosOrThrow(IntPtr hwnd, Rect rect, SetWindowPosFlags flags, string op)
    {
        if (_api.SetWindowPos(hwnd, rect, flags))
        {
            return;
        }

        var err = _api.GetLastErrorCode();
        if (err == ErrorAccessDenied)
        {
            throw new ElevationBoundaryException(
                hwnd,
                $"{op}: SetWindowPos denied by OS (ERROR_ACCESS_DENIED). "
                    + "Target window likely belongs to a higher-integrity process."
            );
        }

        throw new Win32InteropException(err, $"{op}: SetWindowPos failed (Win32 error {err}).");
    }
}
