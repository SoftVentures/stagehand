using System.Collections.Immutable;

namespace App.Core.Stage;

/// <summary>
/// A grouping of one or more windows that belong together on the same
/// monitor. Renders as a single sidebar tile when parked; presents all
/// member windows simultaneously when active.
/// </summary>
/// <remarks>
/// <para>
/// <b>Invariants enforced by the constructor.</b>
/// <list type="bullet">
///   <item><description><see cref="Windows"/> is never empty (an empty scene must be removed from <c>StageState</c> instead).</description></item>
///   <item><description><see cref="Primary"/>'s identity matches one of the entries in <see cref="Windows"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Mutability.</b> <see cref="Scene"/> is immutable; a scene's
/// membership changes by producing a new <see cref="Scene"/> via the
/// record's <c>with</c> expression and an updated <see cref="Windows"/>
/// list.
/// </para>
/// </remarks>
public sealed record Scene
{
    /// <summary>Stable id, persistent across snapshot round-trips.</summary>
    public SceneId Id { get; }

    /// <summary>
    /// Title shown in the sidebar tile. Plan 03 derives this from
    /// <see cref="Primary"/>'s title at scene creation; Plan 04 will allow
    /// users to override it.
    /// </summary>
    public string Title { get; }

    /// <summary>One or more parked-window descriptors, never empty.</summary>
    public ImmutableList<ParkedWindow> Windows { get; }

    /// <summary>
    /// The window whose thumbnail represents the scene in the sidebar and
    /// that ends up topmost when the scene is activated. Always a member of
    /// <see cref="Windows"/>.
    /// </summary>
    public WindowIdentity Primary { get; }

    /// <summary>Wall-clock creation timestamp, used for LRU ordering.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Process image name (e.g. <c>"msedge"</c>) of the scene's primary at
    /// creation time. Used by <see cref="App.Services.Stage.SceneGrouper"/>
    /// to group multi-process apps (Chromium-based browsers spawn one
    /// process per window — PID-only grouping creates duplicate tiles).
    /// Defaults to empty so existing test fixtures and snapshot restores
    /// — which don't carry the field — fall back to PID grouping.
    /// </summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>
    /// Constructs a scene with at least one member. Throws when the
    /// invariants above are violated.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any reference parameter is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="windows"/> is empty, or <paramref name="primary"/> is
    /// not present in <paramref name="windows"/>.
    /// </exception>
    public Scene(
        SceneId id,
        string title,
        ImmutableList<ParkedWindow> windows,
        WindowIdentity primary,
        DateTimeOffset createdAt
    )
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.IsEmpty)
        {
            throw new ArgumentException(
                "A scene must contain at least one window.",
                nameof(windows)
            );
        }
        var primaryFound = false;
        foreach (ParkedWindow w in windows)
        {
            if (w.Identity == primary)
            {
                primaryFound = true;
                break;
            }
        }
        if (!primaryFound)
        {
            throw new ArgumentException(
                "Primary window must be a member of the scene's Windows list.",
                nameof(primary)
            );
        }

        Id = id;
        Title = title;
        Windows = windows;
        Primary = primary;
        CreatedAt = createdAt;
    }
}
