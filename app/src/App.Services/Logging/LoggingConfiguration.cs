using System.Globalization;
using System.IO;
using App.Core.Branding;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace App.Services.Logging;

/// <summary>
/// Extension for wiring Serilog into the host's <see cref="ILoggingBuilder"/>.
/// Writes rolling daily files (max 5 MiB, 7 kept) plus a Debug sink in DEBUG builds.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Clears existing providers, configures Serilog with file (+ Debug under
    /// <c>DEBUG</c>) sinks, and registers the resulting logger with the builder.
    /// </summary>
    /// <param name="builder">The host logging builder.</param>
    /// <param name="logDirectoryPath">Absolute directory for rolling log files.</param>
    public static ILoggingBuilder AddStagehandLogging(
        this ILoggingBuilder builder,
        string logDirectoryPath
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logDirectoryPath);

        const string template =
            "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        LoggerConfiguration cfg = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDirectoryPath, $"{BrandConstants.LogFilePrefix}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: template,
                formatProvider: CultureInfo.InvariantCulture
            );

#if DEBUG
        cfg = cfg.WriteTo.Debug(
            outputTemplate: template,
            formatProvider: CultureInfo.InvariantCulture
        );
#endif

        Serilog.Core.Logger serilog = cfg.CreateLogger();

        builder.ClearProviders();
        builder.AddSerilog(serilog, dispose: true);
        return builder;
    }
}
