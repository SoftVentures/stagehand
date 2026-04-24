using System.Runtime.InteropServices;

namespace App.Interop;

internal static partial class NativeMethods
{
    /// <summary>
    /// Per-process DPI awareness. Passed to <c>SetProcessDpiAwareness</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/shellscalingapi/ne-shellscalingapi-process_dpi_awareness"/>
    internal enum PROCESS_DPI_AWARENESS
    {
        PROCESS_DPI_UNAWARE = 0,
        PROCESS_SYSTEM_DPI_AWARE = 1,
        PROCESS_PER_MONITOR_DPI_AWARE = 2,
    }

    /// <summary>
    /// DPI type selector for <c>GetDpiForMonitor</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/shellscalingapi/ne-shellscalingapi-monitor_dpi_type"/>
    internal enum MONITOR_DPI_TYPE
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
        MDT_DEFAULT = MDT_EFFECTIVE_DPI,
    }

    // consumer: plan 02 §App bootstrap (per-monitor DPI-v2 for WPF)
    /// <see href="https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-setprocessdpiawareness"/>
    [DllImport("shcore.dll", PreserveSig = false)]
    internal static extern void SetProcessDpiAwareness(PROCESS_DPI_AWARENESS value);

    // consumer: plan 02 §WorkAreaManager / §WindowController (monitor-accurate scaling)
    /// <see href="https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor"/>
    [DllImport("shcore.dll", PreserveSig = false)]
    internal static extern void GetDpiForMonitor(
        IntPtr hmonitor,
        MONITOR_DPI_TYPE dpiType,
        out uint dpiX,
        out uint dpiY
    );
}
