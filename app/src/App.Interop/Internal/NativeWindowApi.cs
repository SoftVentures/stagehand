using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using App.Interop.Errors;

namespace App.Interop.Internal;

/// <summary>
/// Production implementation of <see cref="INativeWindowApi"/>. Delegates to
/// the P/Invoke signatures in <see cref="NativeMethods"/> and converts native
/// failure modes to <see cref="Win32InteropException"/>.
/// </summary>
/// <remarks>
/// <para>
/// All state queries (<c>GetWindowText</c>, <c>GetClassName</c>, style reads,
/// geometry) translate a native error into an exception so the enumerator /
/// filter layer can choose to skip a single bad window rather than corrupt
/// the result set.
/// </para>
/// <para>
/// Mutating calls (<c>SetWindowPos</c>, <c>SetForegroundWindow</c>) return the
/// raw Win32 success value without throwing — the caller decides whether a
/// <see langword="false"/> result is a hard error (elevation boundary) or a
/// recoverable race (the window vanished between enumeration and action).
/// </para>
/// </remarks>
internal sealed class NativeWindowApi : INativeWindowApi
{
    // Buffer sizes for fixed-length Win32 string OUT parameters.
    // GetClassName's documented cap is 256 WCHARs including terminator.
    private const int ClassNameBufferChars = 256;

    // GetWindowText has no hard cap; 512 comfortably covers real-world titles
    // without paying for 64K per call. The P/Invoke returns truncated text
    // rather than failing when the title is longer, which is what we want.
    private const int WindowTextBufferChars = 512;

    public IReadOnlyList<IntPtr> EnumTopLevel()
    {
        var list = new List<IntPtr>(64);

        // Capture into the closure; EnumWindows invokes the callback synchronously
        // on the calling thread, so this is safe without additional locking.
        bool Callback(IntPtr hwnd, IntPtr _)
        {
            list.Add(hwnd);
            return true;
        }

        if (!NativeMethods.EnumWindows(Callback, IntPtr.Zero))
        {
            throw NewWin32(nameof(NativeMethods.EnumWindows));
        }

        return list;
    }

    public string GetWindowText(IntPtr hwnd)
    {
        // GetWindowText returning 0 can mean "no title" OR "error". Distinguish
        // via GetLastError: only a non-zero code is a real failure.
        // ReSharper disable once InconsistentNaming — matches Win32 convention
        Marshal.SetLastSystemError(0);
        var sb = new StringBuilder(WindowTextBufferChars);
        var written = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        if (written == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 0)
            {
                throw new Win32InteropException(
                    err,
                    $"{nameof(NativeMethods.GetWindowText)} failed (Win32 error {err})."
                );
            }
            return string.Empty;
        }
        return sb.ToString(0, written);
    }

    public string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(ClassNameBufferChars);
        var written = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        if (written == 0)
        {
            throw NewWin32(nameof(NativeMethods.GetClassName));
        }
        return sb.ToString(0, written);
    }

    public bool IsWindowVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    public bool IsCloaked(IntPtr hwnd)
    {
        // DwmGetWindowAttribute is PreserveSig=false — it throws on HRESULT
        // failure instead of returning it. Wrap so the caller sees our typed
        // exception regardless of how the native API signals failure.
        try
        {
            NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_CLOAKED,
                out var cloaked,
                sizeof(int)
            );
            return cloaked != 0;
        }
        catch (COMException ex)
        {
            throw new Win32InteropException(
                ex.HResult,
                $"{nameof(NativeMethods.DwmGetWindowAttribute)}(DWMWA_CLOAKED) failed (HRESULT 0x{ex.HResult:X8}).",
                ex
            );
        }
    }

    public IntPtr GetAncestorRoot(IntPtr hwnd) =>
        NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);

    public long GetWindowStyle(IntPtr hwnd) =>
        GetWindowLongPtrChecked(hwnd, NativeMethods.GWL_STYLE);

    public long GetWindowExStyle(IntPtr hwnd) =>
        GetWindowLongPtrChecked(hwnd, NativeMethods.GWL_EXSTYLE);

    public IntPtr GetWindowOwner(IntPtr hwnd)
    {
        // GWL_HWNDPARENT returns zero both for "no owner" (legitimate) and for
        // "invalid hwnd" (error). SetLastError before the call so we can tell
        // them apart. A zero style with GetLastError==0 means un-owned.
        Marshal.SetLastSystemError(0);
        var owner = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT);
        if (owner == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 0)
            {
                throw new Win32InteropException(
                    err,
                    $"{nameof(NativeMethods.GetWindowLongPtr)}(GWL_HWNDPARENT) failed (Win32 error {err})."
                );
            }
        }
        return owner;
    }

    public Rect GetWindowRect(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            throw NewWin32(nameof(NativeMethods.GetWindowRect));
        }
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public int GetProcessId(IntPtr hwnd)
    {
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (tid == 0)
        {
            throw NewWin32(nameof(NativeMethods.GetWindowThreadProcessId));
        }
        return checked((int)pid);
    }

    public long GetProcessStartTimeUtcTicks(int processId)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION is enough for creation time and
        // works across integrity levels (unlike PROCESS_QUERY_INFORMATION).
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            dwProcessId: checked((uint)processId)
        );
        if (handle == IntPtr.Zero)
        {
            throw NewWin32(nameof(NativeMethods.OpenProcess));
        }

        try
        {
            if (
                !NativeMethods.GetProcessTimes(
                    handle,
                    out NativeMethods.FILETIME creation,
                    out _,
                    out _,
                    out _
                )
            )
            {
                throw NewWin32(nameof(NativeMethods.GetProcessTimes));
            }

            // FILETIME is 100-nanosecond intervals since 1601-01-01 UTC —
            // the exact same unit DateTime.FromFileTimeUtc expects, and the
            // resulting DateTime's Ticks are our stable identity component.
            var raw = ((long)creation.dwHighDateTime << 32) | creation.dwLowDateTime;
            return DateTime.FromFileTimeUtc(raw).Ticks;
        }
        finally
        {
            // Best-effort close; CloseHandle failing on a handle we just opened
            // is not actionable by the caller and must not mask the real work.
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    public IntPtr MonitorFromWindow(IntPtr hwnd) =>
        NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

    public bool SetWindowPos(IntPtr hwnd, Rect rect, SetWindowPosFlags flags) =>
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            (uint)flags
        );

    public bool SetForegroundWindow(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    public bool ShowWindow(IntPtr hwnd, int nCmdShow) => NativeMethods.ShowWindow(hwnd, nCmdShow);

    public bool IsZoomed(IntPtr hwnd) => NativeMethods.IsZoomed(hwnd);

    public bool IsIconic(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);

    public Rect GetRestoredBounds(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
        {
            var wp = new NativeMethods.WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
            };
            if (NativeMethods.GetWindowPlacement(hwnd, ref wp))
            {
                return new Rect(
                    wp.rcNormalPosition.Left,
                    wp.rcNormalPosition.Top,
                    wp.rcNormalPosition.Right - wp.rcNormalPosition.Left,
                    wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top
                );
            }
            // GetWindowPlacement failed — fall through to GetWindowRect (will
            // be the iconic position, but better than throwing on a soft path).
        }
        return GetWindowRect(hwnd);
    }

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public uint GetCurrentThreadId() => NativeMethods.GetCurrentThreadId();

    public int GetLastErrorCode() => Marshal.GetLastPInvokeError();

    public uint GetWindowThreadProcessId(IntPtr hwnd, out int processId)
    {
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (tid == 0)
        {
            processId = 0;
            throw NewWin32(nameof(NativeMethods.GetWindowThreadProcessId));
        }
        processId = checked((int)pid);
        return tid;
    }

    public bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach) =>
        // Contract: returns bool, never throws. The caller (WindowController.BringToFront)
        // treats a failing detach as best-effort (per Microsoft docs, a redundant
        // detach can legitimately report failure) and a failing attach as a logged
        // warning that skips the foreground call. Throwing here would let a detach
        // failure in a `finally` block overwrite a real exception from the `try`.
        NativeMethods.AttachThreadInput(idAttach, idAttachTo, attach);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long GetWindowLongPtrChecked(IntPtr hwnd, int index)
    {
        // GetWindowLongPtr returning zero is ambiguous (zero is a valid style
        // value); distinguish via SetLastError/GetLastError.
        Marshal.SetLastSystemError(0);
        var value = NativeMethods.GetWindowLongPtr(hwnd, index);
        if (value == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 0)
            {
                throw new Win32InteropException(
                    err,
                    $"{nameof(NativeMethods.GetWindowLongPtr)}({IndexName(index)}) failed (Win32 error {err})."
                );
            }
        }
        return value.ToInt64();
    }

    private static string IndexName(int index) =>
        index switch
        {
            NativeMethods.GWL_STYLE => "GWL_STYLE",
            NativeMethods.GWL_EXSTYLE => "GWL_EXSTYLE",
            NativeMethods.GWL_HWNDPARENT => "GWL_HWNDPARENT",
            NativeMethods.GWL_ID => "GWL_ID",
            NativeMethods.GWL_USERDATA => "GWL_USERDATA",
            NativeMethods.GWL_WNDPROC => "GWL_WNDPROC",
            _ => index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static Win32InteropException NewWin32(string apiName)
    {
        var err = Marshal.GetLastWin32Error();
        // Defer to the Win32Exception message for a human-readable description,
        // but keep the numeric code on the typed exception for matching logic
        // (e.g. ERROR_ACCESS_DENIED in WindowController.Park).
        var message = new Win32Exception(err).Message;
        return new Win32InteropException(err, $"{apiName} failed: {message} (Win32 error {err}).");
    }
}
