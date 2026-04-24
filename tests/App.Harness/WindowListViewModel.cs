using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using App.Interop;
using App.Services.Windows;

namespace App.Harness;

/// <summary>
/// Thin view-model backing the harness's window-list ListBox. Plan 02 §S9.
/// </summary>
/// <remarks>
/// <para>
/// Not a full MVVM implementation — the harness is a manual test runner,
/// not a production surface. <see cref="ObservableCollection{T}"/> provides
/// the change-notification the ListBox needs; no <c>INotifyPropertyChanged</c>
/// scaffolding is required for the handful of scalar properties.
/// </para>
/// <para>
/// <see cref="ParkedOriginalBounds"/> caches the pre-park bounds keyed on the
/// HWND of the target window, so the "Restore" button can round-trip through
/// <see cref="IWindowController.RestorePosition"/> without having to re-query
/// the original geometry.
/// </para>
/// </remarks>
public sealed class WindowListViewModel(IManageableWindowService service)
{
    private readonly IManageableWindowService _service =
        service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>Current snapshot list — bound to the ListBox.</summary>
    public ObservableCollection<WindowSnapshot> Windows { get; } = [];

    /// <summary>
    /// Pre-park bounds cache keyed on HWND. Populated by the "Park" button,
    /// consumed by "Restore".
    /// </summary>
    public Dictionary<IntPtr, Rect> ParkedOriginalBounds { get; } = [];

    /// <summary>
    /// Re-enumerates manageable windows and replaces the
    /// <see cref="Windows"/> collection. Called by the "Refresh" button and
    /// by the WinEvent-hook-driven auto-refresh.
    /// </summary>
    public IReadOnlyList<WindowSnapshot> Refresh()
    {
        IReadOnlyList<WindowSnapshot> snapshots = _service.GetManageableWindows();
        Windows.Clear();
        foreach (WindowSnapshot snap in snapshots)
        {
            Windows.Add(snap);
        }
        return snapshots;
    }
}
