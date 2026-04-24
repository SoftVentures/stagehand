using System.Diagnostics;

namespace App.Core.Time;

/// <summary>
/// Default <see cref="IClock"/> implementation backed by
/// <see cref="DateTimeOffset.UtcNow"/> and <see cref="Stopwatch.GetTimestamp"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public long TimestampTicks => Stopwatch.GetTimestamp();
}
