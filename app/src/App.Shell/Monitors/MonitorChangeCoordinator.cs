using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using Microsoft.Extensions.Logging;

namespace App.Shell.Monitors;

/// <summary>
/// On <see cref="DisplayChangeListener.DisplayChanged"/>, diffs the current
/// monitor topology against the previously-managed set and creates / disposes
/// overlays accordingly. Plan 03 §Design.8.
/// </summary>
/// <remarks>
/// <para>
/// Plan 03 ships a minimal version: it adjusts the overlay set but does not
/// re-home parked scenes between monitors when one disappears. Plan 04 owns
/// the full scene-rehoming logic.
/// </para>
/// </remarks>
public sealed class MonitorChangeCoordinator
{
    private static readonly Action<ILogger, int, int, Exception?> s_logChanged =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(12001, nameof(MonitorChangeCoordinator) + ".Changed"),
            "Monitor topology changed: {Added} added, {Removed} removed."
        );

    private readonly IStageController _stage;
    private readonly IStageOverlayHost _overlay;
    private readonly MonitorEnumerator _monitors;
    private readonly ILogger<MonitorChangeCoordinator> _log;

    /// <summary>Production constructor.</summary>
    public MonitorChangeCoordinator(
        IStageController stage,
        IStageOverlayHost overlay,
        MonitorEnumerator monitors,
        ILogger<MonitorChangeCoordinator> log
    )
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Reconciles the overlay host's <see cref="IStageOverlayHost.ManagedMonitors"/>
    /// set with the current monitor enumeration: disposes overlays for
    /// monitors that have detached, creates overlays for newly-attached ones.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Public instance API; testability and DI registration both benefit from instance form."
    )]
    public void Reconcile()
    {
        if (!_stage.IsEnabled)
        {
            return; // nothing to do — overlays only exist while enabled
        }

        IReadOnlyList<MonitorDescriptor> current = _monitors.EnumerateAll();
        var currentByHmon = new HashSet<IntPtr>(current.Select(m => m.Hmonitor));
        var managed = new HashSet<IntPtr>(_overlay.ManagedMonitors);

        var added = 0;
        var removed = 0;

        foreach (IntPtr h in managed)
        {
            if (!currentByHmon.Contains(h))
            {
                _overlay.DisposeForMonitor(h);
                removed++;
            }
        }

        foreach (MonitorDescriptor m in current)
        {
            if (!managed.Contains(m.Hmonitor))
            {
                _overlay.CreateForMonitor(m.Hmonitor);
                added++;
            }
        }

        if (added > 0 || removed > 0)
        {
            s_logChanged(_log, added, removed, null);
        }
    }
}
