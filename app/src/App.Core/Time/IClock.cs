namespace App.Core.Time;

/// <summary>
/// Abstraction over system time, injected to keep time-dependent logic testable.
/// </summary>
public interface IClock
{
    /// <summary>Current wall-clock time in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// High-resolution monotonic timestamp, suitable for measuring elapsed
    /// intervals. Units match <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.
    /// </summary>
    long TimestampTicks { get; }
}
