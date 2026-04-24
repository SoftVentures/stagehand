using System.Collections.Immutable;

namespace App.Core.Stage;

/// <summary>
/// Immutable snapshot of the complete Stage state at a point in time.
/// </summary>
/// <remarks>
/// All mutation in the Stage subsystem produces a new <see cref="StageState"/>
/// from a prior one; instances are never modified in place. Consumers (the UI
/// layer, controllers, tests) compare by value.
/// </remarks>
/// <param name="Phase">Current lifecycle phase.</param>
/// <param name="Parked">Windows currently parked by Stage.</param>
/// <param name="ActiveHwndByDevice">
/// The "active" (focused-on-stage) window per monitor, keyed by GDI device name.
/// Value is <see langword="null"/> when the monitor has no active window.
/// </param>
/// <param name="SavedWorkAreasByDevice">
/// Original pre-Stage work areas per monitor, keyed by GDI device name, used to
/// restore the desktop on <c>DisableAsync</c>.
/// </param>
/// <param name="ExcludedWindows">Windows explicitly excluded from Stage management.</param>
/// <param name="IsPaused">Whether the Stage lifecycle is temporarily paused.</param>
public sealed record StageState(
    StagePhase Phase,
    ImmutableList<ParkedWindow> Parked,
    ImmutableDictionary<string, IntPtr?> ActiveHwndByDevice,
    ImmutableDictionary<string, SavedWorkArea> SavedWorkAreasByDevice,
    ImmutableList<WindowIdentity> ExcludedWindows,
    bool IsPaused
)
{
    /// <summary>The canonical empty state: Stage disabled, nothing parked, not paused.</summary>
    public static readonly StageState Empty = new(
        StagePhase.Disabled,
        ImmutableList<ParkedWindow>.Empty,
        ImmutableDictionary<string, IntPtr?>.Empty,
        ImmutableDictionary<string, SavedWorkArea>.Empty,
        ImmutableList<WindowIdentity>.Empty,
        IsPaused: false
    );
}
