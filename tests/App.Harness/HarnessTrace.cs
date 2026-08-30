using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace App.Harness;

/// <summary>
/// Synchronous file-trace helper for the harness. Writes one line per call
/// directly to <c>%TEMP%\stagehand-harness-trace.log</c> with immediate
/// flush — diagnoses crash paths where Serilog's file sink may not flush
/// before the dotnet host is torn down (Park / Resize / DWM access
/// violations, dispatcher reentrancy, etc.).
/// </summary>
/// <remarks>
/// Cleared on every harness startup (<see cref="Reset"/>) so each session
/// starts with a fresh trace. Production code uses Serilog; this is a
/// harness-only diagnostic that complements it.
/// </remarks>
internal static class HarnessTrace
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "stagehand-harness-trace.log"
    );

    /// <summary>
    /// Truncates the trace file. Call once at harness startup so the trace
    /// only contains data from the current session.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Diagnostic helper — never throws on IO failure."
    )]
    public static void Reset()
    {
        try
        {
            File.WriteAllText(Path, string.Empty);
        }
        catch
        {
            // deliberate swallow
        }
    }

    /// <summary>Appends one timestamped line and flushes immediately.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Diagnostic helper — never throws on IO failure."
    )]
    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    Path,
                    $"{DateTime.Now:HH:mm:ss.fff} [tid={Environment.CurrentManagedThreadId}] {line}{Environment.NewLine}"
                );
            }
        }
        catch
        {
            // deliberate swallow — diagnostics must not fail the harness.
        }
    }
}
