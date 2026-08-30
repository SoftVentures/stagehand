#Requires -Version 7
<#
.SYNOPSIS
    Notfall-Restore für Stagehand-Crashes: holt off-screen geparkte Fenster
    zurück und setzt die Work Area aller Monitore auf den vollen Monitor-
    Bereich.
.DESCRIPTION
    Wird gebraucht wenn Stagehand (oder der Harness) abstürzt während
    Stage aktiv war: dann sind Fenster auf (-32000, -32000) geparkt und
    die Work Area der Monitore ist um 200 px links eingedellt. Dieses
    Skript fixt beides ohne Neustart.

    1. Enumeriert alle Top-Level-Fenster via EnumWindows.
    2. Verschiebt jedes Fenster mit X<=-30000 oder Y<=-30000 auf das
       primäre Monitor-Center (sichtbar, beibehaltene Größe).
    3. Setzt für jeden angeschlossenen Monitor SPI_SETWORKAREA auf den
       vollen Monitor-Bereich (= keine Sidebar-Reservation mehr).

    Idempotent — wenn nichts zu fixen ist, passiert nichts. Schreibt
    eine Übersicht nach stdout.

.NOTES
    Reines PowerShell + P/Invoke; kein Build nötig.
#>
$ErrorActionPreference = 'Stop'

Add-Type -Namespace 'StagehandRescue' -Name 'Native' -MemberDefinition @'
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, System.Text.StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEXW info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW", SetLastError = true)] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pv, uint fWinIni);
'@

$SPI_SETWORKAREA  = 0x002F
$SPIF_SENDCHANGE  = 0x02
$SWP_NOZORDER     = 0x0004
$SWP_NOACTIVATE   = 0x0010
$SWP_NOSIZE       = 0x0001

# 1. Alle Monitore einsammeln + Work Area auf full bounds setzen.
$monitors = New-Object 'System.Collections.Generic.List[object]'
$collect = [StagehandRescue.Native+MonitorEnumProc]{
    param([IntPtr]$h, [IntPtr]$hdc, [ref][StagehandRescue.Native+RECT]$rc, [IntPtr]$data)
    $info = [StagehandRescue.Native+MONITORINFOEXW]::new()
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([Type][StagehandRescue.Native+MONITORINFOEXW])
    if ([StagehandRescue.Native]::GetMonitorInfo($h, [ref]$info)) {
        $monitors.Add(@{ Name = $info.szDevice; Rect = $info.rcMonitor; Work = $info.rcWork })
    }
    return $true
}
[void][StagehandRescue.Native]::EnumDisplayMonitors([IntPtr]::Zero, [IntPtr]::Zero, $collect, [IntPtr]::Zero)

Write-Host "→ $($monitors.Count) Monitor(e) gefunden" -ForegroundColor Cyan
foreach ($m in $monitors) {
    $w = $m.Rect.Right - $m.Rect.Left
    $h = $m.Rect.Bottom - $m.Rect.Top
    $waW = $m.Work.Right - $m.Work.Left
    Write-Host "  $($m.Name): full=$($w)x$($h)  work=$($waW)x$($m.Work.Bottom - $m.Work.Top)"

    # Work Area auf den vollen Monitor-Bereich zurücksetzen.
    $rc = $m.Rect
    if (-not [StagehandRescue.Native]::SystemParametersInfo($SPI_SETWORKAREA, 0, [ref]$rc, $SPIF_SENDCHANGE)) {
        Write-Host "    SPI_SETWORKAREA fehlgeschlagen (Win32 $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))" -ForegroundColor Yellow
    } else {
        Write-Host "    Work Area zurückgesetzt" -ForegroundColor Green
    }
}

# 2. Alle Fenster prüfen, off-screen-geparkte zurückholen.
$primary = $monitors | Where-Object { $_.Rect.Left -eq 0 -and $_.Rect.Top -eq 0 } | Select-Object -First 1
if (-not $primary) { $primary = $monitors[0] }
$primaryWidth  = $primary.Rect.Right - $primary.Rect.Left
$primaryHeight = $primary.Rect.Bottom - $primary.Rect.Top

$rescued = 0
$enumerator = [StagehandRescue.Native+EnumWindowsProc]{
    param([IntPtr]$hwnd, [IntPtr]$l)
    if (-not [StagehandRescue.Native]::IsWindowVisible($hwnd)) { return $true }
    $rc = New-Object StagehandRescue.Native+RECT
    if (-not [StagehandRescue.Native]::GetWindowRect($hwnd, [ref]$rc)) { return $true }
    if ($rc.Left -le -30000 -or $rc.Top -le -30000) {
        $sb = New-Object System.Text.StringBuilder 256
        [void][StagehandRescue.Native]::GetWindowTextW($hwnd, $sb, 256)
        $title = $sb.ToString()
        $w = $rc.Right - $rc.Left
        $h = $rc.Bottom - $rc.Top
        if ($w -le 0) { $w = 800 }
        if ($h -le 0) { $h = 600 }
        # Mittig auf primary, Größe beibehalten.
        $x = [int](($primaryWidth - $w) / 2)
        $y = [int](($primaryHeight - $h) / 2)
        if ($x -lt 0) { $x = 0 }
        if ($y -lt 0) { $y = 0 }
        $flags = $SWP_NOZORDER -bor $SWP_NOACTIVATE
        if ([StagehandRescue.Native]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $w, $h, $flags)) {
            Write-Host "  HWND 0x$($hwnd.ToString('X')) '$title' → ($x, $y, ${w}x${h})" -ForegroundColor Green
            $script:rescued++
        }
    }
    return $true
}
Write-Host "→ Suche off-screen-geparkte Fenster…" -ForegroundColor Cyan
[void][StagehandRescue.Native]::EnumWindows($enumerator, [IntPtr]::Zero)

if ($rescued -eq 0) {
    Write-Host "✓ Keine geparkten Fenster gefunden — alles war schon ok." -ForegroundColor Green
} else {
    Write-Host "✓ $rescued Fenster zurückgeholt." -ForegroundColor Green
}
