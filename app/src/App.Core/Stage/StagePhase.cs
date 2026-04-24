namespace App.Core.Stage;

/// <summary>
/// Lifecycle phases of the Stage — the mode that parks background windows and
/// presents thumbnails in the sidebar.
/// </summary>
public enum StagePhase
{
    /// <summary>Stage is off; no windows are parked.</summary>
    Disabled,

    /// <summary>Stage is transitioning from <see cref="Disabled"/> to <see cref="Enabled"/>.</summary>
    Enabling,

    /// <summary>Stage is active; background windows are parked and thumbnails rendered.</summary>
    Enabled,

    /// <summary>Stage is transitioning from <see cref="Enabled"/> back to <see cref="Disabled"/>.</summary>
    Disabling,
}
