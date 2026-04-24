# Stagehand Architecture Overview

This document is the permanent architectural reference for Stagehand. The original rationale and
trade-off discussion lives in the execution brief
[`docs/plans/01-architecture-contribution-security.md`](../plans/01-architecture-contribution-security.md);
feature-level execution briefs that build on this foundation are
[`docs/plans/02-core-window-mechanics.md`](../plans/02-core-window-mechanics.md),
[`docs/plans/03-stage-lifecycle-interaction-multimonitor.md`](../plans/03-stage-lifecycle-interaction-multimonitor.md),
[`docs/plans/04-settings-ux-polish.md`](../plans/04-settings-ux-polish.md), and
[`docs/plans/05-distribution-updates-robustness.md`](../plans/05-distribution-updates-robustness.md).

## 1. System context

Stagehand is a single-process WPF application. It runs at the signed-in user's privilege level,
installs no kernel driver and no Windows service, and does not elevate. All window management is
performed through public Win32 and DWM APIs that the user's session already exposes by virtue of
owning the desktop.

The runtime footprint is three threads of interest: the WPF dispatcher (the UI thread), a dedicated
STA thread that hosts `SetWinEventHook` callbacks without starving the dispatcher, and background
workers spawned via `Task.Run` for disk, HTTP, and log I/O. Everything that touches an HWND or a DWM
thumbnail lands on the UI thread; everything slow is pushed off it.

Because Stagehand holds no special privileges and opens no listening sockets, its external attack
surface is limited to the update channel documented in [`threat-model.md`](./threat-model.md).

## 2. Module architecture

The solution is four runtime assemblies and two test projects:

| Project                | Purpose                                                                                                           |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `app/src/App.Interop`  | Raw P/Invoke and thin safe wrappers. No business logic.                                                           |
| `app/src/App.Core`     | Pure business logic — state, layout, filtering rules, stage controller. No P/Invoke; consumes Interop interfaces. |
| `app/src/App.Services` | Settings persistence, global hotkey, updater, logging bootstrap.                                                  |
| `app/src/App.Shell`    | WPF shell: tray icon, overlay host, settings window, first-run, DI composition root.                              |
| `tests/App.Tests`      | xUnit + FluentAssertions + NSubstitute unit tests for Core and Services.                                          |
| `tests/App.Harness`    | Minimal WPF harness that exercises Interop against the real desktop. Not shipped.                                 |

Dependency direction is one-way: `App → Services → Core → Interop`. The direction is enforced by
project references; reverse edges fail `dotnet build`. In particular, `App.Core` never
references `System.Windows.*` or `user32.dll`; when Core needs window information it takes an
`IWindowEnumerator` or a `WindowSnapshot` value.

Tests reach Interop only via the native-seam interfaces (`INativeWindowApi`, `INativeDwmApi`,
`INativeCompositionApi`), which are `internal` in Interop with `InternalsVisibleTo` for the test
project. Production-path Core and Services tests substitute the public interfaces with NSubstitute
fakes rather than taking a direct reference to Interop.

## 3. P/Invoke layer conventions

All `[DllImport]` and `[LibraryImport]` declarations live in `App.Interop/NativeMethods.*.cs`.
No other file declares native entry points. The `NativeMethods` class is split by subsystem into
partial files (`NativeMethods.User32.cs`, `NativeMethods.Dwmapi.cs`, `NativeMethods.Shcore.cs`) and
merged at build time. Signatures that do not yet have a safe wrapper still live here so there is
exactly one place to find them, annotated with a `// used by: plan NN §X` comment.

`LibraryImport` (source-generated marshalling) is preferred over `DllImport` wherever .NET 8
supports the signature; `DllImport` remains the fallback. `SetLastError = true` is set whenever the
underlying API documents a last-error code. Blittable types — `IntPtr` for HWND/HMONITOR,
`[StructLayout(LayoutKind.Sequential)]` structs — are used wherever possible.

Resources with deterministic lifetimes are wrapped in `SafeHandle` subclasses
(`DwmThumbnailSafeHandle`, `WinEventHookSafeHandle`); raw pointers are never stored in managed state
beyond the method that produced them. Win32 failures are translated into typed exceptions at the
wrapper boundary (see §7), so callers never see `Marshal.GetLastWin32Error()`. Every native wrapper
has a counterpart integration exercise in the Harness project.

## 4. Threading and dispatching model

The threading rules are enforced by code review and by debug-only runtime asserts.

1. All HWND manipulations (`SetWindowPos`, `ShowWindow`, `SetForegroundWindow`) happen on the UI
   thread; wrappers call `Application.Current.Dispatcher.VerifyAccess()` in Debug.
2. DWM thumbnail register, update, and unregister calls happen on the UI thread under the same
   assertion.
3. `SetWinEventHook` is installed on a dedicated STA thread (`WinEventHookThread`) with its own
   message loop. Callbacks marshal onto the UI thread via `Dispatcher.InvokeAsync` before any state
   change.
4. Blocking I/O — settings read/write, updater HTTP, log rotation — runs inside `Task.Run` and never
   on the UI thread.
5. Any UI-thread callback must return within 16 ms. Longer work is offloaded.
6. `Thread.Sleep`, `Task.Wait`, and `.Result` are forbidden on the UI thread.

Two helpers support the rules from `App.Shell/Threading/`: `UiDispatcher`, a mockable facade
over `Application.Current.Dispatcher` with `RunAsync`, `Post`, and `AssertOnUiThread`; and
`WinEventHookThread`, which encapsulates the STA hook thread, starts lazily on first hook install,
and disposes on app exit.

## 5. Dependency injection and composition

Composition uses `Microsoft.Extensions.DependencyInjection` with `Microsoft.Extensions.Hosting`
integration. The composition root is `App.OnStartup` in `App.Shell/App.xaml.cs`; it delegates
to a static `ServiceConfiguration.ConfigureServices(IServiceCollection)` so `App.xaml.cs` itself
stays under ~50 lines.

Most services are registered as singletons: the Interop facades (`IWindowEnumerator`,
`IWindowController`, `IWorkAreaManager`, `IWinEventHookFactory`, `IDwmThumbnailFactory`), the
Services layer (`ISettingsService`, `IHotkeyService`, `IUpdateService`), the Core stage types
(`IStageController`, `IStageLayoutEngine`, `IWindowFilter`), the `UiDispatcher`, the `TrayIconHost`,
and the `IClock` (`SystemClock` by default, `FakeClock` in tests). `SettingsWindow` is transient,
resolved per open. `ILogger<T>` comes from the framework, backed by the Serilog sink described in
§8.

Interfaces for subsystems that are not yet implemented are registered with throw-only stub
implementations (`NotImplementedWindowEnumerator` and siblings). Plans 02–05 replace each stub with
a real implementation without changing the registration site.

## 6. State model

The stage's runtime state is the immutable record `StageState`. The live stage holds exactly one
current `StageState` inside `StageController`; every transition returns a new value.

```csharp
public enum StagePhase { Disabled, Enabling, Enabled, Disabling, Paused }

public sealed record StageState(
    StagePhase Phase,
    ImmutableList<ParkedWindow> Parked,
    ImmutableDictionary<string, IntPtr?> ActiveHwndByDevice,
    ImmutableDictionary<string, SavedWorkArea> SavedWorkAreasByDevice,
    ImmutableList<WindowIdentity> ElevatedPresent,
    bool IsPaused);
```

Per-monitor lookups are keyed on the device name from `MONITORINFOEXW.szDevice` (e.g.
`\\.\DISPLAY1`), never on `HMONITOR`, because monitor handles are not stable across hot-plug,
sleep/wake, or Explorer restart. `HMONITOR` values appear only as ephemeral locals within a single
call.

The transition graph is `Disabled → Enabling → Enabled`, with failure in Enabling rolling back to
Disabled, and `Enabled → Disabling → Disabled`. `EnableAsync` and `DisableAsync` serialise through
an async lock: a concurrent call while in Enabling or Disabling awaits the current transition. Tray
clicks, hotkeys, and foreground swaps funnel through the same lock. The detailed transition and
rollback choreography is implemented in Plan 03; this layer declares the types and interfaces.

## 7. Error handling conventions

Typed exceptions live in `App.Core/Errors/`: `Win32InteropException(string apiName,
int errorCode)`, `WindowNotManageableException(IntPtr hwnd, string reason)`,
`ElevationBoundaryException(IntPtr hwnd)`, and
`StageTransitionException(StagePhase from, StagePhase to, Exception inner)`. Interop wrappers throw
the first; stage transitions and filters throw the others.

Three top-level handlers are installed in `App.xaml.cs`:
`AppDomain.CurrentDomain.UnhandledException`, `Application.Current.DispatcherUnhandledException`,
and `TaskScheduler.UnobservedTaskException`. Each handler logs the full exception at `Critical`,
calls `StageController.DisableAsync` with a two-second timeout so the desktop returns to a usable
state, and surfaces a tray notification pointing at the log folder. Debug builds rethrow; release
builds set `e.Handled = true` where possible and keep the tray running so the user can reach
diagnostics.

The guiding principle is "leave the desktop usable before notifying". Users must never be stranded
with windows parked off-screen because the manager crashed.

## 8. Logging and diagnostics

Logging flows through `Microsoft.Extensions.Logging`, sunk by Serilog via
`Serilog.Extensions.Logging`. The primary output is a rolling file sink at
`%LOCALAPPDATA%\Stagehand\logs\stagemanager-.log` with a seven-day retention and a 5 MB per-file
cap. Debug builds additionally log to the debugger output.

Each class takes an `ILogger<T>`. The default minimum level is `Information`; per-category
overrides are configured in `settings.json` under `logging.levels`.

`Program.Main` parses three command-line flags: `--verbose` lowers the minimum level to `Debug`;
`--dry-run` suppresses Win32 state changes by injecting a `DryRunWindowController` decorator and
prefixing affected log lines with `[DRY-RUN]`; `--diagnostics` opens the log folder in Explorer
and exits. The tray context menu always includes an **Open Diagnostics** entry that opens the same
folder.

## 9. Testing strategy

Three tiers cover the product.

- **Unit tests** (`tests/App.Tests`) use xUnit, FluentAssertions, and NSubstitute. They cover
  Core and Services with fakes for every Interop interface. `coverlet.collector` reports coverage;
  the initial target is ≥80 % of Core logic lines, reported rather than gated.
- **Harness** (`tests/App.Harness`) is a WPF application — not a test project — launched by
  hand. It enumerates real windows, registers real DWM thumbnails, and exercises park/restore
  against Notepad. Each run emits a written checklist file.
- **Manual checklists** live under `docs/manual-tests/`, authored as each plan lands. The
  convention is captured in [`docs/manual-tests/README.md`](../manual-tests/README.md).

Testability rules keep the seams honest: every Interop interaction sits behind an interface, `IClock`
is injected wherever time matters, every async method takes a `CancellationToken`, and Core has no
statics beyond pure math. Fake implementations (`FakeWindowEnumerator`, `FakeWindowController`, and
siblings) live in `tests/App.Tests/Fakes/`.

## 10. Persistence and configuration

The settings file is `%APPDATA%\Stagehand\settings.json`; the path is resolved through
`ISettingsPathProvider` so tests and dry runs can redirect it. The schema is versioned: Stagehand
ships schema **v0** as a placeholder with empty section records, and Plan 04 performs an honest
`v0 → v1` migration that populates concrete fields.

```json
{
  "schemaVersion": 0,
  "general": { "startWithWindows": false, "language": "en" },
  "appearance": {},
  "behavior": {},
  "excludedApps": [],
  "updates": { "channel": "stable", "autoCheck": true },
  "logging": { "levels": {} }
}
```

`AppSettings` is an immutable record with nested `General`, `Appearance`, `Behavior`, `Updates`, and
`Logging` records. `SettingsService` loads on startup (writing defaults and logging at `Warning` if
the file is missing or corrupt), saves atomically via `settings.json.tmp` + `File.Replace`, raises
a `Changed` event on every successful save, and uses `SettingsMigrator` to map old keys to new on
schema bumps. Schema bumps are breaking changes and earn their own plan section.

## 11. Build, CI, and release baseline

Every project targets `net8.0-windows10.0.19041.0`. The supported operating systems are Windows 10
21H2 (build 19044) and Windows 11 22H2+. CI runs on `windows-2022`, pinned to avoid surprise image
bumps. Both x64 and arm64 are built; arm64 is tier-2 (built but not exhaustively tested). Nullable
reference types are enabled project-wide, and release builds treat warnings as errors.

Central package management keeps every NuGet version in `Directory.Packages.props`; project files
reference packages without versions. Each project commits a `packages.lock.json`, and CI runs with
`RestoreLockedMode=true`. Builds are deterministic (`<Deterministic>true</Deterministic>` and
`ContinuousIntegrationBuild` set in CI).

The `.github/workflows/ci.yml` workflow runs four formatter gates (Prettier, `dotnet format`,
CSharpier, XAML Styler), restores with `--locked-mode`, builds Release, runs the test suite with
coverage, and executes `dotnet list package --vulnerable --include-transitive`. A release workflow
(`release.yml`) is scaffolded but disabled until Plan 05 enables signing, packaging, and update
publication.
