# Third-Party Notices

This file is maintained manually as part of Plan 01. Plan 05 replaces it with a CycloneDX-generated
SBOM derived directly from `Directory.Packages.props`, so the manual table here is a stop-gap.

Stagehand depends on the following third-party packages. Versions match the pins in
`Directory.Packages.props` at the time of writing.

| Package                                   | Version  | Licence          | Usage   |
| ----------------------------------------- | -------- | ---------------- | ------- |
| Microsoft.Extensions.Hosting              | 8.0.1    | MIT              | runtime |
| Microsoft.Extensions.DependencyInjection  | 8.0.1    | MIT              | runtime |
| Microsoft.Extensions.Logging              | 8.0.1    | MIT              | runtime |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3    | MIT              | runtime |
| Serilog                                   | 4.3.1    | Apache-2.0       | runtime |
| Serilog.Extensions.Logging                | 8.0.0    | Apache-2.0       | runtime |
| Serilog.Sinks.File                        | 6.0.0    | Apache-2.0       | runtime |
| Serilog.Sinks.Debug                       | 3.0.0    | Apache-2.0       | runtime |
| H.NotifyIcon.Wpf                          | 2.3.2    | MIT              | runtime |
| CommunityToolkit.Mvvm                     | 8.4.2    | MIT              | runtime |
| System.Collections.Immutable              | 8.0.0    | MIT              | runtime |
| xunit                                     | 2.9.3    | Apache-2.0 + MIT | test    |
| xunit.runner.visualstudio                 | 2.8.2    | MIT              | test    |
| Microsoft.NET.Test.Sdk                    | 17.14.1  | MIT              | test    |
| NSubstitute                               | 5.3.0    | BSD-3-Clause     | test    |
| FluentAssertions                          | 6.12.2   | Apache-2.0       | test    |
| coverlet.collector                        | 6.0.4    | MIT              | test    |
| prettier                                  | 3.8.3    | MIT              | tooling |
| csharpier                                 | 1.2.6    | MIT              | tooling |
| xamlstyler.console                        | 3.2501.8 | MIT              | tooling |

Licences must be reverified against each package's source of truth before any external distribution
of Stagehand binaries. The automated SBOM planned for Plan 05 replaces manual verification with a
reproducible build-time artefact.
