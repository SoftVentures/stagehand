namespace App.Interop;

/// <summary>
/// Placeholder <see cref="IWinEventHook"/>. Subscribing to <see cref="EventRaised"/>
/// is allowed but no events will ever fire until Plan 02 lands.
/// </summary>
public sealed class NotImplementedWinEventHook : IWinEventHook
{
    /// <inheritdoc />
    public event EventHandler<WinEvent>? EventRaised
    {
        add
        { /* no-op until Plan 02 */
        }
        remove
        { /* no-op until Plan 02 */
        }
    }

    /// <inheritdoc />
    public void Start() => throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release in the placeholder.
    }
}
