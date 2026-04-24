using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Services.Settings;
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

        using var provider = services.BuildServiceProvider();

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
}
