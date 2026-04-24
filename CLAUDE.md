# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

**Stagehand** is a .NET 8 WPF desktop app for Windows — a clone of macOS Stage Manager. It parks windows as live DWM-thumbnail miniatures in a sidebar overlay, gives the active window the main stage, and adjusts the work area to make room.

**Current state (Plan 01 scaffolding complete):**

- Solution builds clean in Debug + Release (0 warnings under `TreatWarningsAsErrors`).
- 15 tests pass (`dotnet test`).
- App launches (`dotnet run`), tray icon appears, Settings window opens with a Plan-04 placeholder.
- **All feature logic is `NotImplementedException` stubs.** Window enumeration, thumbnails, stage toggle, hotkeys, updates, real settings UI — nothing yet. See `docs/plans/02-05*.md` for what each subsequent phase adds.

## Phase-driven workflow

The work is organised as five phase plans under `docs/plans/`. Do **not** invent features or refactor outside the current plan's scope; if something you want to change belongs to a later plan, leave it alone.

| Plan                                             | Scope                                                                         |
| ------------------------------------------------ | ----------------------------------------------------------------------------- |
| `01-architecture-contribution-security.md`       | Repo hygiene, solution skeleton, DI, stubs, governance (done)                 |
| `02-core-window-mechanics.md`                    | Window enumerator, filter, controller, DWM thumbnails, sidebar overlay        |
| `03-stage-lifecycle-interaction-multimonitor.md` | Enable/disable, swap, work-area, multi-monitor                                |
| `04-settings-ux-polish.md`                       | Real settings UI, hotkeys, Mica/Acrylic, welcome, animations, autostart       |
| `05-distribution-updates-robustness.md`          | Velopack updater, release pipeline, fullscreen/DRM/virtual-desktop edge cases |

Each plan has numbered Subtasks (§S1–§Sn) with files to touch, API signatures, tests, and a Claude Code prompt. Execute them in order.

## Commands

```bash
# Build + test + run
dotnet build App.sln                              # Debug
dotnet build App.sln -c Release                   # Release (TreatWarningsAsErrors)
dotnet test App.sln                               # All tests
dotnet test App.sln --filter FullyQualifiedName~StageStateTests   # Single test class
dotnet test App.sln --filter Name=Empty_IsDisabled_WithNoParkedWindows  # Single test
dotnet run --project app/src/App.Shell            # Launch the app (tray icon)

# Clean rebuild from scratch (when asset-copy, lock files, or caches misbehave)
rm -rf artifacts/ app/src/*/bin app/src/*/obj tests/*/bin tests/*/obj \
       app/src/App.Shell/Assets/TrayIcon*.ico
find . -name "packages.lock.json" -not -path "./node_modules/*" -delete
dotnet restore App.sln
dotnet build App.sln

# First-time setup on a new machine
npm ci                                            # Prettier devDependency
dotnet tool restore                               # csharpier + xamlstyler

# Four formatter gates (all must be green in CI; all have write and check modes)
npx prettier --check .                            # Markdown, YAML, JSON
dotnet csharpier check .                          # C#
dotnet xstyler --recursive --directory app --passive   # XAML
dotnet format App.sln --verify-no-changes --severity warn   # C# whitespace + csproj

# Apply formatter fixes
npx prettier --write .
dotnet csharpier format .
dotnet xstyler --recursive --directory app
dotnet format App.sln --severity warn

# Vulnerability scan (reporting only — CI uses continue-on-error)
dotnet list App.sln package --vulnerable --include-transitive
```

## Architecture (condensed)

Four source assemblies plus two test assemblies, strict one-way dependency direction **enforced by `<ProjectReference>` in `.csproj`**:

```
App.Shell  →  App.Services  →  App.Core  →  App.Interop
```

- **`App.Interop`** — P/Invoke signatures (`NativeMethods.{User32,Dwmapi,Shcore,Kernel32}.cs`), SafeHandles, interfaces (`IWindowEnumerator`, `IWindowController`, `IDwmThumbnail`, `IWinEventHook`, `IWorkAreaManager`). No logic; Plan 02 implements.
- **`App.Core`** — Pure domain: `StageState` record (immutable snapshot with per-device dictionaries), `StagePhase`, typed exceptions (`Win32InteropException`, `WindowNotManageableException`, `ElevationBoundaryException`), `IClock`/`SystemClock`, `BrandConstants`. No IO, no WPF, no Win32.
- **`App.Services`** — IO: real `SettingsService` (atomic write via `File.Replace`), `AppDataSettingsPathProvider`, `SettingsMigrator`, stub `HotkeyService`/`UpdateService`, `LoggingConfiguration` (Serilog rolling file).
- **`App.Shell`** — WPF: composition root (`App.xaml.cs`, class is **`ShellApp`** not `App`), `ServiceConfiguration` (DI wiring), `TrayIconHost`, placeholder `SettingsWindow`.
- **`App.Tests`** — xUnit + FluentAssertions + NSubstitute. Unit tests only; fakes for interop seams.
- **`App.Harness`** — WPF harness for manual exploration of real Win32/DWM behaviour. Not in CI.

For a longer tour (in German, aimed at stack-unfamiliar readers), see `STRUKTUR.md`. For permanent architecture reference, see `docs/architecture/overview.md` and `docs/architecture/threat-model.md`.

## Brand-neutral by design

Physical code structure (folders, projects, namespaces, solution file) uses the `App.*` prefix and **does not contain the product name**. User-visible strings live in one place: `app/src/App.Core/Branding/BrandConstants.cs`. A rebrand touches that file, the Markdown prose, and two GitHub URLs — not one `.csproj`, not one `using`, not one folder.

Do **not** hard-code `"Stagehand"`, `"%APPDATA%\\Stagehand"`, log-file prefixes, window titles, or about-dialog strings anywhere in code. Read from `BrandConstants`.

## Non-obvious gotchas

- **The Application class is called `ShellApp`, not `App`.** Inside `namespace App.Shell`, a class named `App` collides with the namespace segment in WPF's auto-generated `App.g.cs` (CS0426). Do not rename it back.
- **No `Program.cs` exists.** The WPF SDK auto-generates `Main` from `App.xaml`. Composition happens in `ShellApp.OnStartup`.
- **Brand icon `<Resource>` items must be declared inside the `CopyBrandAssets` MSBuild target**, not in the csproj at project-evaluation scope. A project-scope `<ItemGroup Condition="Exists(...)">` is evaluated once at project load, before the copy target runs — on a fresh clone the icons then never enter the WPF resource graph and the tray icon fails at runtime. See `app/build/copy-brand-assets.targets` for the working pattern.
- **CSharpier 1.x also formats XAML** — `.csharpierignore` excludes `*.xaml` so XAML Styler owns it. Don't remove that line.
- **Central Package Management is on.** Versions live in `Directory.Packages.props`. Do **not** add `<Version>` attributes on `<PackageReference>` in individual csprojs.
- **`packages.lock.json` is committed per project.** CI restores with `--locked-mode`. When you bump a package version, commit the updated lock files in the same change.
- **`ShutdownMode = OnExplicitShutdown`** in `App.xaml`. Closing the Settings window does not exit the app; only the tray "Exit" menu does. Do not close the app from anywhere except `TrayIconHost.Exit`.
- **Stubs throw `NotImplementedException("Implemented in Plan 0X.")` deliberately.** Do not "fix" them by returning `null` or fake data — the throw is a tripwire that tells you you're calling code whose behaviour the current plan does not cover.
- **`.editorconfig` globally disables CA1031, CA1303, CA1724.** Do not re-enable without discussion. Narrow `[SuppressMessage]` attributes with justification strings are acceptable for other analyzer false-positives.
- **Serilog + `LoggerMessage.Define` delegates.** Release's analyzer config enforces CA1848. Use cached `LoggerMessage.Define` delegates (see existing files for the pattern), not inline `_log.LogInformation(...)` calls.

## Version pins with rationale (see `NOTES.md` for details)

- `Microsoft.Extensions.*` pinned to 8.0.x (LTS line).
- `Serilog.Extensions.Logging` 8.0.0 — 9.x would drag in M.E.Logging 9.x.
- `H.NotifyIcon.Wpf` 2.3.2 — 2.4.x dropped the `net8.0-windows` TFM and trips NU1701 under `TreatWarningsAsErrors`.
- `FluentAssertions` 6.12.2 — 7.x/8.x relicensed commercial.
- GitHub Actions: `checkout@v6`, `setup-dotnet@v5`, `setup-node@v6`, `cache@v5`, `upload-artifact@v7` (all latest stable majors as of 2026-04-24).
- Node.js 24 (Krypton LTS).

## User-specific interaction rules (observed)

- **Never auto-commit.** The user reviews the working tree and commits manually.
- **Ask before destructive operations** (file deletes, branch force-pushes, discarding uncommitted state).
- **The user doesn't know .NET/C#/WPF deeply.** When explaining, match that level — use analogies (npm ↔ NuGet, Jest ↔ xUnit) and label unfamiliar concepts inline. `STRUKTUR.md` is written in that register and is a good source to mirror.
- Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`, `ci:`, `perf:`) — release tooling parses these.

## What NOT to do

- Do not add a feature from Plans 02–05 "because it's easy". Stubs exist for a reason; the plan order is load-bearing.
- Do not introduce new NuGet packages without updating `Directory.Packages.props` and the THIRD-PARTY-NOTICES entry.
- Do not break the layer direction (`App.Shell → App.Services → App.Core → App.Interop`). If a lower layer needs something from a higher layer, the design is wrong — add an interface to the lower layer and register an implementation from above.
- Do not touch user-data paths (`%APPDATA%\Stagehand\`, `%LOCALAPPDATA%\Stagehand\logs\`) literally. Go through `BrandConstants.ProductFolderName`.
- Do not silently swallow exceptions. `App.xaml.cs` has three global handlers; use them. For expected failures, prefer a typed exception (`Win32InteropException`, `ElevationBoundaryException`, `WindowNotManageableException`).

## Where to look first

| Question                   | File                                                                                   |
| -------------------------- | -------------------------------------------------------------------------------------- |
| How do services get wired? | `app/src/App.Shell/Composition/ServiceConfiguration.cs`                                |
| What happens at startup?   | `app/src/App.Shell/App.xaml.cs` (class `ShellApp`)                                     |
| Where are settings/logs?   | `app/src/App.Services/Settings/AppDataSettingsPathProvider.cs` + `BrandConstants`      |
| Which packages + why       | `Directory.Packages.props` + `NOTES.md`                                                |
| Why a formatter rule       | `.editorconfig`, `.prettierrc.json`, `.csharpierignore`                                |
| What a class should do     | The interface definition (every seam is `I…`) and the plan subtask (`docs/plans/…§S…`) |
| End-to-end verification    | `docs/plans/01-architecture-contribution-security.md` §Verification                    |
