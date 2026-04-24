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
}
