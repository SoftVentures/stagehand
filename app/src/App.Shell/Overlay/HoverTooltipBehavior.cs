using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace App.Shell.Overlay;

/// <summary>
/// Plan 03 §S9 placeholder hover tooltip. Attaches to a
/// <see cref="SidebarOverlay"/> and shows a basic <see cref="ToolTip"/>
/// after a 500 ms dwell. Plan 04 polishes the visual (icon + title card +
/// fade animation).
/// </summary>
/// <remarks>
/// The tooltip text comes from the WPF window's <see cref="FrameworkElement.ToolTip"/>
/// property; for Plan 03 the SidebarOverlay does not yet plumb per-tile
/// tooltip text into the visual tree, so this behaviour activates only when
/// future code attaches a string. Kept here as the documented Plan-03 hook.
/// </remarks>
public sealed class HoverTooltipBehavior : IDisposable
{
    private readonly Window _owner;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    /// <summary>Attaches the behaviour to <paramref name="owner"/>.</summary>
    public HoverTooltipBehavior(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick += OnDwell;

        _owner.MouseMove += OnMouseMove;
        _owner.MouseLeave += OnMouseLeave;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _owner.MouseMove -= OnMouseMove;
        _owner.MouseLeave -= OnMouseLeave;
        _timer.Stop();
        _timer.Tick -= OnDwell;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _timer.Stop();
        _timer.Start();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _timer.Stop();
    }

    private void OnDwell(object? sender, EventArgs e)
    {
        _timer.Stop();
        // Plan 04 will plumb per-scene tooltip text and a properly-positioned
        // popup. For Plan 03 this hook is intentionally minimal.
    }
}
