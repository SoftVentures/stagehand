# Stagehand — decisions log

A running record of choices made during implementation. Each entry should
note **what**, **why**, and (if relevant) **when to revisit**.

## Naming

- **Product name: Stagehand.** Decided. Windows-native homage to macOS Stage
  Manager; short, pronounceable, distinct. User-facing strings (tray tooltip,
  window titles, About dialog, installer display name) use this exact casing.
- **Physical code layout is brand-neutral.** Folders, projects, namespaces,
  assembly names all use the `App.*` prefix (`App.Core`, `App.Interop`,
  `App.Services`, `App.Shell`). The solution file is `App.sln`. A future
  rebrand does **not** require renaming source files, folders, csproj files,
  or namespaces — it only touches the runtime strings, which are centralised
  in `app/src/App.Core/Branding/BrandConstants.cs`, plus prose in Markdown
  docs and a handful of GitHub URLs. Migration of existing user data
  (`%APPDATA%\Stagehand\` → `%APPDATA%\<new-brand>\`) is a one-time code
  addition at rebrand time; noted in the `BrandConstants` class docs.
- **`App` class renamed to `ShellApp`.** The WPF-generated `App.g.cs` would
  otherwise produce a name-resolution ambiguity between the `App` namespace
  segment and the `App` class. `ShellApp : Application` avoids that without
  weakening the `App.X` prefix scheme.

## Deferred questions

- **Installer: MSIX vs Inno Setup vs WiX.** Deferred to Plan 05
  (distribution & updates). Stage 1 only needs the app to build and run from
  `dotnet run`; packaging choice doesn't affect the code layout.

## Dependency pins worth knowing

- **GitHub Actions majors.** Workflow uses `actions/checkout@v6`,
  `actions/setup-dotnet@v5`, `actions/setup-node@v6`, `actions/cache@v5`,
  `actions/upload-artifact@v7` — all latest stable majors as of 2026-04-24.
  Dependabot `github-actions` ecosystem keeps these on the current major.
- **Node.js 24 LTS (Krypton).** CI and the local dev prerequisite both target
  Node 24. Node 20 went end-of-life in April 2026, Node 22 is in maintenance,
  Node 24 is the active LTS through October 2026. `package.json` deps stay
  compatible back to Node 18 because Prettier 3.x requires it; we pin via
  `node-version: 24` only on the CI runner.
- **`FluentAssertions` 6.12.2 (not 7.x/8.x).** 7.0.0 relicensed under a paid
  commercial model; 6.12.x is the last Apache-2.0 release and is feature-complete.
- **`Serilog.Extensions.Logging` 8.0.0 (not 9.x).** 9.x requires
  `Microsoft.Extensions.Logging >= 9.0.0`; we stay on the .NET 8 LTS line.
  Revisit when the baseline moves to .NET 10 LTS.
- **`H.NotifyIcon.Wpf` 2.3.2 (not 2.4.x).** 2.4.x dropped the `net8.0-windows`
  TFM (now targets `.NETFramework4.6.2` + `net10.0-windows7.0`), which produces
  NU1701 on a `net8.0-windows` project and fails Release under
  `TreatWarningsAsErrors`. Revisit when moving to .NET 10.

## Follow-ups deferred to later plans

- **Plan 01 §S6 typo.** The plan text calls the Serilog extension
  `AddStageManagerLogging`; the implementation uses `AddStagehandLogging`
  (consistent with the brand). Plan doc to be updated in a follow-up PR.
- **Plan 01 §Design.5 DI registrations missing in Plan 01 code.** `UiDispatcher`
  singleton and factory variants of `IWinEventHook` / `IDwmThumbnail` are in
  the design table but not registered. Plan 02 needs the factories for
  per-event-range hooks — add them there rather than retrofitting the DI
  surface. `UiDispatcher` wrapper is a trivial Plan 03 add (`Dispatcher.CurrentDispatcher`).
- **Harness has no App.xaml.** `tests/App.Harness/Program.cs` is a stub
  Console-style `Main`. Plan 02 adds real harness scenarios and will own its
  own `App.xaml` at that point.
- **AssemblyInformationalVersion.** Plan 05 wires versioning via MinVer or
  GitVersion. Until then `TrayIconHost.ShowAbout` falls back to the assembly
  version `1.0.0.0` placeholder.

## Open options

- **Fullscreen-game detection heuristic.** Stage Manager's goal is to get
  out of the way when a game is running fullscreen. Candidates under
  consideration:
  - `SHQueryUserNotificationState` — simple, returns `QUNS_RUNNING_D3D_FULL_SCREEN`.
  - Manual check: foreground window covers a monitor, `WS_EX_TOPMOST`, exclusive
    mode via DWM composition flags.
  - Heuristic combination (process name list + window-rect + topmost).

  To be prototyped in Plan 03 once core stage mechanics work. No commitment
  yet; we'll pick based on false-positive rate observed in dogfooding.
