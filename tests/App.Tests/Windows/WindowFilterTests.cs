using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Branding;
using App.Interop;
using App.Services.Settings;
using App.Services.Windows;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Windows;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class WindowFilterTests
{
    // Win32 style bits used by Rule 4.
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;

    /// <summary>
    /// Builds a <see cref="WindowSnapshot"/> that passes every filter rule by
    /// default. Individual tests override just the one field whose rule they
    /// exercise.
    /// </summary>
    private static WindowSnapshot ManageableSnapshot(
        string? title = "Notepad",
        string className = "Notepad",
        bool isVisible = true,
        bool isCloaked = false,
        bool isTopLevel = true,
        long style = 0,
        long exStyle = 0,
        bool hasOwner = false,
        string processName = "notepad",
        IntPtr? hwnd = null,
        int processId = 1234,
        long processStartTimeUtcTicks = 638000000000000000L
    ) =>
        new(
            Hwnd: hwnd ?? new IntPtr(0x1001),
            Title: title ?? string.Empty,
            ClassName: className,
            ProcessId: processId,
            ProcessStartTimeUtcTicks: processStartTimeUtcTicks,
            Bounds: new Rect(0, 0, 800, 600),
            Monitor: new IntPtr(1),
            IsVisible: isVisible,
            IsCloaked: isCloaked,
            IsTopLevel: isTopLevel,
            Style: style,
            ExStyle: exStyle,
            HasOwner: hasOwner,
            ProcessName: processName
        );

    private static ISettingsService SettingsWith(params string[] excludedApps)
    {
        ISettingsService svc = Substitute.For<ISettingsService>();
        ImmutableHashSet<string> excluded =
            excludedApps.Length == 0
                ? ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase)
                : ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, excludedApps);
        svc.Current.Returns(
            AppSettings.Defaults() with
            {
                Behavior = new BehaviorSettings(excluded),
            }
        );
        return svc;
    }

    private static WindowFilter NewFilter(ISettingsService? settings = null) =>
        new(settings ?? SettingsWith(), NullLogger<WindowFilter>.Instance);

    // ------------------------------------------------------------------
    // Rule 1 — IsWindowVisible
    // ------------------------------------------------------------------

    [Fact]
    public void Rule1_InvisibleWindow_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter.IsManageable(ManageableSnapshot(isVisible: false)).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Rule 2 — cloaked (DWMWA_CLOAKED)
    // ------------------------------------------------------------------

    [Fact]
    public void Rule2_CloakedUwpWindow_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(className: "ApplicationFrameWindow", isCloaked: true))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Rule2_NonCloakedUwpWindow_IsIncluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(className: "ApplicationFrameWindow", isCloaked: false))
            .Should()
            .BeTrue();
    }

    // ------------------------------------------------------------------
    // Rule 3 — GetAncestor(hwnd, GA_ROOT) == hwnd
    // ------------------------------------------------------------------

    [Fact]
    public void Rule3_OwnedDialog_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(isTopLevel: false, hasOwner: true))
            .Should()
            .BeFalse();
    }

    // ------------------------------------------------------------------
    // Rule 4 — non-empty title OR (WS_EX_APPWINDOW set OR (!WS_EX_TOOLWINDOW && !HasOwner))
    // ------------------------------------------------------------------

    [Fact]
    public void Rule4a_EmptyTitle_ToolWindow_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(title: string.Empty, exStyle: WS_EX_TOOLWINDOW))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Rule4b_EmptyTitle_AppWindow_IsIncluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(title: string.Empty, exStyle: WS_EX_APPWINDOW))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Rule4c_NonEmptyTitle_NoOwner_IsIncluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(title: "Real Window", hasOwner: false))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Rule4d_EmptyTitle_NoToolWindow_NoOwner_IsIncluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(title: string.Empty, exStyle: 0, hasOwner: false))
            .Should()
            .BeTrue();
    }

    // ------------------------------------------------------------------
    // Rule 5 — class-name exclusion list (one test per class name + prefix + our brand classes)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("Wallpaper")]
    [InlineData("wallpaper_engine")]
    public void Rule5_ShellAndWallpaperClassNames_AreExcluded(string className)
    {
        WindowFilter filter = NewFilter();

        filter.IsManageable(ManageableSnapshot(className: className)).Should().BeFalse();
    }

    [Theory]
    [InlineData("WallpaperEngine")]
    [InlineData("WallpaperEngine_42_en-US")]
    [InlineData("wallpaperengine.overlay")]
    public void Rule5_WallpaperEnginePrefix_IsExcluded(string className)
    {
        WindowFilter filter = NewFilter();

        filter.IsManageable(ManageableSnapshot(className: className)).Should().BeFalse();
    }

    [Fact]
    public void Rule5_OwnOverlayClass_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(className: BrandConstants.OverlayWindowClassName))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Rule5_OwnSettingsClass_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        filter
            .IsManageable(ManageableSnapshot(className: BrandConstants.SettingsWindowClassName))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Rule5_ClassNameComparison_IsCaseInsensitive()
    {
        WindowFilter filter = NewFilter();

        filter.IsManageable(ManageableSnapshot(className: "PROGMAN")).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Rule 6 — user-configured process/class name exclusion + reactivity to Changed.
    // ------------------------------------------------------------------

    [Fact]
    public void Rule6_ExcludedProcessName_IsExcluded()
    {
        WindowFilter filter = NewFilter(SettingsWith("BadApp"));

        filter.IsManageable(ManageableSnapshot(processName: "badapp")).Should().BeFalse();
    }

    [Fact]
    public void Rule6_ExcludedWindowClassName_IsExcluded()
    {
        WindowFilter filter = NewFilter(SettingsWith("CustomClass"));

        filter
            .IsManageable(ManageableSnapshot(className: "CustomClass", processName: "something"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Rule6_EmptyExclusionList_AdmitsOtherwiseManageableWindows()
    {
        WindowFilter filter = NewFilter(SettingsWith());

        filter.IsManageable(ManageableSnapshot()).Should().BeTrue();
    }

    [Fact]
    public void Rule6_ChangedEvent_InvalidatesCache()
    {
        ISettingsService settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Defaults());
        var filter = new WindowFilter(settings, NullLogger<WindowFilter>.Instance);

        // Baseline — no exclusions, window is manageable.
        filter.IsManageable(ManageableSnapshot(processName: "newtoxic")).Should().BeTrue();

        // Fire Changed with a new exclusion set.
        settings.Current.Returns(
            AppSettings.Defaults() with
            {
                Behavior = new BehaviorSettings(
                    ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "newtoxic")
                ),
            }
        );
        settings.Changed += Raise.Event<EventHandler<AppSettings>>(settings, settings.Current);

        filter.IsManageable(ManageableSnapshot(processName: "newtoxic")).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Combinations — rule ordering guarantees the "nearest failing rule" wins.
    // ------------------------------------------------------------------

    [Fact]
    public void Combination_Passes1Through4_FailsRule5_IsExcluded()
    {
        WindowFilter filter = NewFilter();

        // Visible, not cloaked, top-level, has a title → passes 1–4. Class name
        // is Shell_TrayWnd → rule 5 rejects.
        filter
            .IsManageable(ManageableSnapshot(className: "Shell_TrayWnd", title: "taskbar"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Combination_Passes1Through5_FailsRule6_IsExcluded()
    {
        WindowFilter filter = NewFilter(SettingsWith("notepad"));

        // Default ManageableSnapshot has processName=notepad and class=Notepad
        // (not in the system exclusion list) → passes 1–5, rule 6 rejects.
        filter.IsManageable(ManageableSnapshot()).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Stable identity — snapshot equality.
    // ------------------------------------------------------------------

    [Fact]
    public void StableIdentity_TwoSnapshotsWithSameFields_AreEqual()
    {
        WindowSnapshot a = ManageableSnapshot(
            hwnd: new IntPtr(0x2222),
            processId: 77,
            processStartTimeUtcTicks: 555L
        );
        WindowSnapshot b = ManageableSnapshot(
            hwnd: new IntPtr(0x2222),
            processId: 77,
            processStartTimeUtcTicks: 555L
        );

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ------------------------------------------------------------------
    // Dispose — unsubscribes so later Changed fires do not touch a disposed filter.
    // ------------------------------------------------------------------

    [Fact]
    public void Dispose_Unsubscribes_FromChangedEvent()
    {
        ISettingsService settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Defaults());
        var filter = new WindowFilter(settings, NullLogger<WindowFilter>.Instance);

        filter.Dispose();

        // Firing Changed after Dispose must NOT update the cache. We prove that
        // indirectly: add an exclusion and fire Changed; the snapshot that
        // names the excluded process should still be manageable because the
        // cache was frozen at construction time.
        settings.Current.Returns(
            AppSettings.Defaults() with
            {
                Behavior = new BehaviorSettings(
                    ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "frozen")
                ),
            }
        );
        settings.Changed += Raise.Event<EventHandler<AppSettings>>(settings, settings.Current);

        filter.IsManageable(ManageableSnapshot(processName: "frozen")).Should().BeTrue();
    }
}
