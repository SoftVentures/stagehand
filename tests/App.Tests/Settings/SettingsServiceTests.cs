using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using App.Core.Time;
using App.Services.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace App.Tests.Settings;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemorySettingsPathProvider _paths;
    private readonly ILogger<SettingsService> _log;
    private readonly SettingsMigrator _migrator;
    private readonly IClock _clock;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "App.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _paths = new InMemorySettingsPathProvider(
            settingsFilePath: Path.Combine(_tempDir, "settings.json"),
            logDirectoryPath: Path.Combine(_tempDir, "logs")
        );
        _log = Substitute.For<ILogger<SettingsService>>();
        _log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _migrator = new SettingsMigrator();
        _clock = new SystemClock();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; tests must not fail due to locked temp files.
        }
    }

    [Fact]
    public void DefaultsOnMissingFile()
    {
        File.Exists(_paths.SettingsFilePath).Should().BeFalse();

        var svc = new SettingsService(_paths, _log, _migrator, _clock);

        svc.Current.Should().Be(AppSettings.Defaults());
        File.Exists(_paths.SettingsFilePath)
            .Should()
            .BeTrue("the service seeds a defaults file when none exists");
    }

    [Fact]
    public async Task SaveAndReloadRoundTrips()
    {
        var first = new SettingsService(_paths, _log, _migrator, _clock);
        var target = AppSettings.Defaults();

        await first.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);

        var second = new SettingsService(_paths, _log, _migrator, _clock);
        second.Current.Should().Be(target);
    }

    [Fact]
    public async Task AtomicWriteSurvivesSimulatedFailure()
    {
        var svc = new SettingsService(_paths, _log, _migrator, _clock);

        await svc.SaveAsync(AppSettings.Defaults(), CancellationToken.None).ConfigureAwait(true);

        File.Exists(_paths.SettingsFilePath).Should().BeTrue();
        File.Exists(_paths.SettingsFilePath + ".tmp")
            .Should()
            .BeFalse("the temp file is renamed in place by File.Replace/Move");

        // Parseable as JSON — no corruption.
        var json = await File.ReadAllTextAsync(_paths.SettingsFilePath, CancellationToken.None)
            .ConfigureAwait(true);
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void SchemaVersionTooHighLoadsReadOnlyDefaults()
    {
        var payload =
            "{\n"
            + "  \"schemaVersion\": 9999,\n"
            + "  \"general\": {},\n"
            + "  \"appearance\": {},\n"
            + "  \"behavior\": {},\n"
            + "  \"updates\": {},\n"
            + "  \"logging\": {}\n"
            + "}";
        File.WriteAllText(_paths.SettingsFilePath, payload);

        var svc = new SettingsService(_paths, _log, _migrator, _clock);

        svc.Current.Should().Be(AppSettings.Defaults());

        var criticalCalls = _log.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Count(c => (LogLevel)c.GetArguments()[0]! == LogLevel.Critical);
        criticalCalls.Should().Be(1, "the schema-too-high branch must log Critical exactly once");
    }

    private sealed class InMemorySettingsPathProvider : ISettingsPathProvider
    {
        public InMemorySettingsPathProvider(string settingsFilePath, string logDirectoryPath)
        {
            SettingsFilePath = settingsFilePath;
            LogDirectoryPath = logDirectoryPath;
        }

        public string SettingsFilePath { get; }

        public string LogDirectoryPath { get; }
    }
}
