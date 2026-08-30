using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace App.Interop.Threading;

/// <summary>
/// Message-only Win32 window (HWND_MESSAGE-parented). Receives messages
/// posted/sent to its HWND through the calling thread's message pump and
/// raises <see cref="MessageReceived"/> for every message before the
/// default window procedure runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> The HWND is bound to the thread that constructed
/// it — Windows delivers messages to the thread that owns the window's
/// queue. Callers that want to receive <c>WM_DISPLAYCHANGE</c> /
/// <c>WM_HOTKEY</c> typically construct one of these on the UI thread,
/// where WPF's dispatcher is already pumping. Construction must happen on
/// a thread that runs a message loop; otherwise messages will queue
/// indefinitely.
/// </para>
/// <para>
/// <b>Lifetime.</b> <see cref="Dispose"/> calls <c>DestroyWindow</c>
/// followed by <c>UnregisterClass</c>. Dispose must run on the same
/// thread that created the window — Windows enforces this for
/// <c>DestroyWindow</c>.
/// </para>
/// <para>
/// <b>Class registration.</b> Each instance registers a unique window
/// class (suffixed with a per-instance GUID) so multiple
/// <see cref="MessageOnlyWindow"/> instances can coexist without
/// stomping each other's class registration.
/// </para>
/// </remarks>
[SuppressMessage(
    "Usage",
    "CA2216:Disposable types should declare finalizer",
    Justification = "DestroyWindow / UnregisterClass must run on the thread that created the window — a finalizer running on the GC thread would call them on the wrong thread (Windows refuses DestroyWindow from a non-creating thread). The OS reclaims the HWND on process exit if Dispose is missed."
)]
public sealed class MessageOnlyWindow : IDisposable
{
    // Holding the delegate as a field keeps the GC from collecting it while
    // user32 still holds a raw function pointer to it.
    private readonly NativeMethods.WndProcDelegate _wndProc;
    private readonly string _className;
    private readonly IntPtr _hInstance;
    private IntPtr _hwnd;
    private bool _disposed;

    /// <summary>
    /// Creates a new message-only window with a unique class name and
    /// returns once the HWND is realised.
    /// </summary>
    /// <param name="classNamePrefix">
    /// Human-readable prefix for the registered window class (visible in
    /// debuggers / Spy++). The constructor appends a unique suffix.
    /// </param>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>RegisterClassEx</c> or <c>CreateWindowEx</c> failed.
    /// </exception>
    public MessageOnlyWindow(string classNamePrefix = "App.Interop.MessageOnlyWindow")
    {
        ArgumentException.ThrowIfNullOrEmpty(classNamePrefix);

        _wndProc = WndProc;
        _className = classNamePrefix + "+" + Guid.NewGuid().ToString("N");
        _hInstance = Marshal.GetHINSTANCE(typeof(MessageOnlyWindow).Module);

        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = _wndProc,
            hInstance = _hInstance,
            lpszClassName = _className,
        };

        if (NativeMethods.RegisterClassEx(ref wc) == 0)
        {
            var err = Marshal.GetLastWin32Error();
            throw new App.Interop.Errors.Win32InteropException(
                err,
                $"RegisterClassEx failed for class '{_className}' (Win32 error {err})."
            );
        }

        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: _className,
            lpWindowName: null,
            dwStyle: 0,
            X: 0,
            Y: 0,
            nWidth: 0,
            nHeight: 0,
            hWndParent: NativeMethods.HWND_MESSAGE,
            hMenu: IntPtr.Zero,
            hInstance: _hInstance,
            lpParam: IntPtr.Zero
        );

        if (_hwnd == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            _ = NativeMethods.UnregisterClass(_className, _hInstance);
            throw new App.Interop.Errors.Win32InteropException(
                err,
                $"CreateWindowEx failed for class '{_className}' (Win32 error {err})."
            );
        }
    }

    /// <summary>The HWND of the underlying message-only window.</summary>
    public IntPtr Hwnd => _hwnd;

    /// <summary>
    /// Raised on the window's owning thread for every message delivered
    /// to <see cref="Hwnd"/>. Set <c>e.Handled = true</c> to suppress the
    /// default window procedure.
    /// </summary>
    public event EventHandler<MessageOnlyWindowMessageEventArgs>? MessageReceived;

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Dispose must be idempotent and never throw — log and continue. Win32 errors during teardown are logged via the caller's pipeline; here we just swallow."
    )]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            try
            {
                _ = NativeMethods.DestroyWindow(_hwnd);
            }
            catch
            {
                // intentional: tear-down best-effort
            }
            _hwnd = IntPtr.Zero;
        }

        try
        {
            _ = NativeMethods.UnregisterClass(_className, _hInstance);
        }
        catch
        {
            // intentional: tear-down best-effort
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A subscriber must not be allowed to crash the message pump or leave the window in an unfinalised state."
    )]
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        EventHandler<MessageOnlyWindowMessageEventArgs>? handler = MessageReceived;
        if (handler is not null)
        {
            var args = new MessageOnlyWindowMessageEventArgs(msg, wParam, lParam);
            try
            {
                handler.Invoke(this, args);
            }
            catch
            {
                // intentional: pump-protection
            }
            if (args.Handled)
            {
                return IntPtr.Zero;
            }
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}

/// <summary>
/// Payload for <see cref="MessageOnlyWindow.MessageReceived"/>. Set
/// <see cref="Handled"/> to suppress the default window procedure.
/// </summary>
public sealed class MessageOnlyWindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam)
    : EventArgs
{
    /// <summary>The Win32 message id.</summary>
    public uint Message { get; } = message;

    /// <summary>The Win32 wParam.</summary>
    public IntPtr WParam { get; } = wParam;

    /// <summary>The Win32 lParam.</summary>
    public IntPtr LParam { get; } = lParam;

    /// <summary>
    /// When <see langword="true"/>, <see cref="MessageOnlyWindow"/> skips
    /// the default window procedure and returns <c>0</c>.
    /// </summary>
    public bool Handled { get; set; }
}
