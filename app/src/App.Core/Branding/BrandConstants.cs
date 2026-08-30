namespace App.Core.Branding;

/// <summary>
/// Single source of truth for every user-visible brand string. All runtime code
/// that would otherwise hard-code the product name or a derived identifier
/// should read from here instead.
/// </summary>
/// <remarks>
/// Rationale: the physical code structure (folders, namespaces, project files,
/// assembly names) is deliberately brand-neutral (<c>App.Core</c>,
/// <c>App.Shell</c>, …) so that a rename of the product never requires renaming
/// source files. The brand name lives here, plus in documentation (Markdown)
/// and in static configuration files (issue templates, SECURITY.md, license).
///
/// <para>
/// <b>If you rebrand, you change:</b>
/// </para>
/// <list type="bullet">
///   <item>The constants in this class.</item>
///   <item>The <c>ProductFolderName</c> — <b>plus</b> a one-time migration
///     step that renames <c>%APPDATA%\&lt;old&gt;</c> → <c>%APPDATA%\&lt;new&gt;</c>
///     and <c>%LOCALAPPDATA%\&lt;old&gt;</c> → <c>%LOCALAPPDATA%\&lt;new&gt;</c> so
///     existing users do not lose their settings and logs. That migration code
///     does not exist yet — add it in the same PR that changes this constant.</item>
///   <item>The <c>LogFilePrefix</c> — old log files keep their old names;
///     new files use the new prefix.</item>
///   <item>Prose references in Markdown docs (<c>README.md</c>, <c>STRUKTUR.md</c>,
///     <c>docs/</c>, <c>CONTRIBUTING.md</c>, <c>SECURITY.md</c>, etc.) via
///     grep-replace.</item>
///   <item>GitHub URLs in <c>.github/ISSUE_TEMPLATE/config.yml</c> and
///     <c>SECURITY.md</c>.</item>
/// </list>
///
/// <para>
/// <b>What does <i>not</i> change during a rebrand:</b>
/// </para>
/// <list type="bullet">
///   <item>Any C# namespace.</item>
///   <item>Any folder under <c>app/</c> or <c>tests/</c>.</item>
///   <item>Any <c>.csproj</c> file name.</item>
///   <item>The solution file (<c>App.sln</c>).</item>
///   <item>Any <c>using</c> directive anywhere in the code base.</item>
/// </list>
/// </remarks>
public static class BrandConstants
{
    /// <summary>Display name shown to users (tray tooltip, About dialog, window titles).</summary>
    public const string ProductName = "Stagehand";

    /// <summary>
    /// Folder name used under <c>%APPDATA%\</c> for roaming settings and
    /// <c>%LOCALAPPDATA%\</c> for non-roaming logs and caches. Changing this
    /// requires a migration step (see class docs).
    /// </summary>
    public const string ProductFolderName = "Stagehand";

    /// <summary>
    /// Prefix of the rolling log file names
    /// (e.g. <c>stagehand-2026-04-24.log</c>, <c>stagehand-2026-04-24_001.log</c>).
    /// </summary>
    public const string LogFilePrefix = "stagehand";

    /// <summary>Text shown in the tray tooltip on hover.</summary>
    public const string TrayTooltip = ProductName;

    /// <summary>Title of the "About" MessageBox.</summary>
    public const string AboutTitle = $"About {ProductName}";

    /// <summary>Subtitle / product positioning shown in the About dialog.</summary>
    public const string AboutDescription = "Stage Manager for Windows.";

    /// <summary>Title of the Settings window.</summary>
    public const string SettingsWindowTitle = $"{ProductName} — Settings";

    /// <summary>Title of the sidebar overlay window.</summary>
    public const string OverlayWindowTitle = $"{ProductName} — Overlay";

    /// <summary>
    /// Win32 window-class name used for Plan 02's sidebar overlay windows.
    /// Consumed by the window-filter exclusion list so our own overlays are
    /// never enumerated as managed windows.
    /// </summary>
    public const string OverlayWindowClassName = $"{ProductName}-Overlay";

    /// <summary>
    /// Win32 window-class name used for the Settings window so the window
    /// filter can exclude it from enumeration. The actual WPF window class
    /// name is set via a window-class registration in Plan 04; this string
    /// is the single source of truth.
    /// </summary>
    public const string SettingsWindowClassName = $"{ProductName}-Settings";

    /// <summary>
    /// Class-name prefix for hidden message-only windows the app uses
    /// internally (Plan 03 §S0). Suffixed per-instance with a GUID so
    /// multiple instances coexist. Visible in Spy++ and tooling that
    /// inspects window-class registrations.
    /// </summary>
    public const string MessageWindowClassPrefix = $"{ProductName}.MessageOnly";

    /// <summary>
    /// Class-name prefix for the hidden window that owns Win32
    /// <c>RegisterHotKey</c> registrations (Plan 03 §S8 HotkeyService).
    /// </summary>
    public const string HotkeyWindowClassPrefix = $"{ProductName}.Hotkeys";

    /// <summary>
    /// Class-name prefix for the hidden window that listens for
    /// <c>WM_DISPLAYCHANGE</c> (Plan 03 §S6 DisplayChangeListener).
    /// </summary>
    public const string DisplayChangeWindowClassPrefix = $"{ProductName}.DisplayChangeListener";
}
