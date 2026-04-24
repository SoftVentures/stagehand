# Plan 01 — Architecture, Contribution & Security

> Foundational plan. Covers everything that must be decided before any feature code is written. Incorporates Phase 0 (scaffolding) from `stage-manager-windows-plan.md` together with all cross-cutting concerns that would otherwise be re-litigated during feature phases.

---

## Context

The repository today contains one design document (`stage-manager-windows-plan.md`) describing the product vision, the DWM-thumbnail core mechanism, a tech stack choice and a ten-phase roadmap. No code exists yet.

Before we start building features, we need a single document that freezes the architectural and organisational ground rules: module boundaries, the P/Invoke strategy, the threading model, how errors propagate, how the app is tested and released, how contributors work with the code, and what the security posture is. Locking these in first prevents divergent decisions in Plans 02–05 and gives new contributors a map of the code they are stepping into.

This plan is the hardest to get right and the cheapest to change. Everything downstream assumes it is stable.

---

## Goal

Produce an architecturally complete, lint-clean, CI-green **scaffold** of the Stagehand solution together with the governance documents (`CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `.github/`) and the decisions document (`docs/architecture/overview.md`) so that Plans 02–05 can be executed without re-opening any cross-cutting question.

At the end of this plan:

- `dotnet build` and `dotnet test` succeed on a clean clone.
- A tray icon appears when the app is launched; right-click → Exit works.
- An empty Settings window opens from the tray menu.
- CI runs green on push.
- Every contribution convention (style, commit format, PR template, security disclosure) is documented and enforced where automation is possible.

No feature behaviour. No window enumeration. No DWM thumbnails. Just the skeleton — but a _complete_ skeleton.

---

## Acceptance Criteria

1. Solution builds on a clean Windows 11 machine with only the .NET 8 SDK installed: `dotnet build -c Release` succeeds with zero warnings.
2. `dotnet test` runs at least one sentinel test in each test project and exits 0.
3. All four formatter gates pass on a clean checkout:
   - `dotnet format --verify-no-changes` — style/whitespace on C# + `.csproj`.
   - `npx prettier --check .` — Markdown, YAML, JSON.
   - `dotnet csharpier --check .` — C# layout.
   - `dotnet xstyler --recursive --directory app --passive` — XAML.
4. Running the App project produces a tray icon (sourced from `app/assets/brand/icons/colored/icon.ico`, copied at build time by `app/build/copy-brand-assets.targets`) and an empty Settings window.
5. CI workflow `.github/workflows/ci.yml` runs on push + PR; all four formatter gates, build, test, and vulnerability audit green.
6. `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `LICENSE` (MIT), and `THIRD-PARTY-NOTICES.md` exist and are non-placeholder.
7. Every module has at least one public class stub with an XML doc comment describing its contract (as specified in §Design).
8. No feature code: enumeration, parking, Work Area and DWM calls are interfaces with stub implementations that throw `NotImplementedException`. Plans 02–05 will fill them in.
9. `docs/architecture/overview.md` exists and contains the decisions from §Design in permanent form.

---

## Scope

### In scope

- Solution layout and empty projects (App, Core, Interop, Services, Tests, Harness) under `app/src/` + `tests/`.
- Public interfaces, empty class stubs, XML doc comments.
- Dependency injection container wiring.
- Logging sink configuration.
- Settings file path and schema **v0** placeholder (Plan 04 migrates to v1).
- Tray icon (sourced from `app/assets/brand/icons/colored/icon.ico`) and an empty Settings window with Close button only.
- Formatter toolchain: Prettier, CSharpier, XAML Styler, `dotnet format` — all four gated in CI.
- Brand assets extracted from `stagehand-icons.zip` into `app/assets/brand/` + MSBuild copy target.
- GitHub repo hygiene: issue templates, PR template, Dependabot, security policy.
- GitHub Actions `ci.yml` with format / build / test / audit steps.
- `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props` (central package management).
- `packages.lock.json` per project (supply-chain lock).
- Documentation: `docs/architecture/overview.md` and per-module `README.md` stubs.
- MIT licence, contribution guide, code of conduct, security policy.

### Out of scope (belongs to later plans)

- Window enumeration / filtering / control → Plan 02.
- Sidebar overlay and DWM thumbnails → Plan 02.
- StageController enable/disable → Plan 03.
- Settings window UI beyond "opens, closes, placeholder text" → Plan 04.
- Auto-update integration → Plan 05.
- Release workflow / packaging → Plan 05.
- Any real Win32 call implementation.

---

## Design

### 1. System Context

Single-process WPF app. Runs at the user's privilege level. No kernel drivers, no service, no elevation. All window management is done via the public Win32 / DWM APIs that the user already has access to by virtue of running the same desktop session.

```text
┌──────────────────────────────────────────────────────────────┐
│                   Stagehand (user process)             │
│                                                              │
│   ┌──────────────┐    ┌────────────────┐   ┌─────────────┐  │
│   │ App (WPF)    │───▶│ Services       │──▶│ Core        │  │
│   │ tray,        │    │ settings,      │   │ state,      │  │
│   │ overlay,     │    │ hotkey,        │   │ layout,     │  │
│   │ settings UI  │    │ updater        │   │ stage ctrl  │  │
│   └──────┬───────┘    └────────┬───────┘   └──────┬──────┘  │
│          │                     │                  │         │
│          └───────────┬─────────┘                  │         │
│                     ▼                             ▼         │
│              ┌─────────────────────────────────────┐        │
│              │ Interop (P/Invoke + safe wrappers)  │        │
│              │ NativeMethods / WindowEnumerator /  │        │
│              │ WindowController / WinEventHook /   │        │
│              │ WorkAreaManager / DwmThumbnail      │        │
│              └──────────────┬──────────────────────┘        │
└─────────────────────────────┼───────────────────────────────┘
                              │
                     Win32 / DWM APIs
                              │
                        (Windows 10/11)
```

Three threads worth knowing about at this level:

- **UI thread** (WPF dispatcher). Owns all HWND-touching calls and all DWM thumbnail updates.
- **WinEvent hook STA thread** (Plan 03 creates it). Required so `SetWinEventHook` can deliver callbacks without starving the UI thread.
- **Background workers** (`Task.Run`) for I/O: settings read/write, updater HTTP, log file rotation.

### 2. Module Architecture

Four runtime assemblies plus two test projects:

| Project                      | Purpose                                                                                                               | May reference                                                               |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `app/src/App.Interop`  | Raw P/Invoke and thin safe wrappers. No business logic.                                                               | —                                                                           |
| `app/src/App.Core`     | Pure business logic: state, layout, filtering rules, stage controller. **No P/Invoke**, only interfaces from Interop. | Interop (interfaces only)                                                   |
| `app/src/App.Services` | Settings persistence, global hotkey, updater, logging bootstrap.                                                      | Core, Interop                                                               |
| `app/src/App.Shell`      | WPF shell: tray icon, overlay host, settings window, first-run, DI composition root.                                  | Services, Core, Interop                                                     |
| `tests/App.Tests`      | xUnit + FluentAssertions + NSubstitute. Unit tests for Core + Services.                                               | Core, Services; Interop only via `InternalsVisibleTo` for native-seam fakes |
| `tests/App.Harness`    | A minimal WPF app that exercises Interop on the real desktop. Not shipped.                                            | All of the above                                                            |

**Dependency direction is one-way**: `App → Services → Core → Interop`. Enforced by the project references and reviewed in code review. Reverse references fail `dotnet build`.

**Core never references `System.Windows.*` or `user32.dll`.** If a Core type needs window information it takes an `IWindowEnumerator` or `WindowSnapshot` argument.

**Test access to Interop.** `App.Tests` does not take a direct `ProjectReference` to Interop in production-path tests (Core and Services tests substitute interfaces via NSubstitute). The one exception is the native-seam layer: `INativeWindowApi`, `INativeDwmApi`, `INativeCompositionApi` are `internal` in Interop with `[assembly: InternalsVisibleTo("App.Tests")]` (Plan 02 §S1). Tests use these seams only to build fakes — never to reach into Interop business logic directly.

#### Public class stubs (written in this plan)

```csharp
// App.Interop
public interface IWindowEnumerator { IReadOnlyList<WindowSnapshot> GetManageableWindows(); }
public interface IWindowController { /* Park, Restore, Resize, BringToFront — Plan 02 */ }
public interface IWorkAreaManager   { /* SaveAll, Apply, RestoreAll — Plan 03 */ }
public interface IWinEventHookFactory { IWinEventHook Create(WinEventSpec spec); }
public interface IDwmThumbnailFactory { IDwmThumbnail Register(IntPtr source, IntPtr destination); }

public sealed record WindowSnapshot(
    IntPtr Hwnd,
    string Title,
    string ClassName,
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    Rect Bounds,
    IntPtr Monitor);

// App.Core  (note: IStageController lives under Core/Stage/ from the start)
public interface IStageController { Task EnableAsync(CancellationToken ct); Task DisableAsync(CancellationToken ct); bool IsEnabled { get; } }
public sealed class StageState { /* immutable snapshot — see §Design.6 */ }
public interface IStageLayoutEngine { /* Plan 02/03 */ }
public interface IWindowFilter { bool IsManageable(WindowSnapshot w); } // Plan 02

// App.Services
public interface ISettingsService { AppSettings Current { get; } event EventHandler<AppSettings> Changed; Task SaveAsync(AppSettings next, CancellationToken ct); }
public interface IHotkeyService { /* Plan 04 */ }
public interface IUpdateService  { /* Plan 05 */ }

// App.Shell
internal sealed class TrayIconHost { /* this plan */ }
public partial class SettingsWindow : Window { /* stub, Plan 04 fills in */ }
```

Each stub has XML doc comments describing its contract. Stubs that cannot be usefully implemented yet throw `NotImplementedException` with a `TODO` comment pointing to the plan that implements them.

### 3. P/Invoke Layer Conventions

All `[DllImport]` declarations live in `App.Interop/NativeMethods.cs`. No other file declares native entry points.

Rules:

- Use `partial class NativeMethods` split by subsystem into files: `NativeMethods.User32.cs`, `NativeMethods.Dwmapi.cs`, `NativeMethods.Shcore.cs`. Partial merges at build time.
- Prefer `LibraryImport` (source-generated marshalling) over `DllImport` where .NET 8 supports it; fall back to `DllImport` for unsupported signatures. Always set `SetLastError = true` where the API documents a last-error code.
- Use blittable types where possible: `IntPtr` for HWND/HMONITOR, structs with `[StructLayout(LayoutKind.Sequential)]`.
- Use `SafeHandle` subclasses for resources that must be released: `DwmThumbnailSafeHandle`, `WinEventHookSafeHandle`. Never store a raw pointer in managed state past the end of the wrapping method.
- Translate Win32 failures into typed exceptions at the wrapper boundary (§7). The rest of the app never sees `Marshal.GetLastWin32Error()`.
- Every native wrapper method has an integration test in the Harness project (written as part of Plan 02 onwards).

Signatures that don't have a safe wrapper yet (pre-Plan 02) still go through `NativeMethods.*.cs` so there is exactly one place for them. A `// used by: plan 02 §3` comment points at who will consume them.

### 4. Threading & Dispatching Model

Explicit rules, enforced by code review and by runtime asserts in debug builds:

1. **All HWND manipulations** (`SetWindowPos`, `ShowWindow`, `SetForegroundWindow`) happen on the **UI thread**. Wrappers call `Application.Current.Dispatcher.VerifyAccess()` in Debug.
2. **DWM thumbnail register/update/unregister** happens on the **UI thread**. Same assertion.
3. **`SetWinEventHook`** is installed on a **dedicated STA thread** with its own message loop (`WinEventHookThread`). Callbacks are marshalled to the UI thread via `Dispatcher.InvokeAsync` before any state change.
4. **Blocking I/O** (settings disk read/write, updater HTTP, log rotation) happens in `Task.Run` and never on the UI thread.
5. **Budget**: any UI-thread callback must return in ≤16 ms. Longer work is offloaded.
6. No `Thread.Sleep`, no `Task.Wait`, no `.Result` on the UI thread.

Two helper utilities live in `App.Shell/Threading/`:

- `UiDispatcher` — a thin facade over `Application.Current.Dispatcher` that is mockable for tests and has `RunAsync`, `Post`, `AssertOnUiThread` helpers.
- `WinEventHookThread` — encapsulates the STA hook thread; created lazily when the first hook is installed, disposed on app exit.

### 5. Dependency Injection & Composition

We use `Microsoft.Extensions.DependencyInjection` with `Microsoft.Extensions.Hosting` integration. Composition root: `App.OnStartup` in `App.Shell/App.xaml.cs`.

Registered services (singletons unless noted):

| Type                   | Lifetime       | Notes                                                                                           |
| ---------------------- | -------------- | ----------------------------------------------------------------------------------------------- |
| `IWindowEnumerator`    | Singleton      | Plan 02 supplies real impl; this plan registers a `NotImplementedWindowEnumerator` that throws. |
| `IWindowController`    | Singleton      | Same pattern.                                                                                   |
| `IWorkAreaManager`     | Singleton      | Same pattern.                                                                                   |
| `IWinEventHookFactory` | Singleton      | Same pattern.                                                                                   |
| `IDwmThumbnailFactory` | Singleton      | Same pattern.                                                                                   |
| `ISettingsService`     | Singleton      | Real impl in this plan — see §10.                                                               |
| `IHotkeyService`       | Singleton      | Stub in this plan, real impl in Plan 04.                                                        |
| `IUpdateService`       | Singleton      | Stub, Plan 05.                                                                                  |
| `IStageController`     | Singleton      | Stub, Plan 03.                                                                                  |
| `IStageLayoutEngine`   | Singleton      | Stub, Plan 02.                                                                                  |
| `IWindowFilter`        | Singleton      | Stub, Plan 02.                                                                                  |
| `UiDispatcher`         | Singleton      | Real.                                                                                           |
| `TrayIconHost`         | Singleton      | Real — this plan.                                                                               |
| `SettingsWindow`       | Transient      | Factory-resolved each open.                                                                     |
| `ILogger<T>`           | From framework | Serilog sink (§8).                                                                              |
| `IClock`               | Singleton      | `SystemClock` by default; `FakeClock` in tests.                                                 |

`App.xaml.cs` keeps composition to under 50 lines by delegating to a static `ServiceConfiguration.ConfigureServices(IServiceCollection services)` method.

### 6. State Model

`StageState` is an immutable record. All transitions return a new `StageState`; the live stage holds exactly one current state inside `StageController`.

```csharp
public enum StagePhase { Disabled, Enabling, Enabled, Disabling, Paused }

public sealed record StageState(
    StagePhase Phase,
    ImmutableList<ParkedWindow> Parked,
    ImmutableDictionary<string, IntPtr?> ActiveHwndByDevice, // key = MONITORINFOEXW.szDevice
    ImmutableDictionary<string, SavedWorkArea> SavedWorkAreasByDevice,
    ImmutableList<WindowIdentity> ElevatedPresent,           // windows visible in sidebar but not parkable (Plan 05)
    bool IsPaused);                                          // fullscreen-app pause (Plan 05)

public sealed record ParkedWindow(WindowSnapshot Snapshot, Rect OriginalBounds, string OriginalDeviceName);
public sealed record SavedWorkArea(string DeviceName, Rect OriginalRect);
```

**Why device-name keying, not HMONITOR.** `HMONITOR` handles are not stable across hot-plug, sleep/wake, or explorer restart. The canonical stable identifier is the device name from `MONITORINFOEXW.szDevice` (e.g. `\\.\DISPLAY1`). Every lookup that persists across frames — saved Work Areas, per-monitor active window, snapshot file — uses the device name. Raw `HMONITOR` values are only held as ephemeral, within-a-single-call locals. Plan 03 §Design.8 depends on this invariant.

Transitions:

```text
Disabled ──Enable()──▶ Enabling ──(success)──▶ Enabled
                         │
                         └──(failure)──▶ rollback ──▶ Disabled

Enabled ──Disable()──▶ Disabling ──▶ Disabled
```

Reentrancy rule: `EnableAsync` / `DisableAsync` take an `async` lock; a second call while Enabling/Disabling awaits the first. User actions (tray click, hotkey, foreground swap) all funnel through the same lock.

The state machine's detailed transitions and rollback choreography are implemented in Plan 03. This plan only declares the types and the interface.

### 7. Error Handling Conventions

Typed exceptions in `App.Core/Errors/`:

- `Win32InteropException(string apiName, int errorCode)` — raised by Interop wrappers.
- `WindowNotManageableException(IntPtr hwnd, string reason)`
- `ElevationBoundaryException(IntPtr hwnd)` — raised when an unelevated process tries to control an elevated window.
- `StageTransitionException(StagePhase from, StagePhase to, Exception inner)` — wraps transition failures.

Top-level handlers in `App.xaml.cs`:

- `AppDomain.CurrentDomain.UnhandledException`
- `Application.Current.DispatcherUnhandledException`
- `TaskScheduler.UnobservedTaskException`

Each handler:

1. Logs the full exception at `Critical`.
2. Calls `StageController.DisableAsync(CancellationToken.None)` with a 2-second timeout so the desktop is restored.
3. Surfaces a tray notification summarising the failure and pointing at the log folder.
4. In debug builds: rethrows. In release builds: sets `e.Handled = true` (where possible) and keeps the tray running so the user can reach diagnostics.

**Principle.** On unrecoverable error, _leave the desktop in a usable state before notifying_. Users should never be stranded with windows parked off-screen because the manager crashed.

### 8. Logging & Diagnostics

Logging stack:

- API: `Microsoft.Extensions.Logging`.
- Sink: Serilog via `Serilog.Extensions.Logging`.
- Output: rolling file sink at `%LOCALAPPDATA%\Stagehand\logs\stagemanager-.log`, 7-day retention, 5 MB per file.
- Debug build: also logs to the debugger output.

Categories (`ILogger<T>` per class). Default level: `Information`. Per-category overrides configurable via `settings.json` key `logging.levels`.

Command-line flags (parsed in `Program.Main`):

- `--verbose` → sets minimum level to `Debug`.
- `--dry-run` → suppresses all Win32 state changes; log entries become `[DRY-RUN]`-prefixed. Implemented by injecting a `DryRunWindowController` decorator.
- `--diagnostics` → opens the log folder in Explorer, then exits.

Tray context menu always includes **Open Diagnostics** which opens the log folder.

### 9. Testing Strategy

Three tiers:

1. **Unit** (`tests/App.Tests`) — xUnit, FluentAssertions, NSubstitute. Covers Core + Services. Fakes for Interop interfaces. Target ≥80 % line coverage on Core logic (not a gate initially, but reported by `coverlet` in CI).
2. **Harness** (`tests/App.Harness`) — a WPF app, not a test project; launched manually. Enumerates real windows, registers real DWM thumbnails, tests park/restore against Notepad. Produces a written checklist file after each run.
3. **Manual checklists** (`docs/manual-tests/*.md`) — authored as each plan is completed. This plan creates the `docs/manual-tests/` folder and an index file.

Testability rules:

- Every Interop interaction is behind an interface. Core tests never touch `user32.dll`.
- `IClock` is injected wherever time matters.
- All async methods take `CancellationToken`.
- No statics in Core except pure math.

Fake implementations live in `tests/App.Tests/Fakes/`: `FakeWindowEnumerator`, `FakeWindowController`, etc.

### 10. Persistence & Configuration

Settings file: `%APPDATA%\Stagehand\settings.json`. Path encapsulated by `ISettingsPathProvider`.

Schema **v0** (placeholder fields only — Plan 04 does a real v0 → v1 migration with concrete fields):

```json
{
  "schemaVersion": 0,
  "general": {
    "startWithWindows": false,
    "language": "en"
  },
  "appearance": {},
  "behavior": {},
  "excludedApps": [],
  "updates": { "channel": "stable", "autoCheck": true },
  "logging": { "levels": {} }
}
```

**Why v0 and not v1.** Plan 04 populates all section records with concrete fields and bumps the schema to v1 with a real migrator. If Plan 01 shipped `schemaVersion: 1` with empty sections, Plan 04's migrator would see a "v1 but partially populated" file and risk overwriting user tweaks (someone who installed the Plan 01 scaffold, toggled `updates.autoCheck`, then upgraded). Using v0 here makes the Plan 04 upgrade an honest `v0 → v1` transformation with defensive merging of any pre-existing field values.

`AppSettings` is an immutable record with a nested `General`, `Appearance`, `Behavior`, `Updates`, `Logging` records.

`SettingsService`:

- Loads on startup; if missing or corrupt, writes defaults and logs at `Warning`.
- Saves atomically: write to `settings.json.tmp` → `File.Replace` → delete temp. On Windows this is atomic for the renamer.
- Raises `Changed` event on every successful save.
- Migrates forward: on schema bump, `SettingsMigrator` maps old keys to new. Bump is a breaking change and warrants a new plan section.

### 11. Build, CI, Release Baseline

- **Target framework**: `net8.0-windows10.0.19041.0`.
- **OS baseline**: Windows 10 21H2 (build 19044) and Windows 11 22H2+. CI runs `windows-latest` (currently Server 2022; pinned to prevent surprise bumps).
- **Runtimes**: x64 and arm64. Both built in CI; arm64 is marked as tier-2 (built, not exhaustively tested).
- **Nullable**: enabled project-wide.
- **Warnings-as-errors**: yes, release builds only.
- **Central package management**: `Directory.Packages.props` pins every NuGet version. Project files reference packages without version.
- **Lockfile**: `packages.lock.json` committed per project; `RestoreLockedMode=true` in CI.
- **Determinism**: `<Deterministic>true</Deterministic>`, `<ContinuousIntegrationBuild>` set in CI.

CI pipeline `.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push: { branches: [main] }
  pull_request:
jobs:
  build:
    runs-on: windows-2022
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with: { dotnet-version: "8.0.x" }
      - uses: actions/setup-node@v6
        with: { node-version: "24" }

      # Formatter gates (§S1a)
      - name: Install Prettier
        run: npm ci
      - name: Prettier check
        run: npx prettier --check .
      - name: Restore dotnet tools
        run: dotnet tool restore
      - name: CSharpier check
        run: dotnet csharpier --check .
      - name: XAML Styler check
        run: dotnet xstyler --recursive --directory app --passive

      # Build + test gates
      - run: dotnet restore --locked-mode
      - run: dotnet format --verify-no-changes
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger "trx;LogFileName=test.trx" --collect:"XPlat Code Coverage"
      - run: dotnet list package --vulnerable --include-transitive
      - uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/test.trx"
```

The release workflow (`release.yml`) is scaffolded but disabled until Plan 05 enables it.

### 12. Contribution Guide (`CONTRIBUTING.md`)

Outline (full text written in this plan):

- **Setup**: VS 2022 17.8+ with ".NET desktop development" workload, or Rider 2024.1+. `dotnet-format` via `dotnet tool restore`.
- **Run**: `dotnet run --project app/src/App.Shell`.
- **Test**: `dotnet test`.
- **Style**: four opinionated formatters (see §S1a):
  - C#: **CSharpier** — `dotnet csharpier .` (write) / `--check` (CI gate).
  - C# whitespace/style: `dotnet format` (driven by `.editorconfig`).
  - XAML: **XAML Styler** — `dotnet xstyler --recursive --directory app`.
  - Markdown / YAML / JSON: **Prettier** — `npx prettier --write .`.
    All four run as checks in CI; none of them require debate in code review.
- **Nullable**: on everywhere. `!` suppression requires a `// reason:` comment on the same line.
- **Branching**: trunk-based. Feature branches named `feat/<topic>`, `fix/<topic>`, `docs/<topic>`. Short-lived (< 1 week preferred).
- **Commits**: Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`). Scope optional but encouraged: `feat(overlay): ...`. Body wraps at 72 chars. Rationale: Velopack release notes parse Conventional Commits.
- **PRs**: linked issue, description, manual test steps, screenshots for UI changes, CI green, one approving review.
- **Issue templates**: `bug.yml`, `feature.yml`, `question.yml`, `security.yml` (redirects to `SECURITY.md`).
- **PR template**: checkboxes for tests added, docs updated, no new warnings, target plan link.
- **Dependabot**: NuGet + GitHub Actions, weekly.
- **Code of Conduct**: Contributor Covenant v2.1 verbatim.

### 13. Security Model

Threat model (documented in `docs/architecture/threat-model.md`, created by this plan):

- **Attacker capability**: another process on the same user session. Cannot escalate via Stage Manager because Stage Manager has only user-level privileges itself.
- **Assets**: user's settings file, log files, update binaries, local desktop state.
- **Non-assets**: network traffic (there is only the updater endpoint; contents are public release artefacts).

Mitigations:

1. **No elevation by default.** The app is designed to run unprivileged. On startup, if it detects it is running elevated AND no `--allow-elevated` flag was passed, it shows a warning dialog instructing the user to relaunch normally, then exits. Plan 05 adds an opt-in "Run Stagehand as administrator" path (Settings → General) that sets a persisted "user acknowledged elevation" flag; subsequent elevated launches pass `--allow-elevated` via the `.lnk` target and the refusal is skipped. This is deliberately friction-heavy: elevation lets Stagehand control more windows (Task Manager, Registry Editor) but also makes any bug in the app more dangerous. Off by default.
2. **No code injection, no DLL hooks.** All window management is via public APIs.
3. **Elevation boundary.** Elevated windows from other processes cannot be parked; `WindowController.Park` raises `ElevationBoundaryException`. Plans 02–03 surface this to the UI.
4. **Update channel.** Update URL is pinned to the GitHub Releases of the official repo (compile-time constant, overridable only via a debug-only env var). Velopack verifies Authenticode signatures on every package.
5. **Supply chain.**
   - `Directory.Packages.props` + `packages.lock.json` committed. `--locked-mode` in CI.
   - Dependabot weekly.
   - `dotnet list package --vulnerable --include-transitive` in CI fails the build on any Critical/High advisory.
   - SBOM (`CycloneDX`) generated in `release.yml` (disabled until Plan 05) and attached to each release.
6. **Code signing.** Path documented; release workflow reads `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD` from repo secrets. Unsigned builds are allowed for development but logged as `Warning` on startup.
7. **Telemetry.** None. Explicitly documented. If ever added, it must be opt-in, anonymised, and described in a separate plan.
8. **Crash dumps.** Not collected. Exceptions go to the local log file only.
9. **Responsible disclosure.** `SECURITY.md` lists a security contact email and a 90-day embargo policy, based on the GitHub Private Vulnerability Reporting flow.
10. **`.gitignore`.** Excludes `*.pfx`, `*.snk`, `secrets.*`, `.env*`, `*.user`, `bin/`, `obj/`, `logs/`.

### 14. Licensing & OSS Hygiene

- **Licence**: MIT. Present as `LICENSE` in the repo root.
- **Copyright**: "© the Stagehand contributors." (Replace with a concrete legal entity name once one exists.)
- **Third-party notices**: generated from NuGet metadata by `dotnet-project-licenses` into `THIRD-PARTY-NOTICES.md`, checked into the repo, regenerated in CI on PRs that change `Directory.Packages.props`.
- **Apple trademarks**: not used. Product name is **Stagehand**; used for both the brand and the code identifier (namespaces, projects, solution). The macOS feature being emulated is still referred to as "Stage Manager" when discussing the source of the UX inspiration.
- **Icons / art**: no Apple assets. Placeholder icon shipped; replacement tracked in Plan 04.

### 15. Phase 0 Deliverables (concrete scaffolding outputs)

Files to create in this plan — see §Subtasks for the build order. The tree below shows Stagehand at the end of Phase 0 only; §16 shows the v1.0 state.

**Root-level rule:** only four folders live at the repo root — `app/`, `tests/`, `docs/`, `.github/`. Everything else at root is a single configuration or documentation file.

```text
Stagehand/
├─ app/                                     # the Windows application — everything product-specific
│  ├─ src/                                  # .NET source projects
│  │  ├─ App.Interop/                 # P/Invoke + safe wrappers, no business logic
│  │  ├─ App.Core/                    # pure business logic, no P/Invoke
│  │  ├─ App.Services/                # settings, hotkey, updater, logging
│  │  └─ App.Shell/                     # WPF shell: tray, overlay, settings, composition root
│  ├─ assets/                               # brand / press artefacts consumed by the app build
│  │  └─ brand/
│  │     ├─ README.md                       # provenance + licence note
│  │     └─ icons/                          # from stagehand-icons.zip
│  │        ├─ colored/{icon.ico, icon.svg} # default full-colour logo
│  │        ├─ dark/{icon.ico, icon.svg}    # for light-theme OS chrome
│  │        └─ light/{icon.ico, icon.svg}   # for dark-theme OS chrome
│  └─ build/                                # MSBuild + release helpers for the app
│     └─ copy-brand-assets.targets          # copies app/assets/brand/icons/* → app/src/App.Shell/Assets/
├─ tests/                                    # xUnit + manual Harness
│  ├─ App.Tests/                      # unit tests (Core + Services)
│  └─ App.Harness/                    # WPF test harness, not shipped
├─ docs/
│  ├─ architecture/                         # permanent reference docs
│  │  ├─ overview.md
│  │  └─ threat-model.md
│  ├─ plans/                                # per-plan implementation briefs
│  │  ├─ 01-architecture-contribution-security.md   # this file
│  │  ├─ 02-core-window-mechanics.md
│  │  ├─ 03-stage-lifecycle-interaction-multimonitor.md
│  │  ├─ 04-settings-ux-polish.md
│  │  └─ 05-distribution-updates-robustness.md
│  ├─ manual-tests/                         # per-plan QA checklists
│  │  └─ README.md
│  └─ brand/
│     └─ README.md                          # logo / colour usage rules for contributors + press
├─ .github/
│  ├─ CODEOWNERS
│  ├─ dependabot.yml
│  ├─ pull_request_template.md
│  ├─ ISSUE_TEMPLATE/
│  │  ├─ bug.yml
│  │  ├─ feature.yml
│  │  ├─ question.yml
│  │  └─ config.yml
│  └─ workflows/
│     ├─ ci.yml
│     └─ release.yml                        # skeleton, disabled until Plan 05
├─ .config/
│  └─ dotnet-tools.json                     # local .NET tools: csharpier, xamlstyler.console
├─ .editorconfig
├─ .gitattributes
├─ .gitignore
├─ .prettierrc.json                         # Prettier config (Markdown, YAML, JSON)
├─ .prettierignore
├─ package.json                              # dev-only: prettier devDependency
├─ package-lock.json                         # committed; reproducible prettier install in CI
├─ global.json                               # pin .NET SDK 8.0.x
├─ Directory.Build.props                     # LangVersion, Nullable, TargetFramework
├─ Directory.Packages.props                  # central package versions
├─ App.sln                             # references app/src/* and tests/*
├─ LICENSE                                   # MIT
├─ README.md
├─ CONTRIBUTING.md
├─ SECURITY.md
├─ CODE_OF_CONDUCT.md                        # Contributor Covenant 2.1
├─ THIRD-PARTY-NOTICES.md
└─ NOTES.md                                  # running decisions log
```

#### Detail inside each `app/src/` project

```text
app/src/App.Interop/
├─ App.Interop.csproj
├─ NativeMethods.cs                          # partial — shared attributes
├─ NativeMethods.User32.cs                   # [DllImport] signatures for user32
├─ NativeMethods.Dwmapi.cs
├─ NativeMethods.Shcore.cs
├─ IWindowEnumerator.cs                      # public contract; impl arrives in P02
├─ IWindowController.cs
├─ IWorkAreaManager.cs
├─ IWinEventHook.cs
├─ IWinEventHookFactory.cs
├─ IDwmThumbnail.cs
├─ IDwmThumbnailFactory.cs
├─ WindowSnapshot.cs                         # data record
├─ Geometry/
│  └─ Rect.cs                                # WPF-free custom struct
├─ SafeHandles/
│  ├─ DwmThumbnailSafeHandle.cs              # ReleaseHandle stub → real in P02
│  └─ WinEventHookSafeHandle.cs
└─ Stubs/                                     # throw-only shims, removed as plans deliver real impls
   ├─ NotImplementedWindowEnumerator.cs
   ├─ NotImplementedWindowController.cs
   ├─ NotImplementedWorkAreaManager.cs
   ├─ NotImplementedWinEventHookFactory.cs
   └─ NotImplementedDwmThumbnailFactory.cs

app/src/App.Core/
├─ App.Core.csproj
├─ Errors/
│  ├─ Win32InteropException.cs
│  ├─ WindowNotManageableException.cs
│  ├─ ElevationBoundaryException.cs
│  └─ StageTransitionException.cs
├─ State/
│  ├─ StagePhase.cs                          # Disabled | Enabling | Enabled | Disabling | Paused
│  ├─ StageState.cs                          # immutable record, device-name keyed
│  ├─ ParkedWindow.cs
│  └─ SavedWorkArea.cs
├─ Stage/
│  └─ IStageController.cs                    # contract; StageController.cs arrives in P03
├─ Layout/
│  └─ IStageLayoutEngine.cs                  # contract; engine arrives in P02
├─ Windows/
│  └─ IWindowFilter.cs                       # contract; filter arrives in P02
├─ Time/
│  ├─ IClock.cs
│  └─ SystemClock.cs
└─ Stubs/
   ├─ NotImplementedStageController.cs
   ├─ NotImplementedStageLayoutEngine.cs
   └─ NotImplementedWindowFilter.cs

app/src/App.Services/
├─ App.Services.csproj
├─ Settings/
│  ├─ ISettingsService.cs
│  ├─ SettingsService.cs                     # real — atomic JSON read/write
│  ├─ ISettingsPathProvider.cs
│  ├─ AppDataSettingsPathProvider.cs
│  ├─ SettingsMigrator.cs                    # v0 only here; P04 adds v0→v1 migration
│  ├─ AppSettings.cs                         # placeholder sections; P04 fills concrete fields
│  └─ AppSettingsSections.cs
├─ Hotkey/
│  ├─ IHotkeyService.cs
│  └─ StubHotkeyService.cs                   # replaced in P03
├─ Update/
│  ├─ IUpdateService.cs
│  └─ StubUpdateService.cs                   # replaced by Velopack impl in P05
└─ Logging/
   └─ LoggingConfiguration.cs                # Serilog bootstrap

app/src/App.Shell/
├─ App.Shell.csproj                      # <UseWPF>, OutputType WinExe, imports ../../build/copy-brand-assets.targets
├─ App.xaml
├─ App.xaml.cs                               # global exception handlers + DI composition
├─ Program.cs                                # [STAThread] Main, CLI arg parsing
├─ ServiceConfiguration.cs                   # IServiceCollection setup (P02–P05 extend)
├─ Threading/
│  ├─ UiDispatcher.cs
│  └─ WinEventHookThread.cs                  # stub here, real impl in P02
├─ Tray/
│  ├─ TrayIconHost.cs                        # opens Settings, Exit, Open Diagnostics
│  └─ TrayResources.xaml
├─ Settings/
│  ├─ SettingsWindow.xaml                    # placeholder; P04 fills the UI
│  └─ SettingsWindow.xaml.cs
├─ Overlay/
│  ├─ SidebarOverlay.xaml                    # stub; P02 real; P04 polishes
│  └─ SidebarOverlay.xaml.cs
├─ FirstRun/
│  ├─ WelcomeWindow.xaml                     # stub; P04 real
│  └─ WelcomeWindow.xaml.cs
├─ Properties/
│  └─ AssemblyInfo.cs
└─ Assets/                                    # BUILD-TIME COPY TARGET ONLY — do not hand-edit
   ├─ TrayIcon.ico                           # copied from app/assets/brand/icons/colored/icon.ico
   └─ README.md                              # "Generated from app/assets/brand/ by copy-brand-assets.targets"
```

#### Detail inside each `tests/` project

```text
tests/App.Tests/
├─ App.Tests.csproj
├─ ProjectReferenceSmokeTests.cs             # assert DI container resolves every service
├─ Settings/
│  └─ SettingsServiceTests.cs                # load-defaults, round-trip, atomic-write
└─ Fakes/
   └─ FakeClock.cs

tests/App.Harness/
├─ App.Harness.csproj
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml                            # P02 extends with enumerator/overlay/hook buttons
├─ MainWindow.xaml.cs
└─ README.md                                  # how to launch and what to look for
```

### 16. Complete Target File Layout (1:1, all plans combined)

This is the **authoritative file tree of Stagehand at v1.0** — every file that exists after Plans 01–05 have all been executed. Use this as the shape to hold in your head; each subtask in each plan states which files it creates or modifies, and every created/modified path must appear here.

Legend:

- `← P0x` : which plan originates the file (P01 = this plan).
- `← P0x modifies` : file is created by an earlier plan and expanded/replaced by `P0x`.
- No annotation: root-level or implied by tooling (obj/, bin/).

#### Top-level layout at v1.0

```text
Stagehand/
├─ app/                                     # the Windows application — product-specific code, assets, build helpers
│  ├─ src/                                  # detailed per-project trees below
│  │  ├─ App.Interop/
│  │  ├─ App.Core/
│  │  ├─ App.Services/
│  │  └─ App.Shell/
│  ├─ assets/
│  │  └─ brand/
│  │     ├─ README.md                       # P01 — provenance + licensing
│  │     └─ icons/                          # from stagehand-icons.zip
│  │        ├─ colored/{icon.ico, icon.svg}
│  │        ├─ dark/{icon.ico, icon.svg}
│  │        └─ light/{icon.ico, icon.svg}
│  └─ build/
│     ├─ copy-brand-assets.targets          # P01 — copies app/assets/brand/icons → app/src/App.Shell/Assets
│     ├─ Generate-ReleaseNotes.ps1          # P05 — Conventional-Commits → Markdown
│     ├─ Generate-ReleaseNotes.Tests.ps1    # P05 — Pester tests for the above
│     └─ winget/
│        └─ Stagehand.Stagehand.yaml        # P05 — winget manifest
├─ tests/                                    # unit + integration tests — layout mirrors app/src/
│  ├─ App.Tests/
│  └─ App.Harness/
├─ docs/
│  ├─ architecture/                         # P01 — permanent references
│  │  ├─ overview.md
│  │  └─ threat-model.md
│  ├─ plans/                                # P01–P05 — implementation briefs
│  ├─ manual-tests/                         # per-plan QA checklists
│  ├─ release/
│  │  └─ winget.md                          # P05 — submission flow
│  └─ brand/
│     └─ README.md                          # P01 — logo / colour usage rules
├─ .github/
│  ├─ CODEOWNERS                            # P01
│  ├─ dependabot.yml                        # P01
│  ├─ pull_request_template.md              # P01
│  ├─ ISSUE_TEMPLATE/{bug,feature,question,config}.yml    # P01
│  └─ workflows/
│     ├─ ci.yml                             # P01 — build + test + format + vuln scan + prettier + csharpier
│     └─ release.yml                        # P01 skeleton, enabled + fleshed out in P05
├─ .config/
│  └─ dotnet-tools.json                     # P01 — csharpier, xamlstyler.console
├─ .editorconfig                            # P01
├─ .gitattributes                           # P01
├─ .gitignore                               # P01
├─ .prettierrc.json                         # P01 — Markdown/YAML/JSON formatter config
├─ .prettierignore                          # P01
├─ package.json                              # P01 — dev-only, prettier devDependency
├─ package-lock.json                         # P01 — committed
├─ global.json                               # P01 — pin .NET SDK 8.0.x
├─ Directory.Build.props                     # P01; P05 adds MinVer
├─ Directory.Packages.props                  # P01; P02/P04/P05 add packages
├─ App.sln                             # P01 — references app/src/* and tests/*
├─ LICENSE                                   # P01 — MIT
├─ README.md                                 # P01; P05 adds FAQ + known limitations
├─ CONTRIBUTING.md                           # P01
├─ SECURITY.md                               # P01
├─ CODE_OF_CONDUCT.md                        # P01 — Contributor Covenant 2.1
├─ THIRD-PARTY-NOTICES.md                    # P01 — regenerated by CI on package changes
└─ NOTES.md                                  # P01 — running decisions log
```

#### `app/src/App.Interop/`

```text
app/src/App.Interop/
├─ App.Interop.csproj                  # P01
├─ NativeMethods.cs                          # P01 — partial, shared attributes
├─ NativeMethods.User32.cs                   # P01
├─ NativeMethods.Dwmapi.cs                   # P01
├─ NativeMethods.Shcore.cs                   # P01
├─ NativeMethods.Shell32.cs                  # P04 — ExtractIconEx; P05 extends: SHQueryUserNotificationState
├─ IWindowEnumerator.cs                      # P01
├─ IWindowController.cs                      # P01
├─ IWorkAreaManager.cs                       # P01
├─ IWinEventHook.cs                          # P01
├─ IWinEventHookFactory.cs                   # P01
├─ IDwmThumbnail.cs                          # P01
├─ IDwmThumbnailFactory.cs                   # P01
├─ WindowSnapshot.cs                         # P01 — record; P02 adds WindowIdentity
├─ SetWindowPosFlags.cs                      # P02
├─ WindowEnumerator.cs                       # P02
├─ WindowController.cs                       # P02
├─ WinEventHook.cs                           # P02
├─ WinEventHookFactory.cs                    # P02
├─ DwmThumbnail.cs                           # P02
├─ DwmThumbnailFactory.cs                    # P02
├─ INativeDwmApi.cs                          # P02 — internal seam
├─ NativeDwmApi.cs                           # P02
├─ WorkAreaManager.cs                        # P03 — replaces the P01 stub
├─ MonitorEnumerator.cs                      # P03
├─ MonitorDescriptor.cs                      # P03 — raw record (DeviceName, HMONITOR, FullBounds, WorkArea)
├─ Geometry/
│  └─ Rect.cs                                # P01 — WPF-free custom struct
├─ SafeHandles/
│  ├─ DwmThumbnailSafeHandle.cs              # P01 stub → P02 real ReleaseHandle
│  └─ WinEventHookSafeHandle.cs              # P01 stub → P02 real ReleaseHandle
├─ Internal/                                  # InternalsVisibleTo(App.Tests)
│  ├─ INativeWindowApi.cs                    # P02
│  └─ NativeWindowApi.cs                     # P02
├─ Composition/                               # P04 — Mica/Acrylic applicator
│  ├─ SidebarBackgroundApplicator.cs
│  ├─ INativeCompositionApi.cs
│  ├─ NativeCompositionApi.cs
│  ├─ AccentPolicy.cs
│  ├─ IOsVersionProvider.cs
│  └─ OsVersionProvider.cs
├─ Shell/                                     # P04 — autostart .lnk COM
│  ├─ WshInterop.cs                          # primary: IWshShell
│  └─ ShellLinkInterop.cs                    # fallback: IShellLinkW + IPersistFile
└─ VirtualDesktops/                           # P05
   ├─ IVirtualDesktopProbe.cs
   ├─ VirtualDesktopProbe.cs
   └─ IVirtualDesktopManagerInterop.cs
```

#### `app/src/App.Core/`

```text
app/src/App.Core/
├─ App.Core.csproj                     # P01
├─ Errors/                                    # P01 — typed exceptions
│  ├─ Win32InteropException.cs
│  ├─ WindowNotManageableException.cs
│  ├─ ElevationBoundaryException.cs
│  └─ StageTransitionException.cs
├─ State/                                     # P01 — immutable record types
│  ├─ StagePhase.cs                          # Disabled | Enabling | Enabled | Disabling | Paused (P05 adds Paused)
│  ├─ StageState.cs                          # P01; P05 extends with ElevatedPresent, IsPaused
│  ├─ ParkedWindow.cs                        # device-name keyed
│  └─ SavedWorkArea.cs                       # device-name keyed
├─ Stage/
│  ├─ IStageController.cs                    # P01
│  ├─ StageController.cs                     # P03; P05 extends: reconciliation timer, pause, elevation
│  ├─ StageBehaviorBinder.cs                 # P04 — live-apply Behavior settings
│  ├─ ISwapExecutor.cs                       # P03 — swap contract (Plan 03 §Design.5a)
│  ├─ InstantSwapExecutor.cs                 # P03 — default, no animation
│  ├─ SwapPlan.cs                            # P03
│  └─ AnimationSpeed.cs                      # P03
├─ Layout/
│  ├─ IStageLayoutEngine.cs                  # P01
│  ├─ IThumbnailLayoutEngine.cs              # P02
│  ├─ ThumbnailLayoutEngine.cs               # P02 — pure geometry
│  ├─ LayoutRequest.cs                       # P02
│  └─ ThumbnailPlacement.cs                  # P02
├─ Windows/
│  ├─ IWindowFilter.cs                       # P01
│  ├─ WindowFilter.cs                        # P02; P05 integrates cloak check
│  ├─ WindowClassExclusions.cs               # P02
│  ├─ CloakStateMonitor.cs                   # P05
│  └─ CurrentDesktopFilter.cs                # P05 — cached VD lookup, wired in StageController.SyncOverlays
├─ Fullscreen/                                # P05
│  ├─ FullscreenMonitor.cs
│  └─ FullscreenChangedEventArgs.cs
├─ Time/
│  ├─ IClock.cs                              # P01
│  └─ SystemClock.cs                         # P01
└─ Stubs/                                     # removed as plans deliver real impls
   ├─ NotImplementedStageController.cs       # P01 — removed in P03
   ├─ NotImplementedStageLayoutEngine.cs     # P01 — removed in P02
   └─ NotImplementedWindowFilter.cs          # P01 — removed in P02
```

#### `app/src/App.Services/`

```text
app/src/App.Services/
├─ App.Services.csproj                 # P01
├─ Settings/
│  ├─ ISettingsService.cs                    # P01
│  ├─ SettingsService.cs                     # P01; P04 adds Changed-event diff payload
│  ├─ AppSettings.cs                         # P01 placeholder; P04 fills concrete fields
│  ├─ AppSettingsSections.cs                 # P01
│  ├─ ISettingsPathProvider.cs               # P01
│  ├─ AppDataSettingsPathProvider.cs         # P01
│  ├─ SettingsMigrator.cs                    # P01 v0; P04 adds v0→v1 migration
│  ├─ HotkeySpec.cs                          # P04
│  └─ Sections/                              # P04
│     ├─ GeneralSection.cs
│     ├─ AppearanceSection.cs
│     ├─ BehaviorSection.cs
│     ├─ UpdatesSection.cs
│     ├─ LoggingSection.cs
│     └─ ExcludedApp.cs
├─ Hotkey/
│  ├─ IHotkeyService.cs                      # P01
│  ├─ StubHotkeyService.cs                   # P01 — removed in P03
│  ├─ HotkeyService.cs                       # P03
│  └─ HotkeyRebindBinder.cs                  # P04 — live-apply hotkey changes
├─ Update/
│  ├─ IUpdateService.cs                      # P01
│  ├─ StubUpdateService.cs                   # P01 — removed in P05
│  ├─ VelopackUpdateService.cs               # P05
│  ├─ IPowerStateProvider.cs                 # P05 — battery-aware cadence
│  └─ PowerStateProvider.cs                  # P05
├─ Snapshot/                                  # P03 — crash-recovery state store
│  ├─ ISnapshotStore.cs
│  ├─ SnapshotStore.cs
│  └─ SnapshotFile.cs
├─ Processes/                                 # P04 — excluded-apps UI source
│  ├─ IProcessEnumerator.cs
│  └─ ProcessEnumerator.cs
├─ Icons/                                     # P04 — app icon extraction cache
│  ├─ IAppIconProvider.cs
│  ├─ AppIconProvider.cs
│  └─ AppIconCache.cs
├─ Autostart/                                 # P04
│  ├─ IAutostartService.cs
│  └─ StartupFolderAutostartService.cs       # WshShell primary + IShellLink fallback
└─ Logging/
   └─ LoggingConfiguration.cs                # P01 — Serilog bootstrap
```

#### `app/src/App.Shell/`

```text
app/src/App.Shell/
├─ App.Shell.csproj                      # P01; imports ../../build/copy-brand-assets.targets
├─ app.manifest                              # P05 — PerMonitorV2 DPI awareness
├─ App.xaml                                  # P01
├─ App.xaml.cs                               # P01; P03/P05 add coordinators + crash writer wiring
├─ Program.cs                                # P01; P05 adds VelopackApp.Build().Run() + --version
├─ ServiceConfiguration.cs                   # P01; P02–P05 register real impls
├─ Properties/
│  └─ AssemblyInfo.cs                        # P01
├─ Assets/                                    # build-time copy target — not hand-edited
│  ├─ TrayIcon.ico                           # copied from app/assets/brand/icons/colored/icon.ico
│  ├─ TrayIcon.Dark.ico                      # copied from app/assets/brand/icons/dark/icon.ico
│  ├─ TrayIcon.Light.ico                     # copied from app/assets/brand/icons/light/icon.ico
│  ├─ WelcomeHero.png                        # P04 — first-run visual
│  └─ README.md                              # "Generated from app/assets/brand/ by copy-brand-assets.targets"
├─ Threading/
│  ├─ UiDispatcher.cs                        # P01
│  └─ WinEventHookThread.cs                  # P01 stub; P02 real STA pump
├─ Tray/
│  ├─ TrayIconHost.cs                        # P01; P03 wires toggle; P04 adds balloon helper
│  └─ TrayResources.xaml                     # P01
├─ Overlay/
│  ├─ SidebarOverlay.xaml                    # P01 stub; P02 real; P04 adds rounded corners + acrylic
│  ├─ SidebarOverlay.xaml.cs
│  ├─ StageOverlayHost.cs                    # P03
│  ├─ IStageOverlayHost.cs                   # P03
│  ├─ PerMonitorDpiHelpers.cs                # P02
│  ├─ OverlayMonitor.cs                      # P02 — App-side wrapper over MonitorDescriptor
│  ├─ HoverTooltipBehavior.cs                # P03 — placeholder; P04 hover card supersedes
│  ├─ SidebarAppearanceBinder.cs             # P04 — live-apply Appearance settings
│  ├─ Thumbnails/                            # P04
│  │  ├─ ThumbnailFrame.xaml / .xaml.cs
│  │  └─ HoverCardWindow.xaml / .xaml.cs
│  └─ AutoHide/                              # P04
│     ├─ RevealZoneWindow.xaml / .xaml.cs
│     └─ AutoHideCoordinator.cs
├─ Interaction/
│  └─ StageInteractionCoordinator.cs         # P03 — glues host + hook + controller
├─ Input/
│  └─ KeyboardNavigationCoordinator.cs       # P04 — Ctrl+Alt+[ / Ctrl+Alt+] / Ctrl+Alt+Enter
├─ Monitors/
│  ├─ DisplayChangeListener.cs               # P03 — hidden message-only window for WM_DISPLAYCHANGE
│  └─ MonitorChangeCoordinator.cs            # P03 — hot-plug handling
├─ Recovery/
│  └─ CrashRecoveryCoordinator.cs            # P03 — startup snapshot restore prompt
├─ Diagnostics/
│  └─ CrashReportWriter.cs                   # P05 — local-only crash dumps (never touches snapshot.json)
├─ Animation/                                 # P04
│  ├─ SwapAnimationController.cs             # implements ISwapExecutor from Plan 03
│  └─ AnimationLayerWindow.xaml / .xaml.cs
├─ Themes/                                    # P04
│  ├─ Light.xaml
│  ├─ Dark.xaml
│  └─ ThemeManager.cs
├─ Accessibility/
│  └─ HighContrastDetector.cs                # P04
├─ FirstRun/
│  ├─ WelcomeWindow.xaml                     # P01 stub; P04 real
│  ├─ WelcomeWindow.xaml.cs
│  └─ FirstRunCoordinator.cs                 # P04
└─ Settings/
   ├─ SettingsWindow.xaml                    # P01 stub; P04 real
   ├─ SettingsWindow.xaml.cs
   ├─ ViewModels/                            # P04 — one per section
   │  ├─ MainSettingsViewModel.cs
   │  ├─ GeneralViewModel.cs
   │  ├─ AppearanceViewModel.cs
   │  ├─ BehaviorViewModel.cs
   │  ├─ ExcludedAppsViewModel.cs
   │  ├─ UpdatesViewModel.cs
   │  └─ AboutViewModel.cs
   ├─ Sections/                              # P04 — one UserControl per section
   │  ├─ GeneralView.xaml / .xaml.cs
   │  ├─ AppearanceView.xaml / .xaml.cs
   │  ├─ BehaviorView.xaml / .xaml.cs
   │  ├─ ExcludedAppsView.xaml / .xaml.cs
   │  ├─ UpdatesView.xaml / .xaml.cs
   │  └─ AboutView.xaml / .xaml.cs
   ├─ Controls/
   │  └─ HotkeyCaptureControl.xaml / .xaml.cs    # P04
   └─ Converters/
      ├─ BooleanToVisibilityConverter.cs     # P04
      ├─ EnumToBooleanConverter.cs           # P04
      └─ HotkeySpecDisplayConverter.cs       # P04
```

#### `tests/`

The test-project folder layout mirrors the production layout one-to-one. `tests/App.Tests/Fakes/` holds fakes for the native seams. The Harness project is a small WPF app that exercises the real Win32/DWM surfaces.

```text
tests/App.Tests/
├─ App.Tests.csproj                   # P01
├─ ProjectReferenceSmokeTests.cs            # P01
├─ Fakes/
│  ├─ FakeClock.cs                          # P01
│  ├─ FakeNativeWindowApi.cs                # P02
│  ├─ FakeNativeDwmApi.cs                   # P02
│  ├─ FakeNativeCompositionApi.cs           # P04
│  └─ FakeOsVersionProvider.cs              # P04
├─ Settings/
│  ├─ SettingsServiceTests.cs               # P01
│  ├─ SettingsMigratorTests.cs              # P04
│  ├─ AppSettingsTests.cs                   # P04
│  └─ ViewModels/                           # P04
│     ├─ MainSettingsViewModelTests.cs
│     ├─ AppearanceViewModelTests.cs
│     └─ ExcludedAppsViewModelTests.cs
├─ State/
│  └─ StageStateTests.cs                    # P01
├─ Windows/
│  ├─ WindowFilterTests.cs                  # P02
│  ├─ WindowEnumeratorTests.cs              # P02
│  └─ CloakStateMonitorTests.cs             # P05
├─ Stage/
│  └─ StageControllerTests.cs               # P03; P05 adds reconciliation/fullscreen/elevation cases
├─ Interop/
│  ├─ WindowControllerTests.cs              # P02
│  ├─ WinEventHookThreadTests.cs            # P02
│  ├─ WinEventHookTests.cs                  # P02
│  ├─ DwmThumbnailLifecycleTests.cs         # P02
│  ├─ WorkAreaManagerTests.cs               # P03
│  ├─ VirtualDesktopProbeTests.cs           # P05
│  └─ SidebarBackgroundApplicatorTests.cs   # P04
├─ Layout/
│  └─ ThumbnailLayoutEngineTests.cs         # P02
├─ Interaction/
│  ├─ StageInteractionCoordinatorTests.cs   # P03
│  └─ KeyboardNavigationCoordinatorTests.cs # P04
├─ Monitors/
│  └─ MonitorChangeCoordinatorTests.cs      # P03
├─ Snapshot/
│  └─ SnapshotStoreTests.cs                 # P03
├─ Recovery/
│  └─ CrashRecoveryCoordinatorTests.cs      # P03
├─ Fullscreen/
│  └─ FullscreenMonitorTests.cs             # P05
├─ Animation/
│  └─ SwapAnimationControllerTests.cs       # P04
├─ Icons/
│  └─ AppIconCacheTests.cs                  # P04
├─ Autostart/
│  └─ StartupFolderAutostartServiceTests.cs # P04
├─ Themes/
│  ├─ ThemeManagerTests.cs                  # P04
│  └─ HighContrastDetectorTests.cs          # P04
├─ Update/
│  └─ VelopackUpdateServiceTests.cs         # P05
├─ Diagnostics/
│  └─ CrashReportWriterTests.cs             # P05
├─ Hotkey/
│  ├─ HotkeyServiceTests.cs                 # P03
│  └─ HotkeyCaptureControlTests.cs          # P04
└─ FirstRun/
   └─ FirstRunCoordinatorTests.cs           # P04

tests/App.Harness/
├─ App.Harness.csproj                 # P01
├─ App.xaml / .xaml.cs                      # P01
├─ MainWindow.xaml / .xaml.cs               # P01; P02/P03 add overlay/hook/stage controls
├─ WindowListViewModel.cs                   # P02
└─ README.md                                # P01
```

### Runtime data layout (not in repo, created by the running app)

```text
%APPDATA%\Stagehand\
  settings.json                                                   ← P01 writes (schema v0 — P04 migrates to v1)
  settings.json.tmp                                               ← P01 transient (atomic write)
%LOCALAPPDATA%\Stagehand\
  logs\
    stagemanager-<date>.log                                       ← P01 (Serilog rolling file)
  state\
    snapshot.json                                                 ← P03 writes on enable; deletes on clean disable
  crashes\
    crash-<utc>.json                                              ← P05 writes on unhandled exception
```

### 17. Consistency rules for the tree

- **One canonical location for each type.** If Plan 02 says "`app/src/App.Interop/WindowEnumerator.cs`" and §16 says the same, they must agree. Any mismatch is a Plan bug; fix the plan, not the tree.
- **Stubs are deleted when replaced.** The `Stubs/` folders shrink as Plans 02–05 progress; the final v1.0 build has no classes whose name begins with `NotImplemented*`.
- **Brand assets live in `app/assets/brand/`, never in `app/src/*/Assets/`.** The `Assets/` folder inside each WPF project is a build-time copy target (written by `app/build/copy-brand-assets.targets`); contributors edit the sources under `app/assets/brand/icons/` and let the build copy. `app/src/App.Shell/Assets/*.ico` is `.gitignore`d.
- **Tests mirror the code layout.** The folder path inside `tests/App.Tests/` mirrors the source folder; e.g. `app/src/App.Core/Stage/StageController.cs` → `tests/App.Tests/Stage/StageControllerTests.cs`.
- **Root stays sparse.** Only four folders at the root: `app/`, `tests/`, `docs/`, `.github/`. Every other root entry is a single configuration or documentation file. Resist the urge to add `scripts/`, `utils/`, `archive/` directly at root; those live inside `app/build/` or `docs/` as appropriate.
- **Monitor types: distinct names, no duplication.** `app/src/App.Interop/MonitorDescriptor.cs` is the raw Win32-adjacent record (DeviceName, HMONITOR, FullBounds in physical pixels, WorkArea in physical pixels). `app/src/App.Shell/Overlay/OverlayMonitor.cs` is the App-side value type that wraps a `MonitorDescriptor` and adds WPF-friendly fields (device-independent `Rect`, current DPI scale, attached `SidebarOverlay` reference). `OverlayMonitor` references `MonitorDescriptor`; the reverse is forbidden. Interop never takes a dependency on WPF or on `OverlayMonitor`.

---

## Subtasks

Each subtask lists files, API surface, tests, and a Claude Code prompt. Subtasks are executed in order; later ones depend on earlier ones.

### S1 — Repository hygiene and root files

**Files**: `.editorconfig`, `.gitattributes`, `.gitignore`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `LICENSE`, `NOTES.md`, `README.md` (rewrite).

**API**: none.

**Tests**: none yet.

**Details**:

- `.editorconfig`: 4-space indent, CRLF, `csharp_style_*` rules conservative, `dotnet_diagnostic.CA1031.severity = none` (we catch broadly at the top-level exception handler), warnings-as-errors via `TreatWarningsAsErrors` in `Directory.Build.props` for `Release` only.
- `global.json`: pins .NET SDK to 8.0.x with `rollForward: latestFeature`.
- `Directory.Build.props`: `<LangVersion>latest`, `<Nullable>enable`, `<TargetFramework>net8.0-windows10.0.19041.0`, `<Deterministic>true`.
- `Directory.Packages.props`: `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and the initial package list (Microsoft.Extensions.Hosting, Serilog.\*, H.NotifyIcon.Wpf, CommunityToolkit.Mvvm, xUnit, NSubstitute, FluentAssertions, coverlet.collector).
- `.gitignore`: standard VS/.NET plus `logs/`, `*.pfx`, `*.snk`, `secrets.*`, `.env*`.
- `README.md`: one-paragraph product description + quickstart + link to `docs/`.
- `NOTES.md`: ongoing decisions log with sections "Naming", "Deferred questions", "Open options".

**Claude Code prompt**:

> Create the repository hygiene files listed in Plan 01 §S1 exactly as specified. For `Directory.Packages.props`, pin every package to the latest stable version as of the build date and commit the resolved versions. Do not introduce any additional dependency.

### S1a — Formatter toolchain (Prettier + CSharpier + XAML Styler)

**Goal:** every file type has an opinionated formatter, enforced by CI. No subjective style arguments in review.

**Files**:

- `.prettierrc.json` — Prettier config.
- `.prettierignore` — excludes `bin/`, `obj/`, `publish/`, `app/assets/brand/` (don't reformat binary metadata), `*.ico`, `*.svg`.
- `package.json` — dev-only Node manifest with `"prettier"` as the only `devDependency`. No runtime deps.
- `package-lock.json` — committed so CI and contributors get the same Prettier version.
- `.config/dotnet-tools.json` — local .NET tools manifest pinning `csharpier` and `xamlstyler.console`.
- Extend `.editorconfig` with a `[*.cs] csharpier_*` section (future-proof; CSharpier reads standard editorconfig keys).

**Mapping (what handles what):**

| File type                           | Tool                              | Invocation                                                         |
| ----------------------------------- | --------------------------------- | ------------------------------------------------------------------ |
| `*.md`, `*.yml`, `*.yaml`, `*.json` | Prettier                          | `npx prettier --check .` / `--write .`                             |
| `*.cs`                              | CSharpier                         | `dotnet csharpier --check .` / without flag = write                |
| `*.xaml`                            | XAML Styler (as `xstyler`)        | `dotnet xstyler -f <glob> --passive` (check) / `-f <glob>` (write) |
| `*.csproj`, `*.targets`             | `dotnet format` (already planned) | Same invocation as existing format gate                            |

**`.prettierrc.json`:**

```json
{
  "printWidth": 100,
  "proseWrap": "preserve",
  "tabWidth": 2,
  "overrides": [
    { "files": "*.md", "options": { "tabWidth": 2 } },
    { "files": ["*.yml", "*.yaml"], "options": { "tabWidth": 2 } }
  ]
}
```

**`.prettierignore`:**

```text
bin/
obj/
publish/
node_modules/
app/assets/brand/
**/*.ico
**/*.svg
docs/plans/*.md  # OPTIONAL: contributors may opt these in; the plans were prose-authored and Prettier's wrapping can fight tables
```

(The `docs/plans/` exclusion is advisory — remove once the authors agree that Prettier's `proseWrap: preserve` behaves well on the existing tables.)

**`.config/dotnet-tools.json`:**

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "csharpier": { "version": "0.28.*", "commands": ["csharpier"] },
    "xamlstyler.console": { "version": "3.*", "commands": ["xstyler"] }
  }
}
```

After `dotnet tool restore` these become `dotnet csharpier` and `dotnet xstyler` locally.

**CI integration (extends the `ci.yml` from §Design.11):** add two steps before the build step:

```yaml
- name: Install Prettier
  run: npm ci
- name: Prettier check (Markdown, YAML, JSON)
  run: npx prettier --check .

- name: Restore dotnet tools
  run: dotnet tool restore
- name: CSharpier check (C#)
  run: dotnet csharpier --check .
- name: XAML Styler check (XAML)
  run: dotnet xstyler --recursive --directory app --passive
```

Passive mode makes XAML Styler return non-zero if formatting differs, without writing.

**Pre-commit hook (optional).** No Husky / lefthook installed by default; mentioned in `CONTRIBUTING.md` as a "if you want local automation" section. OSS contributors should not be forced into a Node-based hook system.

**Tests:** none (tool integration, verified by the CI steps).

**Claude Code prompt:**

> Implement the formatter toolchain per Plan 01 §S1a. Create `.prettierrc.json`, `.prettierignore`, `package.json` (only `prettier` as devDependency, `"private": true`), run `npm install` to produce `package-lock.json` and commit it. Create `.config/dotnet-tools.json` with the versions listed. Run `dotnet tool restore` and then `dotnet csharpier --check .` + `dotnet xstyler --recursive --directory app --passive` to confirm clean baseline on the scaffolded code. Add the four new CI steps to `.github/workflows/ci.yml` before the existing `dotnet format` step.

### S1b — Brand assets extraction

**Goal:** take the `stagehand-icons.zip` the user dropped at the repo root and lay it out under `app/assets/brand/`, plus the build glue that copies the right variants into the App's embedded resources at build time.

**Files**:

- `app/assets/brand/README.md` — provenance (where the zip came from), licence (assumed in-project, confirm before first external distribution), edit rule ("`.svg` is the master; regenerate `.ico` via ImageMagick / icotool; commit both"). Three variants explained: `colored/` default, `dark/` for light-theme OS chrome (high-contrast-on-light), `light/` for dark-theme OS chrome.
- `app/assets/brand/icons/colored/icon.ico` + `.svg` — extracted from `stagehand-icons.zip`.
- `app/assets/brand/icons/dark/icon.ico` + `.svg` — ditto.
- `app/assets/brand/icons/light/icon.ico` + `.svg` — ditto.
- `app/build/copy-brand-assets.targets` — MSBuild `.targets` file imported from `app/src/App.Shell/App.Shell.csproj`. Defines an `<Target Name="CopyBrandAssets" BeforeTargets="Build">` that copies:

  ```text
  app/assets/brand/icons/colored/icon.ico → app/src/App.Shell/Assets/TrayIcon.ico
  app/assets/brand/icons/dark/icon.ico    → app/src/App.Shell/Assets/TrayIcon.Dark.ico
  app/assets/brand/icons/light/icon.ico   → app/src/App.Shell/Assets/TrayIcon.Light.ico
  ```

  Uses `<Copy SourceFiles="…" DestinationFiles="…" SkipUnchangedFiles="true" />`. Runs every build; idempotent.

- `app/src/App.Shell/Assets/README.md` — "This folder is generated by `app/build/copy-brand-assets.targets`. Edit the sources in `app/assets/brand/icons/` instead. This folder is `.gitignore`d except for this README."
- `.gitignore` additions: `app/src/App.Shell/Assets/TrayIcon*.ico`, `app/src/App.Shell/Assets/WelcomeHero.png`.

**`.csproj` integration:** add to `app/src/App.Shell/App.Shell.csproj`:

```xml
<Import Project="..\..\build\copy-brand-assets.targets" />

<ItemGroup>
  <Resource Include="Assets\TrayIcon.ico" />
  <Resource Include="Assets\TrayIcon.Dark.ico" />
  <Resource Include="Assets\TrayIcon.Light.ico" />
</ItemGroup>
```

`TrayIconHost` reads the right variant at runtime based on `SystemParameters.HighContrast` and `AppsUseLightTheme` (Plan 04 wires this through the theme manager; Plan 01 hard-codes `TrayIcon.ico` and accepts that the tray icon may not perfectly match a dark-mode taskbar until Plan 04).

**Tests:** `dotnet build` fails if any of the three `.ico` source files is missing (build target verifies with a `<Message Importance="high">` + `<Error>`).

**Claude Code prompt:**

> Execute Plan 01 §S1b. Extract the contents of `stagehand-icons.zip` into `app/assets/brand/` preserving the folder structure from the zip (`assets/icons/...` → `app/assets/brand/icons/...`). Write `app/assets/brand/README.md` per the details above. Create `app/build/copy-brand-assets.targets` with the listed `Copy` tasks. Import it from `app/src/App.Shell/App.Shell.csproj` and add the three `<Resource>` entries. After `dotnet build`, confirm the three `.ico` files appear under `app/src/App.Shell/bin/.../Assets/`. Move `stagehand-icons.zip` to `app/assets/brand/icons/` for provenance (or delete if clearly no longer needed; user preference).

### S2 — Solution and projects

**Files**: `App.sln`, every `.csproj` listed in §15, plus empty `Program.cs`, `App.xaml`, `App.xaml.cs` for the App project.

**API**: none beyond `public partial class App : Application`.

**Tests**: build.

**Details**:

- `App.Interop.csproj`: library, no WPF.
- `App.Core.csproj`: library, references Interop.
- `App.Services.csproj`: library, references Core + Interop.
- `App.Shell.csproj`: WPF (`<UseWPF>true</UseWPF>`), `OutputType=WinExe`.
- `App.Tests.csproj`: `<IsPackable>false</IsPackable>`, references Core + Services (not Interop directly; uses fakes).
- `App.Harness.csproj`: WPF, references everything. Not part of CI test runs.

**Claude Code prompt**:

> Create the empty C# projects and the solution file exactly as specified in Plan 01 §S2 and §15. Add project references along the declared dependency direction (App → Services → Core → Interop). Confirm with `dotnet build` that the empty solution compiles.

### S3 — Interop stubs

**Files**: everything under `app/src/App.Interop/`.

**API**: see §Design.2, §Design.3. All interface methods defined; all stub classes throw `NotImplementedException("Implemented in Plan 02.")` (or 03, 04, 05 as appropriate).

**Tests**: one smoke test that `ServiceProvider.GetService<IWindowEnumerator>()` resolves to `NotImplementedWindowEnumerator` (see S10).

**Details**:

- `WindowSnapshot` record contains HWND, title, class, processId, processStartTimeUtcTicks (for identity stability), bounds, monitor HMONITOR.
- `Rect` is a custom struct with `X`, `Y`, `Width`, `Height` and conversions to/from `System.Drawing.Rectangle` (Interop does not reference WPF types).
- `NativeMethods.*.cs`: declare signatures that Plans 02–05 will consume, each annotated with `// consumer: plan 02 §X`. Include at minimum: `EnumWindows`, `GetWindowTextW`, `GetClassNameW`, `IsWindowVisible`, `GetWindowLongPtrW`, `GetWindowRect`, `SetWindowPos`, `SetForegroundWindow`, `GetWindowThreadProcessId`, `GetAncestor`, `MonitorFromWindow`, `EnumDisplayMonitors`, `SystemParametersInfoW` (SPI_SETWORKAREA/SPI_GETWORKAREA), `SetWinEventHook`, `UnhookWinEvent`, `DwmRegisterThumbnail`, `DwmUpdateThumbnailProperties`, `DwmUnregisterThumbnail`, `DwmQueryThumbnailSourceSize`, `DwmGetWindowAttribute` (DWMWA_CLOAKED).

**Claude Code prompt**:

> For `app/src/App.Interop/`, generate the interfaces, the `WindowSnapshot` record, the `Rect` struct, all SafeHandles (with `ReleaseHandle` returning false and a `TODO` comment for the implementing plan), and the `NotImplemented*` stub classes. Generate `NativeMethods.*.cs` with the P/Invoke signatures listed in Plan 01 §S3, each accompanied by a Microsoft Learn URL as an XML doc comment and a `// consumer: plan XX §Y` comment. Do NOT implement any wrapper logic.

### S4 — Core stubs

**Files**: everything under `app/src/App.Core/`.

**API**: typed exceptions, `StagePhase`, `StageState`, `ParkedWindow`, `SavedWorkArea`, `IStageController`, `IStageLayoutEngine`, `IWindowFilter`, `IClock`, `SystemClock`.

**Tests**: `StageStateTests` covering record equality and phase invariants (e.g. `Phase == Disabled` implies `Parked.IsEmpty`).

**Details**:

- Every exception has `SerializableAttribute` removed (legacy). Each has the standard three ctors.
- `StageState` initial value: `public static readonly StageState Empty = new(StagePhase.Disabled, ImmutableList<ParkedWindow>.Empty, ImmutableDictionary<string, IntPtr?>.Empty, ImmutableDictionary<string, SavedWorkArea>.Empty, ImmutableList<WindowIdentity>.Empty, IsPaused: false);`.

**Claude Code prompt**:

> For `app/src/App.Core/`, generate the types listed in Plan 01 §S4. Stub classes throw `NotImplementedException("Implemented in Plan 0X.")` with the correct target plan. Add unit tests `StageStateTests` in `tests/App.Tests/State/` that verify the phase invariants documented in Plan 01 §Design.6.

### S5 — Services: Settings (real) and Hotkey/Update (stub)

**Files**: everything under `app/src/App.Services/`.

**API**:

```csharp
public interface ISettingsService {
    AppSettings Current { get; }
    event EventHandler<AppSettings>? Changed;
    Task SaveAsync(AppSettings next, CancellationToken ct);
}
public interface ISettingsPathProvider { string SettingsFilePath { get; } }
```

**Details**:

- `AppSettings` is an immutable record with `General`, `Appearance`, `Behavior`, `Updates`, `Logging` sub-records. This plan ships empty sub-records; Plan 04 populates them.
- `SettingsService`:
  - Ctor: `(ISettingsPathProvider path, ILogger<SettingsService> log, IClock clock)`.
  - On construction, loads synchronously (settings must be available before DI resolution of dependents). If load fails, writes defaults.
  - `SaveAsync` serialises to JSON (`System.Text.Json`, pretty-printed, camel-case), writes `settings.json.tmp`, then `File.Replace`. Raises `Changed`.
  - Schema version checked on load; if higher than current code version, loads read-only defaults and logs a `Critical`.
- `SettingsMigrator`: `public AppSettings Migrate(JsonDocument raw, int targetVersion)`. No-op for v1.
- `StubHotkeyService` and `StubUpdateService` log a warning "not yet implemented" the first time any method is called.

**Tests** (`SettingsServiceTests`):

- Loads defaults on missing file.
- Saves and re-loads round-trips.
- Atomic write (simulate failure during write and verify original survives).
- Schema-version-too-high path.

**Claude Code prompt**:

> Implement `SettingsService`, `AppDataSettingsPathProvider`, and `SettingsMigrator` per Plan 01 §S5. Stub `HotkeyService` and `UpdateService` as described. Write `SettingsServiceTests` covering all four test cases listed. Use `System.IO.Abstractions` is NOT required; use real `File` + temp paths in tests.

### S6 — Logging configuration

**Files**: `app/src/App.Services/Logging/LoggingConfiguration.cs`.

**API**: `public static ILoggingBuilder AddStageManagerLogging(this ILoggingBuilder builder, AppSettings settings, string logRootPath)`.

**Details**:

- Configures Serilog with rolling file sink, retention 7 days, 5 MB per file, template `[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`.
- Reads per-category overrides from `settings.logging.levels`.
- In `#if DEBUG`, adds `WriteTo.Debug()`.

**Tests**: none (integration, covered by running the app).

**Claude Code prompt**:

> Implement `LoggingConfiguration.AddStageManagerLogging` per Plan 01 §S6. Wire it up from `App.OnStartup`.

### S7 — App composition root

**Files**: `Program.cs`, `App.xaml`, `App.xaml.cs`, `ServiceConfiguration.cs`.

**API**: `public static IServiceCollection ConfigureServices(this IServiceCollection services)`.

**Details**:

- `Program.cs`: `[STAThread] static int Main(string[] args) { ... }`. Parses `--verbose`, `--dry-run`, `--diagnostics`. Builds Host, resolves `App`, calls `.Run()`.
- `App.xaml.cs` `OnStartup`:
  1. Construct host.
  2. Register global exception handlers (§Design.7).
  3. Resolve `ISettingsService` to load settings synchronously.
  4. Resolve `TrayIconHost` and call `Start()`.
  5. Set `ShutdownMode = OnExplicitShutdown` so closing the Settings window doesn't exit the app.
- On shutdown: dispose host.

**Tests**: none (integration).

**Claude Code prompt**:

> Implement the composition root in `App.xaml.cs` and `ServiceConfiguration.cs` per Plan 01 §S7. Register every service listed in §Design.5. Stubs resolve to their `NotImplemented*` / `Stub*` implementations this plan.

### S8 — Tray icon

**Files**: `app/src/App.Shell/Tray/TrayIconHost.cs`, `Tray/TrayResources.xaml`, `Assets/TrayIcon.ico`.

**API**:

```csharp
public sealed class TrayIconHost : IDisposable {
    public TrayIconHost(ILogger<TrayIconHost> log, Func<SettingsWindow> settingsFactory);
    public void Start();
    public void Dispose();
}
```

**Details**:

- Uses `H.NotifyIcon.Wpf`.
- Context menu items (this plan):
  - Settings… → opens (or focuses) the Settings window.
  - Open Diagnostics → opens `%LOCALAPPDATA%\Stagehand\logs\` in Explorer.
  - About → MessageBox with version + build date.
  - ────
  - Exit → `Application.Current.Shutdown()`.
- Left-click toggles Stage Manager — **in this plan it only logs "TODO: toggle"** since `StageController` is a stub. Plan 03 wires it up.
- Icon: placeholder `.ico` with transparent 16/32/48 px frames.

**Tests**: none (integration; manual).

**Claude Code prompt**:

> Implement `TrayIconHost` using `H.NotifyIcon.Wpf` per Plan 01 §S8. The menu items "Settings…", "Open Diagnostics", "About", "Exit" are live. Left-click logs `"Stage toggle requested — wired in Plan 03"` at Information level and does nothing else.

### S9 — Empty Settings window

**Files**: `Settings/SettingsWindow.xaml`, `Settings/SettingsWindow.xaml.cs`.

**API**: `public partial class SettingsWindow : Window`.

**Details**:

- Title: "Stage Manager — Settings".
- Size: 800x600, min 640x480.
- Single `TextBlock`: "Settings will be implemented in Plan 04."
- Close button at the bottom right.
- Uses system title bar (Fluent theme polish deferred to Plan 04).

**Tests**: none.

**Claude Code prompt**:

> Create a minimal `SettingsWindow` per Plan 01 §S9. No MVVM, no bindings, no theming beyond system defaults. A single Close button.

### S10 — Smoke tests

**Files**: `tests/App.Tests/ProjectReferenceSmokeTests.cs`, `tests/App.Tests/Fakes/FakeClock.cs`.

**API**: tests only.

**Details**: one test per project that asserts a representative public type is reachable; one test that asserts `ServiceConfiguration.ConfigureServices(new ServiceCollection()).BuildServiceProvider()` resolves `IStageController`, `ISettingsService`, `IWindowEnumerator` without throwing (resolution, not invocation).

**Claude Code prompt**:

> Add smoke tests per Plan 01 §S10. These tests exist to detect wiring regressions, not to cover behaviour. Keep them under 30 lines each.

### S11 — GitHub metadata

**Files**: `.github/CODEOWNERS`, `.github/dependabot.yml`, `.github/pull_request_template.md`, `.github/ISSUE_TEMPLATE/{bug,feature,question}.yml`, `.github/ISSUE_TEMPLATE/config.yml` (redirect `security` → `SECURITY.md`).

**Details**:

- `CODEOWNERS`: `* @<the-maintainer>` (placeholder replaced on first commit).
- `dependabot.yml`: nuget + github-actions, weekly, grouped minor/patch.
- Issue templates are `.yml` forms with required fields (environment, reproduction steps, expected/actual).

**Claude Code prompt**:

> Generate the files under `.github/` per Plan 01 §S11.

### S12 — CI workflow

**Files**: `.github/workflows/ci.yml`, `.github/workflows/release.yml` (skeleton, `on: workflow_dispatch` only).

**Details**: see §Design.11.

**Claude Code prompt**:

> Write `.github/workflows/ci.yml` per the YAML snippet in Plan 01 §Design.11. Also scaffold `release.yml` as a manual-only placeholder that just prints "release workflow enabled in Plan 05".

### S13 — Governance & security documents

**Files**: `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `THIRD-PARTY-NOTICES.md`.

**Details**: content per §Design.12, §Design.13, §Design.14.

**Claude Code prompt**:

> Write the governance documents per Plan 01 §S13, drawing content from §Design.12–14. Use Contributor Covenant v2.1 verbatim for the Code of Conduct.

### S14 — Architecture documentation

**Files**: `docs/architecture/overview.md`, `docs/architecture/threat-model.md`, `docs/manual-tests/README.md`.

**Details**: `overview.md` is the permanent home for §Design.1–11. `threat-model.md` is the permanent home for §Design.13. `manual-tests/README.md` explains the manual-checklist convention.

**Claude Code prompt**:

> Migrate the architecture content from Plan 01 §Design into `docs/architecture/overview.md` and `docs/architecture/threat-model.md`, adapted to permanent-document tense (drop the "we will"). Plans under `docs/plans/` remain the living execution briefs; `docs/architecture/` is the reference doc.

### S15 — First-run smoke check

**Files**: none.

**Details**: manual: `dotnet run --project app/src/App.Shell`. Tray icon appears; right-click → Settings opens the placeholder window; right-click → Exit terminates cleanly. `dotnet test` is green. `dotnet format --verify-no-changes` is green.

**Claude Code prompt**:

> Run the smoke checklist in Plan 01 §S15 and paste the output. If anything fails, fix before proceeding to Plan 02.

---

## Risks & Mitigations

| Risk                                                                              | Impact                     | Mitigation                                                                                                                                                          |
| --------------------------------------------------------------------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Central package management fails to restore transitive versions deterministically | CI flaps                   | Use `packages.lock.json` in every project; CI restores with `--locked-mode`.                                                                                        |
| WPF app exits when settings window closes                                         | UX bug                     | `ShutdownMode = OnExplicitShutdown`; tray exit is the only exit path.                                                                                               |
| `H.NotifyIcon.Wpf` drops icon on Explorer restart                                 | Tray disappears            | Library handles this natively; regression test covered in manual checklist.                                                                                         |
| Interop interfaces not future-proof (e.g. need to change signature in Plan 02)    | Rework in 02               | Keep stubs minimal; prefer adding overloads over changing signatures. Interfaces shipped in this plan are documented as v1; breaking changes allowed until Plan 05. |
| Composition root grows past 50 lines                                              | Hard to reason about       | Delegate to `ServiceConfiguration` extension method; only lifecycle in `App.xaml.cs`.                                                                               |
| Contributors ignore Conventional Commits                                          | Release notes garbled      | Enforce in CI with `commitlint` GitHub Action (added later if needed). For now: documented and gently enforced in PR review.                                        |
| Vulnerable-package check fails CI on transient advisory                           | Blocks PRs                 | `dotnet list package --vulnerable` is a reporting step; failure is a warning. Dependabot raises PRs to fix. Convert to a gate after v1.                             |
| Someone commits a signing certificate                                             | Secret leak                | `.gitignore` excludes `*.pfx`, `*.snk`; secret scanning enabled on the repo.                                                                                        |
| Elevation boundary surprises users                                                | "App doesn't work" reports | `ElevationBoundaryException` is surfaced in Plan 02 UI with a clear message; documented in README.                                                                  |

---

## Verification

1. **Build gate.** `dotnet build -c Release` exits 0 with zero warnings on a clean clone.
2. **Test gate.** `dotnet test` passes all smoke tests and `SettingsServiceTests`.
3. **Style gate.** `dotnet format --verify-no-changes` exits 0.
4. **Vuln gate.** `dotnet list package --vulnerable --include-transitive` reports no Critical/High entries.
5. **Manual: tray smoke.** Run the App; tray icon appears; menu items all respond; Exit cleanly terminates the process (no zombie).
6. **Manual: settings smoke.** Open Settings from tray; window shows placeholder; Close button dismisses without exiting the app.
7. **Manual: logging.** Confirm `%LOCALAPPDATA%\Stagehand\logs\stagemanager-<date>.log` exists after one run.
8. **Repo hygiene.** All files listed in §15 exist; `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md` are non-placeholder.
9. **CI green.** Push to a branch, open a PR; CI runs to completion with all checks green.

---

## References

- `stage-manager-windows-plan.md` — sections "Tech Stack", "Architecture", "File Layout", "OSS Considerations", "Working With Claude Code".
- .NET 8 docs — <https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8>
- Central Package Management — <https://learn.microsoft.com/nuget/consume-packages/central-package-management>
- `dotnet format` — <https://learn.microsoft.com/dotnet/core/tools/dotnet-format>
- `packages.lock.json` — <https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies>
- Serilog rolling file sink — <https://github.com/serilog/serilog-sinks-file>
- `H.NotifyIcon.Wpf` — <https://github.com/HavenDV/H.NotifyIcon>
- Contributor Covenant v2.1 — <https://www.contributor-covenant.org/version/2/1/code_of_conduct/>
- Conventional Commits — <https://www.conventionalcommits.org/en/v1.0.0/>
- GitHub Private Vulnerability Reporting — <https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability>
- CycloneDX SBOM for .NET — <https://github.com/CycloneDX/cyclonedx-dotnet>
- SignPath for OSS — <https://signpath.org/support/for-open-source>
