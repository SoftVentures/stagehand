using System.Runtime.InteropServices;
using System.Text;

namespace App.Interop;

internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // Process access rights (subset used by WindowEnumerator for identity) — see
    // <see href="https://learn.microsoft.com/windows/win32/procthread/process-security-and-access-rights"/>
    // ---------------------------------------------------------------------
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_VM_READ = 0x0010;

    /// <summary>
    /// Flags for <c>QueryFullProcessImageName</c> dwFlags parameter.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-queryfullprocessimagenamew"/>
    internal const uint PROCESS_NAME_NATIVE = 0x00000001;

    /// <summary>
    /// Native FILETIME — 100-nanosecond intervals since 1601-01-01 UTC.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/minwinbase/ns-minwinbase-filetime"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    // consumer: plan 02 §SingleInstance / diagnostics
    /// <see href="https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocessid"/>
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentProcessId();

    // consumer: plan 02 §WindowEnumerator (stable identity)
    /// <see href="https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess"/>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId
    );

    // consumer: plan 02 §WindowEnumerator (stable identity)
    /// <see href="https://learn.microsoft.com/windows/win32/api/handleapi/nf-handleapi-closehandle"/>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);

    // consumer: plan 02 §WindowEnumerator (image path for rules engine)
    /// <see href="https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew"/>
    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "QueryFullProcessImageNameW"
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1838:Avoid StringBuilder parameters for P/Invokes",
        Justification = "StringBuilder is the standard managed buffer for OUT string Win32 APIs; the Plan 02 consumer pools instances."
    )]
    internal static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize
    );

    // consumer: plan 02 §WindowEnumerator (stable identity via start time)
    /// <see href="https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes"/>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        IntPtr hProcess,
        out FILETIME lpCreationTime,
        out FILETIME lpExitTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime
    );
}
