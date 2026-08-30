using System.ComponentModel;
using System.Runtime.InteropServices;
using App.Interop.Errors;

namespace App.Interop;

/// <summary>
/// Enumerates all attached monitors. Wraps <c>EnumDisplayMonitors</c> +
/// <c>GetMonitorInfoW</c> behind a managed-friendly snapshot type.
/// </summary>
/// <remarks>
/// <para>
/// This is the single point in the system that resolves device-name ↔
/// HMONITOR mappings. Callers that persist monitor identity (e.g.
/// <c>StageState.SavedWorkAreasByDevice</c>, <c>SnapshotFile.monitors[]</c>)
/// store device names; right before they need to call into a Win32 API
/// that takes an HMONITOR they re-enumerate via <see cref="EnumerateAll"/>
/// and look the HMONITOR up by device name with
/// <see cref="ResolveByDeviceName"/>.
/// </para>
/// <para>
/// <b>Threading.</b> The methods are thread-safe — they construct a fresh
/// list per call and do not mutate shared state. The native callbacks run
/// on the calling thread.
/// </para>
/// </remarks>
public class MonitorEnumerator
{
    /// <summary>
    /// Enumerates every active monitor in the order
    /// <c>EnumDisplayMonitors</c> returns them.
    /// </summary>
    /// <exception cref="Win32InteropException">
    /// <c>EnumDisplayMonitors</c> returned <see langword="false"/>.
    /// </exception>
    public virtual IReadOnlyList<MonitorDescriptor> EnumerateAll()
    {
        var collected = new List<IntPtr>();

        bool Callback(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            ref NativeMethods.RECT lprcMonitor,
            IntPtr dwData
        )
        {
            collected.Add(hMonitor);
            return true; // continue enumeration
        }

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32InteropException(
                err,
                $"EnumDisplayMonitors failed (Win32 error {err}).",
                new Win32Exception(err)
            );
        }

        var result = new List<MonitorDescriptor>(collected.Count);
        foreach (IntPtr h in collected)
        {
            if (TryResolve(h, out MonitorDescriptor descriptor))
            {
                result.Add(descriptor);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the descriptor whose <see cref="MonitorDescriptor.DeviceName"/>
    /// matches <paramref name="deviceName"/>, or <see langword="null"/> when
    /// no such monitor is currently attached.
    /// </summary>
    public virtual MonitorDescriptor? ResolveByDeviceName(string deviceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceName);
        foreach (MonitorDescriptor m in EnumerateAll())
        {
            if (string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the descriptor for the primary monitor, or
    /// <see langword="null"/> on a system with no displays attached
    /// (a state Windows itself rarely tolerates).
    /// </summary>
    public virtual MonitorDescriptor? GetPrimary()
    {
        foreach (MonitorDescriptor m in EnumerateAll())
        {
            if (m.IsPrimary)
            {
                return m;
            }
        }
        return null;
    }

    private static bool TryResolve(IntPtr hMonitor, out MonitorDescriptor descriptor)
    {
        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
        };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            descriptor = default;
            return false;
        }

        Rect full = new(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top
        );
        Rect work = new(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left,
            info.rcWork.Bottom - info.rcWork.Top
        );
        descriptor = new MonitorDescriptor(
            hMonitor,
            info.szDevice,
            full,
            work,
            (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0
        );
        return true;
    }
}
