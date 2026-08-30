namespace App.Core.Stage;

/// <summary>
/// User-facing knob describing how aggressively the
/// <see cref="ISceneSwapExecutor"/> animates a scene swap. Plan 03 ships
/// the <c>InstantSceneSwapExecutor</c> which ignores the value (every
/// speed is "instant"); Plan 04 introduces the animated executor that
/// honours it.
/// </summary>
public enum AnimationSpeed
{
    /// <summary>No animation — windows snap to their target rectangles.</summary>
    Off,

    /// <summary>Fast animation (~120 ms).</summary>
    Fast,

    /// <summary>Default animation (~250 ms).</summary>
    Normal,

    /// <summary>Slow animation (~500 ms), useful for screencasts.</summary>
    Slow,
}
