namespace App.Interop.Errors;

/// <summary>
/// Thrown when a Win32 / native interop call fails unexpectedly.
/// </summary>
/// <remarks>
/// Use this when a P/Invoke returns an error that cannot be handled locally and
/// must surface to the caller with the original Win32 error code preserved for
/// diagnostics. A <see cref="Win32ErrorCode"/> of <c>0</c> indicates the code
/// was unavailable at throw time.
/// </remarks>
public sealed class Win32InteropException : Exception
{
    /// <summary>Win32 error code (from <c>GetLastError</c>) if known, otherwise <c>0</c>.</summary>
    public int Win32ErrorCode { get; }

    /// <summary>Creates a new <see cref="Win32InteropException"/> with a default message.</summary>
    public Win32InteropException()
        : this(0, "A Win32 interop call failed.") { }

    /// <summary>Creates a new <see cref="Win32InteropException"/> with the given message.</summary>
    public Win32InteropException(string message)
        : this(0, message) { }

    /// <summary>
    /// Creates a new <see cref="Win32InteropException"/> with the given Win32 error code
    /// and message.
    /// </summary>
    public Win32InteropException(int win32ErrorCode, string message)
        : base(message)
    {
        Win32ErrorCode = win32ErrorCode;
    }

    /// <summary>
    /// Creates a new <see cref="Win32InteropException"/> wrapping an inner exception.
    /// </summary>
    public Win32InteropException(string message, Exception innerException)
        : base(message, innerException)
    {
        Win32ErrorCode = 0;
    }

    /// <summary>
    /// Creates a new <see cref="Win32InteropException"/> with a Win32 error code and
    /// an inner exception.
    /// </summary>
    public Win32InteropException(int win32ErrorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        Win32ErrorCode = win32ErrorCode;
    }
}
