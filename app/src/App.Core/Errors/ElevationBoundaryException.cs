namespace App.Core.Errors;

/// <summary>
/// Thrown when an unelevated Stagehand process attempts to control an elevated
/// (UAC "Run as administrator") window and Windows refuses the operation.
/// </summary>
/// <remarks>
/// The integrity-level boundary is enforced by the OS: unelevated processes
/// cannot send messages, move, resize, or otherwise manipulate windows owned by
/// elevated processes. Callers should surface this to the user and either skip
/// the window or prompt for elevation.
/// </remarks>
public sealed class ElevationBoundaryException : Exception
{
    /// <summary>The elevated window's handle, if known; otherwise <see cref="IntPtr.Zero"/>.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>Creates a new <see cref="ElevationBoundaryException"/> with a default message.</summary>
    public ElevationBoundaryException()
        : this(IntPtr.Zero, "Cannot control an elevated window from an unelevated process.") { }

    /// <summary>Creates a new <see cref="ElevationBoundaryException"/> with the given message.</summary>
    public ElevationBoundaryException(string message)
        : this(IntPtr.Zero, message) { }

    /// <summary>
    /// Creates a new <see cref="ElevationBoundaryException"/> for the given handle
    /// with the given message.
    /// </summary>
    public ElevationBoundaryException(IntPtr hwnd, string message)
        : base(message)
    {
        Hwnd = hwnd;
    }

    /// <summary>Creates a new <see cref="ElevationBoundaryException"/> wrapping an inner exception.</summary>
    public ElevationBoundaryException(string message, Exception innerException)
        : base(message, innerException)
    {
        Hwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Creates a new <see cref="ElevationBoundaryException"/> for the given handle
    /// with a message and inner exception.
    /// </summary>
    public ElevationBoundaryException(IntPtr hwnd, string message, Exception innerException)
        : base(message, innerException)
    {
        Hwnd = hwnd;
    }
}
