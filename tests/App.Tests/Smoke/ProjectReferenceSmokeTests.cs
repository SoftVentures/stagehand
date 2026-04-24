using System.Diagnostics.CodeAnalysis;
using App.Core.Layout;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Interop.Threading;
using App.Services.Settings;
using App.Services.Windows;
using App.Shell.Composition;
using App.Shell.Settings;
using App.Shell.Tray;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Smoke;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class ProjectReferenceSmokeTests
{
    [Fact]
    public void Core_Types_Are_Reachable()
    {
        typeof(StageState).FullName.Should().Contain("App.Core");
        typeof(IClock).Should().NotBeNull();
        typeof(StagePhase).IsEnum.Should().BeTrue();
    }

    [Fact]
    public void Interop_Types_Are_Reachable()
    {
        typeof(WindowSnapshot).FullName.Should().Contain("App.Interop");
        typeof(Rect).IsValueType.Should().BeTrue();
        typeof(IWindowEnumerator).Should().NotBeNull();
    }

    [Fact]
    public void Services_Types_Are_Reachable()
    {
        typeof(SettingsService).FullName.Should().Contain("App.Services");
        typeof(ISettingsPathProvider).Should().NotBeNull();
    }

    [Fact]
    public void DI_Resolves_Non_WPF_Services()
    {
        // AppDataSettingsPathProvider creates real %APPDATA%\Stagehand and
        // %LOCALAPPDATA%\Stagehand\logs directories on construction — accepted
        // side effect (happens once per dev machine anyway).
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsPathProvider, AppDataSettingsPathProvider>();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddLogging();
        services.ConfigureStagehand();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStageController>().Should().NotBeNull();
        provider.GetRequiredService<ISettingsService>().Should().NotBeNull();
        provider.GetRequiredService<IWindowEnumerator>().Should().NotBeNull();
        provider.GetRequiredService<IClock>().Should().NotBeNull();
    }

    [Fact]
    public void DI_Registers_WPF_Services()
    {
        // Do not resolve these — SettingsWindow construction requires the WPF
        // dispatcher. Assert they are registered.
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsPathProvider, AppDataSettingsPathProvider>();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddLogging();
        services.ConfigureStagehand();

        services.Should().Contain(s => s.ServiceType == typeof(TrayIconHost));
        services.Should().Contain(s => s.ServiceType == typeof(SettingsWindow));
    }

    // ---------------------------------------------------------------------
    // Plan 02 DI regression tests. Each of these concrete types is wired in
    // ServiceConfiguration as the live (non-stub) implementation — a future
    // refactor that accidentally swapped in a stub would flip StageController
    // (Plan 03) into NotImplemented mode at runtime before any unit test
    // caught it. Assert the wiring here.
    // ---------------------------------------------------------------------

    [Fact]
    public void DI_Resolves_Real_WindowEnumerator()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IWindowEnumerator>().Should().BeOfType<WindowEnumerator>();
    }

    [Fact]
    public void DI_Resolves_Real_WindowController()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IWindowController>().Should().BeOfType<WindowController>();
    }

    [Fact]
    public void DI_Resolves_Real_WindowFilter()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IWindowFilter>().Should().BeOfType<WindowFilter>();
    }

    [Fact]
    public void DI_Resolves_Real_ManageableWindowService()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IManageableWindowService>()
            .Should()
            .BeOfType<ManageableWindowService>();
    }

    [Fact]
    public void DI_Resolves_Real_DwmThumbnailFactory()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IDwmThumbnailFactory>().Should().BeOfType<DwmThumbnailFactory>();
    }

    [Fact]
    public void DI_Resolves_Real_ThumbnailLayoutEngine()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IThumbnailLayoutEngine>().Should().BeOfType<ThumbnailLayoutEngine>();
    }

    [Fact]
    public void DI_Resolves_Real_WinEventHookFactory()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        sp.GetRequiredService<IWinEventHookFactory>().Should().BeOfType<WinEventHookFactory>();
    }

    [Fact]
    public void DI_Resolves_Real_WinEventHookThread()
    {
        using ServiceProvider sp = BuildMinimalProvider();
        // WinEventHookThread has no interface (see its remarks); we assert it
        // is registered as itself and resolves to a non-null concrete instance.
        sp.GetRequiredService<WinEventHookThread>().Should().NotBeNull();
    }

    /// <summary>
    /// Produces a <see cref="ServiceProvider"/> with the same ConfigureStagehand
    /// wiring used at runtime, minus types that require a WPF dispatcher. The
    /// <see cref="UiDispatcher"/> registration reaches into
    /// <c>Application.Current.Dispatcher</c> which is null in a unit test;
    /// callers that resolve WindowController (which depends on UiDispatcher)
    /// would hit that. Strip UiDispatcher and re-register a dispatcher-free
    /// substitute bound to a scratch STA dispatcher.
    /// </summary>
    private static ServiceProvider BuildMinimalProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsPathProvider, AppDataSettingsPathProvider>();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddLogging();
        services.ConfigureStagehand();

        // Replace the default UiDispatcher factory — which reads
        // System.Windows.Application.Current.Dispatcher and returns null in
        // non-WPF contexts — with one that uses the current thread's
        // dispatcher. Sufficient for resolution; no Invoke calls happen here.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(UiDispatcher))
            {
                services.RemoveAt(i);
            }
        }
        services.AddSingleton<UiDispatcher>(_ => new UiDispatcher(
            System.Windows.Threading.Dispatcher.CurrentDispatcher
        ));

        return services.BuildServiceProvider();
    }
}
