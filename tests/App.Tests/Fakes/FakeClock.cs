using System;
using System.Diagnostics;
using App.Core.Time;

namespace App.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public long TimestampTicks { get; set; } = 1_000_000L;

    public void Advance(TimeSpan delta)
    {
        UtcNow = UtcNow.Add(delta);
        TimestampTicks += (long)(delta.TotalSeconds * Stopwatch.Frequency);
    }
}
