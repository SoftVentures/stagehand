namespace App.Interop;

/// <summary>
/// P/Invoke surface for Win32 APIs consumed by Stagehand.
/// </summary>
/// <remarks>
/// <para>
/// Split across <c>NativeMethods.*.cs</c> files by module (user32, dwmapi, shcore,
/// kernel32). Internal on purpose: the native surface is not part of Stagehand's
/// public API. Consumers call the managed interfaces in this namespace instead.
/// </para>
/// <para>
/// Each signature carries a Microsoft Learn link and a <c>// consumer: plan 02 §…</c>
/// comment identifying the high-level subsystem that drives it.
/// </para>
/// </remarks>
internal static partial class NativeMethods { }
