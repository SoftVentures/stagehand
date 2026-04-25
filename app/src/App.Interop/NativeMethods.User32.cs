using System.Runtime.InteropServices;
using System.Text;

namespace App.Interop;

internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // Constants
    // ---------------------------------------------------------------------

    // GetWindowLongPtr / SetWindowLongPtr indices
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowlongptrw"/>
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_HWNDPARENT = -8;
    internal const int GWL_ID = -12;
    internal const int GWL_USERDATA = -21;
    internal const int GWL_WNDPROC = -4;

    // Window styles (WS_*) — see
    // <see href="https://learn.microsoft.com/windows/win32/winmsg/window-styles"/>
    internal const uint WS_OVERLAPPED = 0x00000000;
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_MINIMIZE = 0x20000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_DISABLED = 0x08000000;
    internal const uint WS_CLIPSIBLINGS = 0x04000000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint WS_MAXIMIZE = 0x01000000;
    internal const uint WS_CAPTION = 0x00C00000;
    internal const uint WS_BORDER = 0x00800000;
    internal const uint WS_DLGFRAME = 0x00400000;
    internal const uint WS_VSCROLL = 0x00200000;
    internal const uint WS_HSCROLL = 0x00100000;
    internal const uint WS_SYSMENU = 0x00080000;
    internal const uint WS_THICKFRAME = 0x00040000;
    internal const uint WS_MINIMIZEBOX = 0x00020000;
    internal const uint WS_MAXIMIZEBOX = 0x00010000;

    // Extended window styles (WS_EX_*) — see
    // <see href="https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles"/>
    internal const uint WS_EX_DLGMODALFRAME = 0x00000001;
    internal const uint WS_EX_NOPARENTNOTIFY = 0x00000004;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_ACCEPTFILES = 0x00000010;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_MDICHILD = 0x00000040;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_WINDOWEDGE = 0x00000100;
    internal const uint WS_EX_CLIENTEDGE = 0x00000200;
    internal const uint WS_EX_CONTEXTHELP = 0x00000400;
    internal const uint WS_EX_RIGHT = 0x00001000;
    internal const uint WS_EX_CONTROLPARENT = 0x00010000;
    internal const uint WS_EX_STATICEDGE = 0x00020000;
    internal const uint WS_EX_APPWINDOW = 0x00040000;
    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // SetWindowPos flags (SWP_*) — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos"/>
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOREDRAW = 0x0008;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;
    internal const uint SWP_NOCOPYBITS = 0x0100;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_DEFERERASE = 0x2000;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;

    // Special HWND values for SetWindowPos's hWndInsertAfter parameter.
    internal static readonly IntPtr HWND_TOP = new(0);
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    // ShowWindow commands (SW_*) — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-showwindow"/>
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNORMAL = 1;
    internal const int SW_SHOWMINIMIZED = 2;
    internal const int SW_SHOWMAXIMIZED = 3;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_SHOW = 5;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_SHOWMINNOACTIVE = 7;
    internal const int SW_SHOWNA = 8;
    internal const int SW_RESTORE = 9;
    internal const int SW_SHOWDEFAULT = 10;
    internal const int SW_FORCEMINIMIZE = 11;

    // WinEvent hook flags — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook"/>
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const uint WINEVENT_INCONTEXT = 0x0004;

    // Selected WinEvent event ids (see winuser.h) — full list at
    // <see href="https://learn.microsoft.com/windows/win32/winauto/event-constants"/>
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_CREATE = 0x8000;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint EVENT_OBJECT_CLOAKED = 0x8017;
    internal const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    // GetAncestor flags — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getancestor"/>
    internal const uint GA_PARENT = 1;
    internal const uint GA_ROOT = 2;
    internal const uint GA_ROOTOWNER = 3;

    // SystemParametersInfo actions (SPI_*) — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow"/>
    internal const uint SPI_GETWORKAREA = 0x0030;
    internal const uint SPI_SETWORKAREA = 0x002F;

    // SystemParametersInfo fWinIni flags.
    internal const uint SPIF_NONE = 0x00;
    internal const uint SPIF_UPDATEINIFILE = 0x01;
    internal const uint SPIF_SENDCHANGE = 0x02;
    internal const uint SPIF_SENDWININICHANGE = SPIF_SENDCHANGE;

    // MONITORINFOF_* flags
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    // MonitorFromWindow dwFlags
    internal const uint MONITOR_DEFAULTTONULL = 0x00000000;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // ---------------------------------------------------------------------
    // Structs
    // ---------------------------------------------------------------------

    /// <summary>
    /// Native RECT, left/top/right/bottom semantics (not x/y/w/h).
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/windef/ns-windef-rect"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Native POINT.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/windef/ns-windef-point"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Extended monitor info with Unicode device name.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfoexw"/>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    // ---------------------------------------------------------------------
    // Delegates
    // ---------------------------------------------------------------------

    /// <summary>
    /// Callback invoked by <c>EnumWindows</c> / <c>EnumChildWindows</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nc-winuser-wndenumproc"/>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool WndEnumProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>
    /// Callback invoked by <c>EnumDisplayMonitors</c> for each HMONITOR.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nc-winuser-monitorenumproc"/>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref RECT lprcMonitor,
        IntPtr dwData
    );

    /// <summary>
    /// Callback invoked by <c>SetWinEventHook</c> when a subscribed event fires.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nc-winuser-wineventproc"/>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime
    );

    // ---------------------------------------------------------------------
    // Enumeration
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(WndEnumProc lpEnumFunc, IntPtr lParam);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumchildwindows"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumChildWindows(
        IntPtr hWndParent,
        WndEnumProc lpEnumFunc,
        IntPtr lParam
    );

    // ---------------------------------------------------------------------
    // Text / class name
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextw"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "GetWindowTextW"
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1838:Avoid StringBuilder parameters for P/Invokes",
        Justification = "StringBuilder is the standard managed buffer for OUT string Win32 APIs; the Plan 02 consumer pools instances."
    )]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextlengthw"/>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    internal static partial int GetWindowTextLength(IntPtr hWnd);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getclassnamew"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "GetClassNameW"
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1838:Avoid StringBuilder parameters for P/Invokes",
        Justification = "StringBuilder is the standard managed buffer for OUT string Win32 APIs; the Plan 02 consumer pools instances."
    )]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    // ---------------------------------------------------------------------
    // State queries
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iswindowvisible"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-isiconic"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iszoomed"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(IntPtr hWnd);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iswindow"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    // ---------------------------------------------------------------------
    // Window styles
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowlongptrw"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "GetWindowLongPtrW"
    )]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowlongptrw"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "SetWindowLongPtrW"
    )]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // ---------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator / §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrect"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getclientrect"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags
    );

    // ---------------------------------------------------------------------
    // Focus / foreground
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow"/>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    // consumer: plan 02 §WindowController
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-attachthreadinput"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(
        uint idAttach,
        uint idAttachTo,
        [MarshalAs(UnmanagedType.Bool)] bool fAttach
    );

    // ---------------------------------------------------------------------
    // Process / thread / hierarchy
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getancestor"/>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    // consumer: plan 02 §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getparent"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr GetParent(IntPtr hWnd);

    // ---------------------------------------------------------------------
    // Monitors / display topology
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WorkAreaManager / §WindowEnumerator
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfromwindow"/>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    // consumer: plan 02 §WorkAreaManager
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfromrect"/>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromRect(in RECT lprc, uint dwFlags);

    // consumer: plan 02 §WorkAreaManager
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData
    );

    // consumer: plan 02 §WorkAreaManager
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmonitorinfow"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "GetMonitorInfoW"
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    // consumer: plan 02 §SidebarOverlay (per-monitor DPI scaling)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getdpiforwindow"/>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr hwnd);

    // ---------------------------------------------------------------------
    // System parameters (work area)
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WorkAreaManager
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "SystemParametersInfoW"
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref RECT pvParam,
        uint fWinIni
    );

    // ---------------------------------------------------------------------
    // WinEvent hooks
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WinEventHook
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook"/>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags
    );

    // consumer: plan 02 §WinEventHook
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-unhookwinevent"/>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    // ---------------------------------------------------------------------
    // Window messages
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // Visibility / show-state
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowController.RestorePosition (UWP cloak recovery)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-showwindow"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ---------------------------------------------------------------------
    // Bitmap capture (PrintWindow / device contexts)
    // ---------------------------------------------------------------------

    // PrintWindow nFlags — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-printwindow"/>
    // PW_CLIENTONLY restricts capture to the client area; we want full window chrome.
    internal const uint PW_CLIENTONLY = 0x00000001;

    // PW_RENDERFULLCONTENT (Windows 8.1+) instructs DWM to also render
    // hardware-accelerated content (Chromium/WPF/WinUI). Without it those
    // surfaces capture as transparent or solid black.
    internal const uint PW_RENDERFULLCONTENT = 0x00000002;

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-printwindow"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getdc"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr GetDC(IntPtr hWnd);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-releasedc"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    // ---------------------------------------------------------------------
    // Resource counters (GDI / user objects) — harness diagnostic
    // ---------------------------------------------------------------------

    // GetGuiResources dwFlags — see
    // <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguiresources"/>
    internal const uint GR_GDIOBJECTS = 0;
    internal const uint GR_USEROBJECTS = 1;

    // consumer: plan 02 §S9 Harness (GDI/user-object soak test)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguiresources"/>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    // consumer: plan 02 §SingleInstance
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerwindowmessagew"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "RegisterWindowMessageW"
    )]
    internal static extern uint RegisterWindowMessage(
        [MarshalAs(UnmanagedType.LPWStr)] string lpString
    );

    // ---------------------------------------------------------------------
    // Thread message pump (WinEventHookThread)
    // ---------------------------------------------------------------------

    // Standard message ids used by the hook thread's manual pump.
    // <see href="https://learn.microsoft.com/windows/win32/winmsg/wm-quit"/>
    internal const uint WM_QUIT = 0x0012;

    // <see href="https://learn.microsoft.com/windows/win32/winmsg/wm-user"/>
    internal const uint WM_USER = 0x0400;

    /// <summary>
    /// Message payload used by <c>GetMessage</c> / <c>PeekMessage</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-msg"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    // consumer: plan 02 §WinEventHookThread
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmessagew"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "GetMessageW"
    )]
    internal static extern int GetMessage(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax
    );

    // PM_NOREMOVE: leave the message in the queue (we only call PeekMessage to
    // force the thread message queue into existence — the pump's GetMessage
    // loop is the real consumer).
    internal const uint PM_NOREMOVE = 0x0000;

    // consumer: plan 02 §WinEventHookThread (queue-creation guarantee)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-peekmessagew"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "PeekMessageW"
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax,
        uint wRemoveMsg
    );

    // consumer: plan 02 §WinEventHookThread
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-translatemessage"/>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(in MSG lpMsg);

    // consumer: plan 02 §WinEventHookThread
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-dispatchmessagew"/>
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern IntPtr DispatchMessage(in MSG lpMsg);

    // consumer: plan 02 §WinEventHookThread
    /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-postthreadmessagew"/>
    [DllImport(
        "user32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "PostThreadMessageW"
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(
        uint idThread,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam
    );
}
