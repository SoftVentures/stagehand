# CLAUDE.md (`app/`)

Scope: source projects, brand assets, and MSBuild glue. Root [`../CLAUDE.md`](../CLAUDE.md) covers the big picture and the gotchas; this file lists conventions that apply inside `app/` only.

## Folder layout

```
app/
├─ src/
│  ├─ App.Interop/   # P/Invoke + SafeHandles + interop interfaces (no logic)
│  ├─ App.Core/      # Pure domain (no IO, no Win32, no WPF)
│  ├─ App.Services/  # IO, settings, logging (no WPF, no UI)
│  └─ App.Shell/     # WPF composition + tray + settings window
├─ assets/brand/     # Brand source of truth (icons/{colored,dark,light}/icon.{ico,svg})
└─ build/            # MSBuild .targets glue
```

## Per-project guardrails

| Project        | May reference                                                                   | Must **not** reference                                                                                        |
| -------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `App.Interop`  | BCL, `System.Drawing.Primitives`                                                | Anything from `App.*` above it                                                                                |
| `App.Core`     | `App.Interop`, BCL, `System.Collections.Immutable`                              | Any `System.Windows.*` (WPF), any `Microsoft.Extensions.*`                                                    |
| `App.Services` | `App.Core`, `App.Interop`, `Microsoft.Extensions.Logging.Abstractions`, Serilog | `System.Windows.*` (WPF)                                                                                      |
| `App.Shell`    | everything below                                                                | Nothing WPF-specific leaks the other way — `App.Shell` types must not appear in other assemblies' public APIs |

The compiler enforces project-reference direction. If you find yourself needing, say, `App.Services` to know about `TrayIconHost`, the design is wrong — add an interface in `App.Services` and register an implementation from `App.Shell`'s `ServiceConfiguration`.

## Adding a new class

1. Pick the correct project per the guardrail table. If unsure, ask: "does this touch Win32? → Interop. Time, state, validation? → Core. Files, logs, registry, network? → Services. Windows, dispatch, tray? → Shell."
2. Use file-scoped namespaces (`namespace App.Core.Stage;`). The folder path **must match** the namespace segment after the project root.
3. Nullable is on project-wide. Every reference type is non-nullable unless marked `?`.
4. If the class has dependencies, register it in `app/src/App.Shell/Composition/ServiceConfiguration.cs`. Use `AddSingleton` by default; only drop to `AddTransient` when each caller needs a fresh instance (e.g. `SettingsWindow`).
5. Every seam (anything a test might want to stub) gets an interface. Keep the interface in the same project as the implementation unless a higher-layer project needs to consume it without taking a reference on the lower project (rare).

## P/Invoke conventions (`App.Interop`)

- Prefer `[LibraryImport]` (.NET 8 source generator). Fall back to `[DllImport]` only when `LibraryImport` is awkward (e.g. delegate parameters, `StringBuilder` OUT buffers).
- The enclosing class in every `NativeMethods.*.cs` file is `internal static partial class NativeMethods` so all files contribute to one surface.
- Every signature carries an XML doc `<see href="https://learn.microsoft.com/..."/>` link to the Microsoft Learn page, plus a `// consumer: plan 0X §Y` comment indicating who will call it.
- Handles returned by native calls that need releasing get a `Safe*Handle` subclass of `SafeHandleZeroOrMinusOneIsInvalid`. `ReleaseHandle()` returns `false` with a `TODO: Plan 02` comment until the owning plan implements it.
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `App.Interop.csproj` is intentional — the `LibraryImport` source generator emits pointer marshalling stubs. Do not remove.
- `AssemblyInfo.cs` carries `[assembly: DefaultDllImportSearchPaths(System32)]` so individual signatures don't need the attribute. Keeps CA5392 quiet and hardens against DLL planting.

## Brand assets (`app/assets/brand/` + `app/build/`)

The `.ico` files under `app/src/App.Shell/Assets/` are **generated at build** by `app/build/copy-brand-assets.targets`. They are `.gitignore`d. Edit the sources in `app/assets/brand/icons/{colored,dark,light}/` instead; the `.svg` is the master, regenerate the `.ico` with ImageMagick or icotool after an SVG change.

The copy target also registers three `<Resource>` items **inside** the target (not at project scope). That detail matters — see the critical gotcha in root CLAUDE.md under "Non-obvious gotchas".

## WPF tips (`App.Shell`)

- The Application class is `ShellApp` (in `App.xaml.cs`), not `App`. `App.xaml` carries `x:Class="App.Shell.ShellApp"`.
- Use file-scoped namespaces in code-behind too. Keep XAML + code-behind paired under `Settings/`, `Tray/`, `Composition/` etc.
- User-visible strings (titles, tooltips, About dialog) must come from `App.Core.Branding.BrandConstants`, not XAML literals or C# string literals. XAML `Title="…"` is fine for internal / developer-only windows; anything a user sees reads from `BrandConstants` via code-behind (`Title = BrandConstants.SettingsWindowTitle;`).
- Logging uses cached `LoggerMessage.Define` delegates (see `TrayIconHost` and `ShellApp` for the pattern). CA1848 is enforced in Release.
- `ShutdownMode = OnExplicitShutdown` (App.xaml). Only `TrayIconHost.Exit` is allowed to call `Application.Current.Shutdown()`.

## Settings / logging / paths (`App.Services`)

- Never concatenate `"Stagehand"` into paths. Use `BrandConstants.ProductFolderName` via `AppDataSettingsPathProvider`.
- `SettingsService` is the only non-stub business logic in Plan 01. Atomic writes via `File.Replace`. Do not bypass `SaveAsync`.
- When you extend `AppSettings` in Plan 04, bump `AppSettings.CurrentSchemaVersion` and add a v0→v1 branch to `SettingsMigrator`.
- Log files land in `%LOCALAPPDATA%\Stagehand\logs\stagehand-YYYY-MM-DD.log`. Prefix via `BrandConstants.LogFilePrefix`.

## When to reach for the harness

`tests/App.Harness` exists for behaviour that cannot be unit-tested (real Win32, DWM, real windows). If you're about to write a "test" that needs a live HWND, that's harness territory — add a scenario there, not a new xUnit test.
