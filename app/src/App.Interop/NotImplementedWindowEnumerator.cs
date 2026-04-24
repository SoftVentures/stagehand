namespace App.Interop;

/// <summary>
/// Placeholder <see cref="IWindowEnumerator"/> used by Plan 01's composition
/// skeleton. Replaced with a real implementation in Plan 02.
/// </summary>
public sealed class NotImplementedWindowEnumerator : IWindowEnumerator
{
    /// <inheritdoc />
    public IReadOnlyList<WindowSnapshot> GetManageableWindows() =>
        throw new NotImplementedException("Implemented in Plan 02.");
}
