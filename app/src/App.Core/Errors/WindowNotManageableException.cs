namespace App.Core.Errors;

/// <summary>
/// Thrown when a window cannot be enumerated, filtered, or controlled by Stagehand.
/// </summary>
/// <remarks>
/// Typical causes: cloaked UWP shell windows, desktop/shell system windows,
/// invisible tool windows, or handles that have become invalid between enumeration
/// and use. The <see cref="Hwnd"/> property carries the offending handle for
/// diagnostics; callers should not assume it is still valid.
/// </remarks>
public sealed class WindowNotManageableException : Exception
{
    /// <summary>The unmanageable window handle, if known; otherwise <see cref="IntPtr.Zero"/>.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>Creates a new <see cref="WindowNotManageableException"/> with a default message.</summary>
    public WindowNotManageableException()
        : this(IntPtr.Zero, "The target window is not manageable by Stagehand.") { }

    /// <summary>Creates a new <see cref="WindowNotManageableException"/> with the given message.</summary>
    public WindowNotManageableException(string message)
        : this(IntPtr.Zero, message) { }

    /// <summary>
    /// Creates a new <see cref="WindowNotManageableException"/> for the given handle
    /// with the given message.
    /// </summary>
    public WindowNotManageableException(IntPtr hwnd, string message)
        : base(message)
    {
        Hwnd = hwnd;
    }

    /// <summary>Creates a new <see cref="WindowNotManageableException"/> wrapping an inner exception.</summary>
    public WindowNotManageableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Hwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Creates a new <see cref="WindowNotManageableException"/> for the given handle
    /// with a message and inner exception.
    /// </summary>
    public WindowNotManageableException(IntPtr hwnd, string message, Exception innerException)
        : base(message, innerException)
    {
        Hwnd = hwnd;
    }
}
