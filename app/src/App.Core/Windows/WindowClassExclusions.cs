using System.Collections.Immutable;
using App.Core.Branding;

namespace App.Core.Windows;

/// <summary>
/// Compile-time list of Win32 window class names that are never eligible for
/// Stage management. Consumed by <c>WindowFilter</c> rule 5.
/// </summary>
/// <remarks>
/// The list mixes two categories:
/// <list type="number">
///   <item>
///     Shell / desktop / system windows (<c>Progman</c>, <c>WorkerW</c>,
///     <c>Shell_TrayWnd</c>, …) that are always present on a running session and
///     must never be parked, resized, or brought to front.
///   </item>
///   <item>
///     Wallpaper-related windows — classic Wallpaper Engine uses
///     <c>wallpaper_engine</c> exactly but newer builds emit class names that
///     begin with <c>WallpaperEngine</c> followed by version/locale suffixes. A
///     separate prefix list handles that case (<see cref="ClassNamePrefixes"/>).
///   </item>
/// </list>
///
/// <para>
/// Stagehand's own top-level windows (<see cref="BrandConstants.OverlayWindowClassName"/>,
/// <see cref="BrandConstants.SettingsWindowClassName"/>) are also listed here so
/// the enumerator never tries to manage its own UI. The class names are the
/// single source of truth in <see cref="BrandConstants"/>.
/// </para>
///
/// <para>
/// Lookups are case-insensitive via <see cref="StringComparer.OrdinalIgnoreCase"/>.
/// </para>
/// </remarks>
internal static class WindowClassExclusions
{
    /// <summary>
    /// Class names matched by exact (case-insensitive) equality.
    /// </summary>
    public static readonly ImmutableHashSet<string> ClassNames = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "Wallpaper",
        "wallpaper_engine",
        BrandConstants.OverlayWindowClassName,
        BrandConstants.SettingsWindowClassName
    );

    /// <summary>
    /// Class-name prefixes matched by <see cref="string.StartsWith(string, StringComparison)"/>
    /// with <see cref="StringComparison.OrdinalIgnoreCase"/>. Covers class-name
    /// families whose suffixes vary across versions/locales.
    /// </summary>
    /// <remarks>
    /// Plan 02 §Risks line 567: "Wallpaper Engine window classes vary across
    /// versions — match on a case-insensitive <c>StartsWith("WallpaperEngine")</c>
    /// rather than exact equality."
    /// </remarks>
    public static readonly ImmutableArray<string> ClassNamePrefixes = ["WallpaperEngine"];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="className"/> matches
    /// either the exact-equality set or any of the prefix patterns.
    /// </summary>
    public static bool IsExcluded(string className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        if (ClassNames.Contains(className))
        {
            return true;
        }

        foreach (var prefix in ClassNamePrefixes)
        {
            if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
