namespace App.Interop;

/// <summary>
/// Immutable snapshot of a top-level window's state at a point in time.
/// </summary>
/// <remarks>
/// The stable identity of a window is the pair
/// (<see cref="ProcessId"/>, <see cref="ProcessStartTimeUtcTicks"/>) — NOT the raw
/// <see cref="Hwnd"/>. Windows recycles HWND values aggressively, so any subsystem
/// that caches/compares windows across time must use the process identity pair to
/// guard against HWND reuse after a target process exits.
///
/// <para>
/// The filter-facing fields (<see cref="IsVisible"/>, <see cref="IsCloaked"/>,
/// <see cref="IsTopLevel"/>, <see cref="Style"/>, <see cref="ExStyle"/>,
/// <see cref="HasOwner"/>, <see cref="ProcessName"/>) carry the raw Win32 state the
/// <c>WindowFilter</c> needs to apply rules 1–6 without re-entering the native API.
/// The enumerator populates them once per enumeration pass; the filter is a pure
/// function of the snapshot.
/// </para>
/// </remarks>
/// <param name="Hwnd">Raw window handle. Valid only for the lifetime of this snapshot.</param>
/// <param name="Title">Window title text at the time of the snapshot.</param>
/// <param name="ClassName">Win32 window class name.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ProcessStartTimeUtcTicks">
/// Owning process start time in UTC ticks. Combined with <paramref name="ProcessId"/>
/// to form a stable identity that survives HWND reuse.
/// </param>
/// <param name="Bounds">Window bounds in physical pixels.</param>
/// <param name="Monitor">HMONITOR the window is currently associated with.</param>
/// <param name="IsVisible">
/// Result of <c>IsWindowVisible(hwnd)</c> at snapshot time — filter rule 1.
/// </param>
/// <param name="IsCloaked">
/// Result of <c>DwmGetWindowAttribute(DWMWA_CLOAKED)</c> — filter rule 2. True when
/// the window is cloaked (e.g. an unfocused UWP background window).
/// </param>
/// <param name="IsTopLevel">
/// <c>GetAncestor(hwnd, GA_ROOT) == hwnd</c> — filter rule 3. False for child/owned
/// windows that happen to appear in the top-level enumeration.
/// </param>
/// <param name="Style">Raw <c>WS_*</c> style bits from <c>GetWindowLong(GWL_STYLE)</c>.</param>
/// <param name="ExStyle">Raw <c>WS_EX_*</c> style bits from <c>GetWindowLong(GWL_EXSTYLE)</c>.</param>
/// <param name="HasOwner">
/// True when the window has an owner window (<c>GetWindow(hwnd, GW_OWNER)</c>
/// returned non-null). Used in filter rule 4 to exclude modeless owned dialogs.
/// </param>
/// <param name="ProcessName">
/// Executable name (case-insensitive, without extension) of the owning process.
/// Used by filter rule 6 for the user exclusion list.
/// </param>
public readonly record struct WindowSnapshot(
    IntPtr Hwnd,
    string Title,
    string ClassName,
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    Rect Bounds,
    IntPtr Monitor,
    bool IsVisible,
    bool IsCloaked,
    bool IsTopLevel,
    long Style,
    long ExStyle,
    bool HasOwner,
    string ProcessName
);
