using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace App.Services.Snapshot;

/// <summary>
/// On-disk representation of a Stage snapshot. Mirrors the JSON schema in
/// Plan 03 §Design.9. Top-level <c>schemaVersion</c> = 1 is independent
/// from <c>AppSettings.CurrentSchemaVersion</c> — the snapshot evolves on
/// its own cadence.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1002:Do not expose generic lists",
    Justification = "JSON DTO; List<T> is required for System.Text.Json deserialisation."
)]
[SuppressMessage(
    "Usage",
    "CA2227:Collection properties should be read only",
    Justification = "JSON DTO; setters are required for deserialisation."
)]
public sealed class SnapshotFile
{
    /// <summary>Snapshot schema version (currently 1).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>UTC timestamp the snapshot was written.</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>PID of the Stagehand process that wrote the snapshot.</summary>
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }

    /// <summary>Process start time in UTC ticks for stale-detection.</summary>
    [JsonPropertyName("processStartTimeUtcTicks")]
    public long ProcessStartTimeUtcTicks { get; set; }

    /// <summary>Per-monitor entries: device name + saved work area.</summary>
    [JsonPropertyName("monitors")]
    public List<SnapshotMonitor> Monitors { get; set; } = new();

    /// <summary>All scenes across all monitors.</summary>
    [JsonPropertyName("scenes")]
    public List<SnapshotScene> Scenes { get; set; } = new();
}

/// <summary>One monitor entry within a <see cref="SnapshotFile"/>.</summary>
public sealed class SnapshotMonitor
{
    /// <summary>GDI device name.</summary>
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Saved work area (pre-Stage).</summary>
    [JsonPropertyName("savedWorkArea")]
    public SnapshotRect SavedWorkArea { get; set; } = new();

    /// <summary>The active scene id at snapshot time, or null.</summary>
    [JsonPropertyName("activeSceneId")]
    public Guid? ActiveSceneId { get; set; }
}

/// <summary>One scene entry within a <see cref="SnapshotFile"/>.</summary>
[SuppressMessage(
    "Design",
    "CA1002:Do not expose generic lists",
    Justification = "JSON DTO; List<T> is required for System.Text.Json deserialisation."
)]
[SuppressMessage(
    "Usage",
    "CA2227:Collection properties should be read only",
    Justification = "JSON DTO; setters are required for deserialisation."
)]
public sealed class SnapshotScene
{
    /// <summary>Scene id (Guid form).</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Monitor device name the scene lives on.</summary>
    [JsonPropertyName("monitorDeviceName")]
    public string MonitorDeviceName { get; set; } = string.Empty;

    /// <summary>Title shown in the sidebar tile.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Identity of the primary window.</summary>
    [JsonPropertyName("primary")]
    public SnapshotIdentity Primary { get; set; } = new();

    /// <summary>Wall-clock creation timestamp.</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>All windows in the scene (≥1).</summary>
    [JsonPropertyName("windows")]
    public List<SnapshotWindow> Windows { get; set; } = new();
}

/// <summary>One window entry within a <see cref="SnapshotScene"/>.</summary>
public sealed class SnapshotWindow
{
    /// <summary>Stable identity.</summary>
    [JsonPropertyName("identity")]
    public SnapshotIdentity Identity { get; set; } = new();

    /// <summary>Pre-park bounds.</summary>
    [JsonPropertyName("originalBounds")]
    public SnapshotRect OriginalBounds { get; set; } = new();

    /// <summary>GDI device name of the window's original monitor.</summary>
    [JsonPropertyName("originalMonitor")]
    public string OriginalMonitor { get; set; } = string.Empty;

    /// <summary>Whether the window was elevated (never moved).</summary>
    [JsonPropertyName("isElevated")]
    public bool IsElevated { get; set; }
}

/// <summary>Window identity persisted form (HWND + process start time).</summary>
public sealed class SnapshotIdentity
{
    /// <summary>HWND value at write time. May be invalid by read time.</summary>
    [JsonPropertyName("hwnd")]
    public long Hwnd { get; set; }

    /// <summary>Owning process start time in UTC ticks.</summary>
    [JsonPropertyName("processStartTimeUtcTicks")]
    public long ProcessStartTimeUtcTicks { get; set; }

    /// <summary>Owning process id.</summary>
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }
}

/// <summary>Rectangle persisted form.</summary>
public sealed class SnapshotRect
{
    /// <summary>Left edge.</summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>Top edge.</summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>Width.</summary>
    [JsonPropertyName("w")]
    public int W { get; set; }

    /// <summary>Height.</summary>
    [JsonPropertyName("h")]
    public int H { get; set; }
}
