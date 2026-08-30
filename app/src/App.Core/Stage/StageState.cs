using System.Collections.Immutable;

namespace App.Core.Stage;

/// <summary>
/// Immutable snapshot of the complete Stage state at a point in time.
/// </summary>
/// <remarks>
/// <para>
/// All mutation in the Stage subsystem produces a new <see cref="StageState"/>
/// from a prior one; instances are never modified in place. Consumers (the UI
/// layer, controllers, tests) compare by value.
/// </para>
/// <para>
/// <b>Scene-centric.</b> Every parked or active window belongs to exactly
/// one <see cref="Scene"/> on exactly one monitor. The flat
/// <c>Parked</c>-list shape used in Plan 01's skeleton was replaced in Plan
/// 03 — a single-window scene serialises identically to a "flat" parked
/// window from the user's point of view, so backward compatibility with
/// the Plan-01 mental model holds.
/// </para>
/// </remarks>
/// <param name="Phase">Current lifecycle phase.</param>
/// <param name="ScenesByDevice">
/// Per-monitor scenes keyed by GDI device name. Within each list, scenes
/// are ordered by <see cref="Scene.CreatedAt"/> ascending. A monitor with no
/// scenes either has no manageable windows or is not yet enumerated.
/// </param>
/// <param name="ActiveSceneByDevice">
/// Per-monitor "active scene" mapping. Value is <see langword="null"/> when
/// the monitor has no active scene (the wallpaper area is fully visible).
/// When non-null, the value is always a <see cref="SceneId"/> present in
/// the corresponding <paramref name="ScenesByDevice"/> list — the controller
/// upholds this invariant; consumers may rely on it.
/// </param>
/// <param name="SavedWorkAreasByDevice">
/// Original pre-Stage work areas per monitor, keyed by GDI device name,
/// used to restore the desktop on <c>DisableAsync</c>.
/// </param>
/// <param name="ExcludedWindows">Windows explicitly excluded from Stage management.</param>
/// <param name="IsPaused">Whether the Stage lifecycle is temporarily paused.</param>
public sealed record StageState(
    StagePhase Phase,
    ImmutableDictionary<string, ImmutableList<Scene>> ScenesByDevice,
    ImmutableDictionary<string, SceneId?> ActiveSceneByDevice,
    ImmutableDictionary<string, SavedWorkArea> SavedWorkAreasByDevice,
    ImmutableList<WindowIdentity> ExcludedWindows,
    bool IsPaused
)
{
    /// <summary>The canonical empty state: Stage disabled, nothing parked, not paused.</summary>
    public static readonly StageState Empty = new(
        StagePhase.Disabled,
        ImmutableDictionary<string, ImmutableList<Scene>>.Empty,
        ImmutableDictionary<string, SceneId?>.Empty,
        ImmutableDictionary<string, SavedWorkArea>.Empty,
        [],
        IsPaused: false
    );
}
