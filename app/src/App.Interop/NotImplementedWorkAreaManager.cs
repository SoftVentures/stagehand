namespace App.Interop;

/// <summary>
/// Placeholder <see cref="IWorkAreaManager"/>. Replaced in Plan 02.
/// </summary>
public sealed class NotImplementedWorkAreaManager : IWorkAreaManager
{
    /// <inheritdoc />
    public Rect GetWorkArea(IntPtr monitorHandle) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void SetWorkArea(IntPtr monitorHandle, Rect area) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void RestoreAll() => throw new NotImplementedException("Implemented in Plan 02.");
}
