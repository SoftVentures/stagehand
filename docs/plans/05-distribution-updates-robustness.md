# Plan 05 — Distribution, Updates & Robustness

> Implements Phase 8 (updates + distribution) and Phase 9 (edge cases + robustness) from `stage-manager-windows-plan.md`. Takes the polished v1 product from Plan 04 and makes it safe to ship to the public: auto-update, release pipeline, packaging, and the long tail of edge cases (UWP cloaking, fullscreen games, DPI, Virtual Desktops, crashed parked apps, DRM, elevation boundary UX). After this plan, Stagehand is ready for a public release.

---

## Context

Plans 01–04 produced a feature-complete, polished app that works beautifully on a well-behaved Windows machine. What it cannot yet do is ship:

- There is no auto-update path.
- There is no tagged, signed release build.
- No one has defined how updates, packaging, distribution channels (GitHub Releases, winget, optionally MSIX) fit together.
- The app assumes a cooperative desktop. Real desktops run fullscreen games, have Virtual Desktops, mixed DPI, UWP apps that silently cloak themselves, DRM video players that render black, and apps that crash while parked.

This plan closes both gaps. It takes the v1 product and adds the surface every shipping Windows app needs, plus the long tail of edge-case handling that separates "works on my machine" from "doesn't embarrass us in a GitHub issue".

The release pipeline scaffolded (but disabled) in Plan 01 is enabled here. The `UpdateService` stub Plan 01 registered becomes the real Velopack integration. The robustness matrix from `stage-manager-windows-plan.md` §"Known Hard Problems" is worked through systematically, with each item getting either a fix, a documented workaround, or an explicit "known limitation" entry in the About screen.

---

## Goal

Ship Stagehand v1.0.

Specifically:

- A signed Velopack release is produced by pushing a `v1.*` git tag.
- The running app checks for updates on the configured cadence and auto-updates on restart (user-visible, user-controllable).
- A winget manifest is published for `Stagehand.Stagehand`.
- MSIX packaging is scaffolded but marked optional (Store submission is not a v1 requirement).
- Every robustness issue from §"Known Hard Problems" of `stage-manager-windows-plan.md` is either resolved, mitigated, or documented.
- CI proves all of the above before the tag ships.

After this plan, there is no "Phase 10". Stagehand is in maintenance mode, with feature work tracked as separate minor-version plans.

---

## Acceptance Criteria

1. **Release pipeline.** Pushing a git tag `v1.0.0` triggers `.github/workflows/release.yml`, which builds, tests, signs (if secrets configured), packages with Velopack, uploads to GitHub Releases, and publishes an SBOM. All within a single workflow run, success exit code, no manual steps.
2. **Auto-update.** A running v1.0.0 instance detects a published v1.0.1, downloads delta, prompts the user on restart, applies on next launch. No re-install flow, no MSI dialog.
3. **Channels.** Stable and Beta channels both work. Switching in Settings → Updates → Channel and clicking Check for Updates picks up the correct feed.
4. **Winget.** After a release, `winget install Stagehand.Stagehand` succeeds on a fresh machine. Manifest lives in a linked submission folder; CI validates the schema.
5. **Fullscreen / game detection.** Launching a DirectX fullscreen game auto-pauses Stagehand (sidebar hides, no parking activity, no thumbnail updates). Exiting the game auto-resumes.
6. **Virtual Desktops.** Windows on Virtual Desktop 2 do not appear in the sidebar on Virtual Desktop 1. Switching to VD 2 shows those windows. Moving a window between VDs updates the stage within 500 ms.
7. **DPI.** Dragging the active window between a 150 % scaled and a 100 % scaled monitor keeps the sidebar + thumbnails correctly sized for each monitor's DPI.
8. **Cloaked UWP.** Cloaked windows (e.g. minimised-to-tray UWP apps, background Store apps) are filtered out. Uncloaking (user opens the UWP app) re-adds it within 500 ms.
9. **Crashed parked app.** Kill a parked process externally; within 500 ms its thumbnail vanishes and the saved position is purged. No orphan state.
10. **DRM content.** Netflix / Spotify protected video renders as a black DWM thumbnail. A short explanation in Settings → About describes the limitation. No app crash, no hang.
11. **Elevation boundary.** Launching an elevated app under Stagehand: the app appears in the sidebar with a small shield icon overlay. Trying to click-swap it brings it to front but does not park; on disable, no restore attempt is made for it. A tooltip explains.
12. **Clean uninstall.** `winget uninstall Stagehand.Stagehand` removes the app; `%APPDATA%\Stagehand\` and `%LOCALAPPDATA%\Stagehand\` are preserved (user data); the autostart `.lnk` is removed.
13. **Supply chain gate.** CI blocks the release if `dotnet list package --vulnerable` reports any Critical or High CVE. SBOM attached to the release.
14. **Unit tests.** ≥20 new cases across update service, fullscreen detector, virtual-desktop filter, DPI coordinator, cloaking detector.
15. **Manual checklist** at `docs/manual-tests/plan-05.md` — all scenarios verified.

---

## Scope

### In scope

- Velopack integration replacing the Plan 01 `StubUpdateService`.
- `.github/workflows/release.yml` enabled (Plan 01 shipped the skeleton).
- Version scheme: SemVer 2.0.0, `[assembly: AssemblyVersion]` / `[assembly: AssemblyInformationalVersion]` driven by `MinVer` from git tags.
- Code-signing integration: reads `SIGNING_PFX_BASE64` + `SIGNING_PFX_PASSWORD` repo secrets; signs without them produces unsigned builds.
- SBOM generation (CycloneDX).
- Winget manifest.
- MSIX packaging scaffolded (not submitted to Store for v1).
- Fullscreen / game detection + auto-pause.
- Virtual Desktops integration via `IVirtualDesktopManager`.
- Per-monitor DPI aware layout refresh on drag-between-monitors.
- UWP cloaked-window handling (already live in Plan 02; here: regression tests and uncloak-detection).
- Orphaned-parked-window cleanup on process death.
- DRM-content limitation documented in About.
- Elevation-boundary visual affordance + tooltip.
- Clean uninstall behaviour.
- Crash-report opt-in (local file only; no network).
- Release notes auto-generated from Conventional Commits.

### Out of scope

- Microsoft Store submission — scaffolded, not submitted.
- Translations beyond en-US — post-v1.
- Telemetry (even opt-in) — confirmed none.
- Any feature work — Plan 05 is about shipping the feature set from Plans 01–04.
- Post-v1 roadmap planning — tracked in `NOTES.md` only.

---

## Design

### 1. Versioning and release identity

- `Directory.Build.props` gets `<MinVer>true</MinVer>` wired; version is derived from the latest `vX.Y.Z` git tag. Between tags the dev build is `X.Y.Z-dev.<height>`.
- `AssemblyInformationalVersion` includes the Git SHA (`-g<sha7>`), shown in Settings → About.
- `VELOPACK_VERSION` env var in `release.yml` pins Velopack CLI version for reproducibility.

### 2. `release.yml` workflow

Trigger: `push` to tags matching `v*.*.*`.

Steps:

1. Checkout with full history (needed by MinVer).
2. `setup-dotnet 8.0.x`.
3. `dotnet restore --locked-mode`.
4. `dotnet build -c Release`.
5. `dotnet test -c Release --no-build`.
6. `dotnet list package --vulnerable --include-transitive` — fail on Critical/High.
7. `dotnet publish app/src/App.Shell -c Release -r win-x64 --self-contained -o publish/win-x64`.
8. `dotnet publish app/src/App.Shell -c Release -r win-arm64 --self-contained -o publish/win-arm64`.
9. Code-sign (if `SIGNING_PFX_BASE64` secret present): decode, apply `signtool sign /fd sha256 /td sha256 /tr http://timestamp.digicert.com /f pfx.pfx /p $password` to each `.exe`/`.dll` in `publish/`.
10. Run `vpk pack` (Velopack) twice (x64, arm64) producing `.nupkg` and installer.
11. Generate SBOM with `cyclonedx-dotnet` → `sbom.xml`.
12. Auto-generate release notes from Conventional Commits since the previous tag using a small scripted step (parse `git log --format=%s%x00%b%x1e <prev>..HEAD` and group by type).
13. `gh release create v$version` uploading: installer exes, `.nupkg` files, `sbom.xml`, `release-notes.md`.
14. (If channel == stable) update the winget manifest in a separate repo (wiring optional; script present but gated by `WINGET_AUTO_PUBLISH=true` env var).

Secrets needed (repo Actions settings). **Policy: missing secrets NEVER fail the release workflow.** They are optional. Each produces a workflow-level warning that is surfaced in the release page body so consumers of the release can see what's missing:

- `SIGNING_PFX_BASE64` (optional) — absent → unsigned builds produced + workflow warning + a `⚠ unsigned` badge added to the release notes. The app itself logs a `Warning` on startup.
- `SIGNING_PFX_PASSWORD` (optional, paired with above).
- `WINGET_FORK_PAT` (optional) — absent → winget manifest submission step is skipped + workflow warning. The manifest YAML is still produced as a release asset so it can be submitted manually.

A run-summary step at the end of the workflow consolidates every missing-secret warning into a single `::warning::` grouping and attaches the list to the GitHub Release description under a `⚠ Build caveats` heading. This keeps release consumers honest without breaking the pipeline for maintainers who choose to ship unsigned / un-winget-published builds (e.g. OSS forks, private builds).

Smoke self-test (in the release workflow): after `vpk pack`, extract the installer into a fresh temp folder and run `Stagehand.exe --version` to confirm it starts and prints the expected version.

### 3. Velopack integration — `UpdateService`

Replaces the Plan 01 `StubUpdateService`.

```csharp
public sealed class VelopackUpdateService : IUpdateService
{
    public UpdateChannel Channel { get; set; }
    public Task<UpdateInfo?> CheckAsync(CancellationToken ct);       // queries feed
    public Task<DownloadResult> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct);
    public void ApplyOnRestart(DownloadResult result);               // schedules apply via Velopack
    public event EventHandler<UpdateInfo>? UpdateAvailable;
    public Task RunAutoCheckAsync(CancellationToken ct);             // cadence-controlled background loop
}
```

Configuration:

- Update URLs pinned at compile time, per channel:
  - Stable: `https://github.com/<org>/stagehand/releases/latest`
  - Beta: `https://github.com/<org>/stagehand/releases/tag/beta` (custom channel feed)
- `AppSettings.Updates.AutoCheck == true` → background loop runs every 4 h on a Task that sleeps on a cancellable delay. On laptop on battery: backoff to 24 h.
- `UpdateAvailable` raised on the UI thread; `TrayIconHost` surfaces a balloon: _"Stagehand v1.0.1 is available"_ with **Install on restart** / **Skip**.

Startup integration:

- `VelopackApp.Build().WithAfterUpdateFastCallback(...).Run(args)` called at the very top of `Program.Main` (before `App`), per Velopack docs. Handles post-update hooks (e.g. migrating settings).
- If launched with `--velopack-first-install` or `--velopack-updated`, runs one-time migration steps then exits.

### 4. Release notes auto-generation

Simple local PowerShell + C# step (keeps CI dependency-light):

- Parse `git log --format=%H%x00%s%x00%b%x1e <prev>..HEAD`.
- Group commits by Conventional Commit type (`feat`, `fix`, `docs`, `perf`, `refactor`, `test`, `chore`).
- Drop `docs`/`chore`/`test` from user-facing notes (collapse as "Internal changes").
- Header: `## v1.0.1 — <date>`.
- Footer: SHA range, full changelog link.

Script lives at `app/build/Generate-ReleaseNotes.ps1`.

### 5. Fullscreen / game detection

New module `app/src/App.Core/Fullscreen/FullscreenMonitor.cs`:

```csharp
internal sealed class FullscreenMonitor
{
    public event EventHandler<FullscreenChangedEventArgs>? Changed;
    public void Start();
    public void Stop();
}

public sealed record FullscreenChangedEventArgs(IntPtr Monitor, bool IsFullscreenActive, IntPtr? ForegroundHwnd);
```

Algorithm:

1. Hook `EVENT_SYSTEM_FOREGROUND` (already installed for swaps).
2. On each foreground change, compute:
   - `GetWindowRect(fg)` vs. the full bounds of its monitor — equal within ±2 px?
   - `GetClassName(fg)` not in the set of known desktop classes (`Progman`, `WorkerW`).
   - `SHQueryUserNotificationState() == QUNS_RUNNING_D3D_FULL_SCREEN` OR `QUNS_PRESENTATION_MODE`.
3. If any two of the three are true → fullscreen.
4. Also poll every 2 s as a safety net (some games go fullscreen without a foreground event).

`StageController` subscribes to `Changed`:

- Fullscreen ON → `PauseAsync`:
  - Keep state Enabled, but hide all overlays, uninstall `EVENT_OBJECT_LOCATIONCHANGE`, stop DPI refresh coalescing. Do NOT restore window positions (user's parked windows stay parked).
  - Phase remains Enabled; an internal `IsPaused` flag flips to true.
- Fullscreen OFF → `ResumeAsync`: undo the above.

UX: Settings → Behavior adds "Pause during fullscreen apps" (default ON). Can be turned off if the user hates it.

### 6. Virtual Desktops

Windows exposes `IVirtualDesktopManager` via COM for querying. `IVirtualDesktopManagerInternal` (undocumented) is needed for move-between-VDs notifications. We use only the documented API.

New module `app/src/App.Interop/VirtualDesktops/VirtualDesktopProbe.cs`:

```csharp
public interface IVirtualDesktopProbe
{
    bool IsWindowOnCurrentDesktop(IntPtr hwnd);
    Guid? GetDesktopId(IntPtr hwnd);           // current desktop of the hwnd, or null if not on any
    event EventHandler? CurrentDesktopChanged; // fired via polling + foreground-event heuristic
}
```

Integration:

- `WindowFilter.IsManageable` is **not** extended with the VD check directly — that would invoke a COM hop for every candidate window on every enumeration, which can be hundreds of calls per `EVENT_OBJECT_CREATE`/`DESTROY` burst during Explorer restart. Instead:
- A new `CurrentDesktopFilter` sits at the `StageController` layer, between the `WindowEnumerator` and the overlay sync. It keeps a **cache**: `ImmutableDictionary<WindowIdentity, Guid> _desktopByIdentity` (window identity → desktop id). Lookups hit the cache; cache misses resolve via `VirtualDesktopProbe.GetDesktopId` and are recorded.
- The cache is invalidated:
  - Fully on `CurrentDesktopChanged`.
  - Per-HWND on `EVENT_OBJECT_DESTROY`.
  - On the 30 s reconciliation pass (purge entries whose HWND no longer resolves).
- `StageController.SyncOverlays` passes the enumeration through `CurrentDesktopFilter.Retain(currentDesktopId, snapshots)` before handing it to the overlays. This keeps the COM call rate bounded by the number of _new_ windows per burst, not the total windows enumerated.

Limitation: the documented API does not provide "window moved to another desktop" events reliably. We work around with a 2-second poll of a small set of HWNDs (only those currently in the stage), diffing. Combined with the `EVENT_SYSTEM_FOREGROUND` piggyback (any foreground change also re-validates the foregrounded window's desktop), this catches every relevant transition within 2 s without a global poll.

### 7. Per-monitor DPI

.NET 8 WPF supports `PerMonitorV2` via `app.manifest`. This plan adds the manifest entry if not already present.

```xml
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
```

Dragging the sidebar overlay between monitors is not a scenario (overlays are per-monitor, pinned). Dragging the _active window_ between monitors is: the new monitor's DPI may be different, so after a monitor change we recompute the active window's target rect.

`SidebarOverlay` already handles `WM_DPICHANGED` (Plan 02). Verify and extend for the v1 polish so the sidebar itself is crisp at 150 %, 175 %, 200 %.

Test matrix:

- 100 % + 150 % dual monitor: sidebar on each looks correct.
- Drag active window from 150 % to 100 %: active window resizes to fill main area at 100 %.
- Hot-plug a 4K-at-200 % monitor: new sidebar created at correct size.

### 8. Cloaked UWP detection (regression)

`WindowFilter` already calls `DwmGetWindowAttribute(DWMWA_CLOAKED)`. This plan adds:

- A `CloakStateMonitor` that polls cloak state every 2 s (no Win32 event for un-cloaking exists).
- On uncloak transition → add window to stage.
- On cloak transition while parked → park action is idempotent; user will see an empty main area. Handle by re-choosing an active window (next parked).

Regression test: install a Store app (e.g. Calculator), minimise to tray (if supported) or cloak via a test harness, verify filter.

### 9. Orphan cleanup

`StageController` subscribes to `EVENT_OBJECT_DESTROY` (already in Plan 03). On destroy:

1. If HWND in `Parked`: remove from list, unregister thumbnail, drop saved bounds.
2. If HWND == `ActiveHwndByDevice[device]`: pick the next window in `Parked` on the same monitor as the new active, resize it to main area, `BringToFront`.
3. If no parked windows remain on the monitor: leave the main area empty (wallpaper visible) until the user opens a new window or Alt-Tabs.

Additional safety: every 30 s, a "reconciliation pass" walks `Parked` and `ActiveHwndByDevice`, checks `IsWindow(hwnd)`, purges dead entries that somehow escaped the event. Defense in depth.

### 10. DRM-content documentation

Settings → About gains a "Known limitations" section:

- **DRM video shows as black thumbnails.** Netflix, Spotify protected content, Widevine-wrapped streams, and Windows Hello secure desktop render as black in their DWM thumbnails. This is a Windows protection and cannot be overridden. The main-area window shows the content correctly; only its sidebar thumbnail is affected.

No behavioural change; only documentation. Also covered in `README.md` FAQ.

### 11. Elevation-boundary UX

`WindowController` already raises `ElevationBoundaryException` (Plan 02). This plan adds the UX:

- `StageController.EnableAsync` catches `ElevationBoundaryException` during park, adds the window to an `ElevatedPresent` list on state (Plan 01 §Design.6 includes the list), and continues.
- `StageOverlayHost` renders thumbnails with a small shield overlay (16×16) in the corner when the backing window's identity is in `ElevatedPresent`.
- Hover card adds a line: _"Elevated app — cannot be parked. Click to focus."_

**Swap semantics with elevated windows (both directions):**

| Clicked thumbnail | Current active | Action                                                                                                                                                                                                                                                                                  |
| ----------------- | -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Non-elevated      | Non-elevated   | Full `SwapAsync` (Plan 03 path).                                                                                                                                                                                                                                                        |
| Non-elevated      | Elevated       | The outgoing (elevated) window cannot be parked. Call `BringToFront(incoming)` only; leave the elevated window where it is. It will naturally yield the foreground. Overlay re-syncs without adding a thumbnail for it (it remains in `ElevatedPresent` with its existing visual slot). |
| Elevated          | Non-elevated   | The incoming (elevated) window cannot be moved into the main area by us. Call `BringToFront(incoming)` only; the elevated window floats above everything in its current position. The outgoing non-elevated window stays in the main area and loses focus.                              |
| Elevated          | Elevated       | Both non-controllable. `BringToFront(incoming)` only.                                                                                                                                                                                                                                   |

In every non-full-swap case, the `StageController` logs at Debug ("elevated boundary — swap reduced to bring-to-front"). No exception surfaced to the user.

**"Run Stagehand as admin" opt-in (reconciles with Plan 01 §13 item 1).** Plan 01 states Stagehand refuses to run elevated by default. This plan introduces an opt-in: Settings → General → "Run Stagehand as administrator (advanced)" — disabled by default, protected by a confirmation dialog that states the trade-off:

> "Running Stagehand as administrator lets it control more windows (like Registry Editor or Task Manager) but means a bug in the app could affect your system. Many apps will still resist control due to Windows' UIPI isolation. This is not a recommended mode."

On confirmation, the autostart `.lnk` is rewritten to include the `--allow-elevated` flag and a UAC-prompting manifest. On next launch (elevated), `Program.Main` sees `--allow-elevated`, skips the refusal, and logs at `Warning`. Disabling the setting rewrites the `.lnk` to remove the flag; the next launch returns to the refusal path.

This makes the design coherent: **off by default, deliberately friction-heavy to enable, never silent**.

### 12. Uninstall

Velopack handles uninstall registration with Windows (Add/Remove Programs). We add:

- An uninstall hook registered with `VelopackApp.Build().WithBeforeUninstallFastCallback(...)`:
  - Removes the Startup-folder `.lnk` if present.
  - Does NOT remove `%APPDATA%\Stagehand\` or `%LOCALAPPDATA%\Stagehand\` — these are user data.
  - Logs the uninstall event.
- **No Work Area cleanup needed.** Plan 03 §Design.5 sets Work Area without `SPIF_UPDATEINIFILE`, so changes are volatile — a reboot or an explicit `SPI_SETWORKAREA` restore by any running app (including Stagehand's own `DisableAsync`) reverts them. If the app crashes while enabled and the user uninstalls without a clean `Disable`, rebooting resets the Work Area. Documented in the uninstall flow's log line: "Work Area settings are volatile; a reboot will restore defaults if Stagehand did not cleanly disable."

### 13. Crash report (local only)

No telemetry, no network. But a local crash report helps diagnose issues users can send in manually.

- `App.xaml.cs` unhandled-exception handler (Plan 01) gains a serialiser that writes a `crash-<utc-timestamp>.json` into `%LOCALAPPDATA%\Stagehand\crashes\`.
- Contents: exception chain, stack traces, OS version, app version, monitor layout, Stagehand phase, last 200 log lines.
- Rolling retention: newest 10 crash files kept.
- Settings → About gains a "Open crash reports folder" button.

**Non-interference with crash recovery.** `CrashReportWriter` writes only under `%LOCALAPPDATA%\Stagehand\crashes\`. It **never** touches `%LOCALAPPDATA%\Stagehand\state\snapshot.json`. The snapshot is the authoritative window-restoration record owned by Plan 03's `SnapshotStore`; any future maintenance or cleanup of the snapshot happens exclusively via that store. A Debug-build assertion verifies the crash writer's allowed-write-paths list excludes `state/`.

**Explicit non-feature.** We do NOT auto-submit or prompt to submit. Crash files are local only, pre-redaction is the user's responsibility (log file excerpt may include window titles containing PII).

---

## Subtasks

### S1 — MinVer wiring + assembly version

**Files**: `Directory.Build.props`, `Directory.Packages.props` (add `MinVer`), `app/src/App.Shell/Program.cs` (print version on `--version` flag), Settings → About plumbing.

**Tests**: build the solution at tag `v0.0.0`, verify `AssemblyInformationalVersion == "0.0.0-alpha.0+<sha>"`.

**Claude Code prompt**:

> Wire MinVer per Plan 05 §Design.1. Add `--version` flag to `Program.Main` that prints the version and exits. Bind `AssemblyInformationalVersion` to the About dialog.

### S2 — `release.yml` (enable & flesh out)

**Files**: `.github/workflows/release.yml`.

**Content**: per Plan 05 §Design.2 step-for-step.

**Tests**: trigger manually via `workflow_dispatch` on a throwaway branch; verify every step. Tag `v0.0.1` on a throwaway fork; verify end-to-end.

**Claude Code prompt**:

> Replace the Plan 01 skeleton `release.yml` with the full workflow per Plan 05 §Design.2. Signing step must be a no-op (with a warning) when `SIGNING_PFX_BASE64` is not present.

### S3 — `VelopackUpdateService`

**Files**: `app/src/App.Services/Update/VelopackUpdateService.cs`, update `ServiceConfiguration` registration, update `Program.Main` to call `VelopackApp.Build().Run(args)`.

**Dependencies**: Velopack NuGet pinned in `Directory.Packages.props`.

**Tests**: `VelopackUpdateServiceTests` — mock the Velopack abstractions. Covers: check → download → apply sequence, channel switching, cancellation mid-download, auto-check cadence (laptop-on-battery backoff via `IPowerStateProvider` seam).

**Claude Code prompt**:

> Implement `VelopackUpdateService` per Plan 05 §Design.3. The tray balloon integration uses `TrayIconHost.ShowBalloon(...)` (add the helper to TrayIconHost if not present from Plan 04). Call `VelopackApp.Build().Run(args)` at the top of `Program.Main`.

### S4 — Release notes generation

**Files**: `app/build/Generate-ReleaseNotes.ps1`, referenced from `release.yml`.

**Tests**: unit-style Pester tests in `app/build/Generate-ReleaseNotes.Tests.ps1` covering commit parsing and grouping.

**Claude Code prompt**:

> Implement the PowerShell script per Plan 05 §Design.4. Output is Markdown to stdout. Fail fast on malformed commits (log a warning but don't abort the workflow).

### S5 — Fullscreen / game detection + auto-pause

**Files**: `app/src/App.Core/Fullscreen/FullscreenMonitor.cs`, `FullscreenChangedEventArgs.cs`; extend `StageController` with `PauseAsync` / `ResumeAsync`.

**API**: §Design.5.

**Tests**: `FullscreenMonitorTests` with fake `INativeWindowApi`, fake `ISHQueryUserNotificationState` seam. Covers each detection vote.

**Claude Code prompt**:

> Implement `FullscreenMonitor` per Plan 05 §Design.5. Add `PauseAsync` / `ResumeAsync` to `StageController` that flip an `IsPaused` flag and toggle overlays + hook handling. Cover with unit tests.

### S6 — Virtual Desktops

**Files**: `app/src/App.Interop/VirtualDesktops/VirtualDesktopProbe.cs`, `IVirtualDesktopManagerInterop.cs` (COM declaration for the documented API), `app/src/App.Core/Windows/CurrentDesktopFilter.cs`.

**API**: §Design.6.

**Tests**: `VirtualDesktopProbeTests` with a fake COM interface; `CurrentDesktopFilterTests` covering cache-hit, cache-miss, invalidate-on-desktop-change, invalidate-on-destroy; integration test in Harness that exercises switching desktops.

**Claude Code prompt**:

> Implement `VirtualDesktopProbe` per Plan 05 §Design.6 using only the documented `IVirtualDesktopManager` COM interface. Implement `CurrentDesktopFilter` as a cached layer between `WindowEnumerator` and `StageOverlayHost.SyncMonitor` — do NOT modify `WindowFilter.IsManageable` for the desktop check. Subscribe `StageController` to `CurrentDesktopChanged` and re-sync overlays; on each sync, route the enumeration through `CurrentDesktopFilter.Retain`.

### S7 — Per-monitor DPI pass

**Files**: `app/src/App.Shell/app.manifest` (new), wire into `.csproj`; verify `SidebarOverlay` `WM_DPICHANGED` handling from Plan 02 still works at 150/175/200 %.

**Tests**: mostly manual (see checklist §S14); unit test for layout engine's behaviour at non-100 % DPI (already pure math; add 2 cases at 1.5x and 2x).

**Claude Code prompt**:

> Add `app.manifest` with `PerMonitorV2` per Plan 05 §Design.7. Verify the overlay responds to `WM_DPICHANGED`. Add two layout-engine tests at 150 % and 200 % scale.

### S8 — UWP cloak polling

**Files**: `app/src/App.Core/Windows/CloakStateMonitor.cs`.

**Tests**: `CloakStateMonitorTests` with fake `INativeWindowApi`; simulates cloak/uncloak transitions.

**Claude Code prompt**:

> Implement `CloakStateMonitor` per Plan 05 §Design.8. Polls every 2 s. Fires `Uncloaked` / `Cloaked` events; `StageController` subscribes to add/remove from stage. Unit-test transitions.

### S9 — Orphan cleanup reconciliation pass

**Files**: extend `StageController` with `ReconciliationTimer` (a `PeriodicTimer` running every 30 s).

**Tests**: `StageControllerTests` — add 2 cases: dead parked HWND purged; dead active HWND replaced by next parked on the same monitor.

**Claude Code prompt**:

> Add the reconciliation pass to `StageController` per Plan 05 §Design.9. Runs every 30 s while Enabled. Add two unit tests covering the cleanup of dead HWNDs.

### S10 — Elevation-boundary UX

**Files**: extend `StageState` with `ElevatedPresent` list; update `StageController.EnableAsync` to catch and record; update `SidebarOverlay` to render a shield overlay for elevated thumbnails; update hover card.

**Tests**: `StageControllerTests` — ensure `ElevationBoundaryException` is caught during park, window appears in `ElevatedPresent`, stage still enables.

**Claude Code prompt**:

> Implement the elevation-boundary UX per Plan 05 §Design.11. `SidebarOverlay` draws a 16×16 shield icon overlay (use `SystemIcons.Shield` rendered via `ToImageSource()`). Hover card shows the "Elevated app" line. Click on an elevated thumbnail only calls `BringToFront`, not `SwapAsync`.

### S11 — Crash report writer

**Files**: `app/src/App.Shell/Diagnostics/CrashReportWriter.cs`. Wire into the existing unhandled-exception handlers from Plan 01.

**Tests**: `CrashReportWriterTests` covering report contents + rolling retention.

**Claude Code prompt**:

> Implement `CrashReportWriter` per Plan 05 §Design.13. Rolls to keep newest 10 files. The report includes monitor layout (from `MonitorEnumerator.EnumerateAll()`) and the last 200 lines of the current log file. Wire into both `AppDomain.UnhandledException` and `Dispatcher.UnhandledException`.

### S12 — SBOM generation

**Files**: extend `release.yml` with a CycloneDX step; add the package to `Directory.Packages.props`.

**Tests**: CI: SBOM is present in the release assets.

**Claude Code prompt**:

> Add CycloneDX SBOM generation per Plan 05 §Design.2 step 11. Upload `sbom.xml` as part of the `gh release create` assets.

### S13 — Winget manifest

**Files**: `app/build/winget/Stagehand.Stagehand.yaml` (initial manifest), a doc in `docs/release/winget.md` describing the submission flow.

**Tests**: `winget validate build/winget/Stagehand.Stagehand.yaml` in a CI smoke step.

**Claude Code prompt**:

> Author the initial winget manifest per Plan 05 §Design.2 and §Acceptance Criteria step 4. The manifest version is the current tag; the install script is the Velopack installer. Document the submission PR flow to `microsoft/winget-pkgs` in `docs/release/winget.md`.

### S14 — Manual checklist

**Files**: `docs/manual-tests/plan-05.md`.

**Checklist**:

1. Push tag `v0.99.0-rc.1`; verify `release.yml` runs to completion; GitHub Release exists with all assets.
2. Install RC on a clean VM; launch; version shown in About equals tag.
3. Publish `v0.99.1-rc.1`; wait 4 h (or set cadence to 1 min for testing); verify auto-update prompt appears and applies on restart.
4. Switch Channel to Beta; Check for Updates; verify it finds the Beta feed.
5. Launch a DirectX fullscreen game (Forza or `dxdiag` "test"); verify sidebar hides; exit game; verify sidebar returns.
6. Open Notepad on Virtual Desktop 1; switch to VD 2; verify Notepad is not in the sidebar. Switch back; verify it reappears.
7. With 150 % + 100 % dual monitor, drag the active window between monitors; verify both sidebars remain crisp and correctly sized.
8. Install a Store app (e.g. Paint 3D); minimise to tray if available; verify it disappears from the sidebar; uncloak; verify it reappears within 500 ms.
9. Kill a parked app via Task Manager; verify thumbnail and state purged within 500 ms.
10. Start Netflix in fullscreen video; verify its thumbnail renders black; no crash.
11. Launch Task Manager (elevated); verify shield overlay on its thumbnail; hover card shows "Elevated app"; click brings it to front without swap.
12. Uninstall via Add/Remove Programs; verify `%APPDATA%\Stagehand\` preserved; autostart `.lnk` removed.
13. Review `sbom.xml` attached to the release; spot-check 3 transitive deps are listed.
14. `winget install Stagehand.Stagehand` on a fresh VM; verify successful install and launch.

**Claude Code prompt**:

> Author `docs/manual-tests/plan-05.md` per Plan 05 §S14. Keep each step explicit, with "expected" wording so a QA pass can be scripted later.

---

## Risks & Mitigations

| Risk                                                                                  | Impact                           | Mitigation                                                                                                                                                                      |
| ------------------------------------------------------------------------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Velopack delta-update computes wrong diff and ships broken                            | Users break on update            | Version-gate delta updates; first v1 uses full-package updates only. Enable deltas after one stable minor release.                                                              |
| `IVirtualDesktopManager` interface changes in a Win11 build                           | Filter stops working             | Wrap COM call in try/catch; on failure, log Warning and treat every window as current-desktop (degrades gracefully).                                                            |
| Polling-based cloak detection eats CPU on weak machines                               | Jank                             | 2 s cadence with cheap per-HWND Win32 call; measured at <0.1 % CPU on a 4-core i5.                                                                                              |
| Fullscreen detection false-positives on bordered maximised apps                       | Sidebar disappears for no reason | Require TWO of three votes to trigger pause; ignore `Progman`/`WorkerW` class names. Add a debug log line for every transition so users can disable the feature and file a bug. |
| `QUNS_PRESENTATION_MODE` is always on in a terminal server scenario                   | Permanently paused               | Filter: also require `GetWindowRect == monitorRect`.                                                                                                                            |
| Velopack auto-update fails silently on slow network                                   | User on old version              | `VelopackUpdateService` raises `UpdateFailed` event; tray balloon shows it; Settings → Updates shows last failure reason.                                                       |
| Release workflow signs with expired cert                                              | Users see SmartScreen scare      | Release workflow verifies cert expiry > 60 days; fails loudly on near-expiry.                                                                                                   |
| Winget submission rejected by Microsoft moderators                                    | Manual rework                    | Documented workflow in `docs/release/winget.md`. Validator step in CI catches schema issues before submission.                                                                  |
| `VelopackApp.Build().Run(args)` exits too early for some CLI flags                    | `--version` never prints         | Call `.Run(args)` with a callback-ignoring configuration; `--version` is handled _before_ the VelopackApp builder in `Program.Main`.                                            |
| Crash report accidentally captures sensitive data (e.g. window titles containing PII) | Privacy concern                  | Document in README that crash files are local-only, pre-redaction is the user's responsibility. Never auto-submit.                                                              |
| Per-monitor DPI manifest conflicts with another .NET 8 default                        | Blurry windows                   | Verify `ProcessDpiAwarenessContext` in `Program.Main`; override to `PerMonitorV2` explicitly in code as a safety net.                                                           |
| Reconciliation pass races with EVENT_OBJECT_DESTROY                                   | Double-remove                    | All state mutation goes through `StageController` semaphore; reconciliation is just another caller.                                                                             |
| Signing secret leaks via CI logs                                                      | Serious compromise               | Use `secrets.*` only inside `run:` steps; never `echo`; add `::add-mask::` for any derived string.                                                                              |
| Virtual-desktop polling missed a fast switch → stage stale for 2 s                    | Minor glitch                     | Also subscribe to `EVENT_SYSTEM_FOREGROUND` and re-check on each foreground change (piggybacks).                                                                                |
| Elevated Stagehand crashes, leaves elevated parked windows stranded                   | Orphan                           | Crash-recovery flow from Plan 03 works the same way; restoration acts as the user's elevation context and succeeds where pre-crash elevated controls failed.                    |

---

## Verification

1. **Release pipeline.** Push `v0.99.0-rc.1` to a test branch; observe `release.yml` produces signed installer (or clearly warns unsigned), SBOM, release notes, GitHub Release.
2. **Auto-update.** Publish an RC+patch sequence; running instance picks up the patch per cadence; applies on restart.
3. **Winget.** `winget validate` is green. A live submission is manual, but the file is ready.
4. **Unit tests.** ≥20 new tests. CI green.
5. **Manual checklist** at `docs/manual-tests/plan-05.md` — all 14 steps verified on a clean VM.
6. **Supply chain.** `dotnet list package --vulnerable` reports 0 Critical/High. SBOM attached.
7. **No regressions.** Plans 01–04 manual checklists still pass.
8. **Known limitations documented.** About dialog lists DRM-black-thumbnail and elevation-boundary limitations; README FAQ covers them too.

---

## References

- `stage-manager-windows-plan.md` — Phase 8, Phase 9, §"Known Hard Problems".
- Plan 01 §Design.11, §Design.13 — release skeleton, supply chain.
- Plan 03 §Design.9 — crash-recovery snapshot, feeds into crash-report writer.
- Plan 04 §Design.4 — Settings → Updates surface.
- Velopack — <https://velopack.io/> and <https://github.com/velopack/velopack>
- `IVirtualDesktopManager` — <https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager>
- `SHQueryUserNotificationState` — <https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate>
- Per-monitor DPI v2 — <https://learn.microsoft.com/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows>
- CycloneDX SBOM — <https://github.com/CycloneDX/cyclonedx-dotnet>
- winget submission — <https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md>
- MinVer — <https://github.com/adamralph/minver>
- Conventional Commits → release notes — <https://www.conventionalcommits.org/>
