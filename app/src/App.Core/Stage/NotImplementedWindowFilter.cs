namespace App.Core.Stage;

/// <summary>
/// Placeholder <see cref="IWindowFilter"/>. Every member throws
/// <see cref="NotImplementedException"/> until Plan 02 wires up the manageability
/// heuristics.
/// </summary>
public sealed class NotImplementedWindowFilter : IWindowFilter
{
    /// <inheritdoc />
    public bool IsManageable(App.Interop.WindowSnapshot snapshot) =>
        throw new NotImplementedException("Implemented in Plan 02.");
}
