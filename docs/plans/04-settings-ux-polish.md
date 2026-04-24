# Plan 04 — Settings, UX & Polish

> Implements Phase 5 (settings window + persistence) and Phase 7 (polish + animation) from `stage-manager-windows-plan.md`. Turns the MVP from Plan 03 into a product that feels like one. Every hard-coded value in Plans 02 and 03 becomes a user setting, the sidebar gets acrylic/Mica, thumbnails get rounded corners and hover affordances, and the swap transition becomes the smooth "cover with a thumbnail" trick instead of an abrupt snap.

---

## Context

Plans 02 and 03 produced a working product with hard-coded defaults: sidebar width 200 px, left edge, no blur, opaque thumbnails, instant swap. The MVP is functional but unmistakably an internal build.

This plan bridges the gap between "works" and "feels finished". Two bodies of work are bundled because they reinforce each other:

1. **Settings**. A real MVVM Settings window, bound to the `AppSettings` schema Plan 01 scaffolded, surfacing every configurable behaviour (sidebar position/width/blur, animation speed, hotkey, auto-start, excluded apps, update channel). Changes apply live where reasonable.
2. **Polish**. Acrylic/Mica background on the sidebar, rounded thumbnail corners, drop shadows, hover cards (title + app icon), smooth swap animation, auto-hide sidebar, accessibility (high contrast, keyboard nav), and a first-run welcome window.

Bundling is deliberate: the polish decisions are the _surface area_ that settings expose. Designing the settings UI first without committing to the polish choices leads to boolean toggles for things that should be sliders (animation speed) or to settings for things that can't actually be toggled at runtime.

Nothing in this plan changes the lifecycle designed in Plan 03. If a polish feature (e.g. the swap animation) would require lifecycle changes, that's a sign to reopen Plan 03 first; the author of this plan should flag it rather than quietly work around.

---

## Goal

After this plan, Stagehand is a **v1.0-ready app**:

- Every behaviour a user might reasonably want to tweak is reachable through a polished Settings window.
- Settings changes apply live (no restart).
- The sidebar has acrylic/Mica blur with live wallpaper visible through it.
- Thumbnails are rounded, softly shadowed, and show a hover card with the window's app icon and title.
- Swaps glide in/out (≥60 fps on mid-range hardware) using the fake-thumbnail technique.
- A first-run welcome window introduces the product on install.
- Accessibility: keyboard navigation (`Ctrl+Alt+[ / Ctrl+Alt+]`) and high-contrast theme support.
- Everything is persisted atomically in `%APPDATA%\Stagehand\settings.json`.

No distribution / release work yet — that's Plan 05.

---

## Acceptance Criteria

1. **Every setting applies live.** Changing sidebar position from Left to Right, sidebar width from 200 to 260, blur from None → Acrylic → Mica, theme from System → Dark, excluding an app — all take effect without restarting the app or toggling Stagehand off and on.
2. **Persistence.** After changing every setting, close the app, reopen — every setting restored exactly. Atomic write survives a simulated crash during save (fault-injection test).
3. **Settings schema migration.** Loading a v0 (pre-Plan-04) settings file produces a v1 file with defaults filled in and no data loss.
4. **Acrylic (default) / Mica (experimental).** Sidebar background shows Acrylic on Windows 10 21H2+ and Windows 11. Mica is offered as an **experimental opt-in** (labelled "Mica (experimental — may fall back to Acrylic)") because Mica officially requires `WS_CAPTION` and the DWM frame extension, and our caption-less `WS_EX_NOACTIVATE` overlay does not reliably take it on all Windows 11 builds. The applicator probes for Mica success on enable; if the probe fails, it silently falls back to Acrylic and displays a one-time info toast explaining. Graceful fallback path: Mica → Acrylic → Tinted → None. Tinted opaque is the final fallback when DWM composition is disabled.
5. **Swap animation.** Clicking a sidebar thumbnail triggers a smooth 250 ms transition at ≥60 fps on a GTX 1060 / Ryzen 5. Frame times logged in `--verbose` mode; no frame exceeds 20 ms during the animation.
6. **Hover card.** Hovering a thumbnail for 400 ms shows a card with the window's title and 32×32 app icon, positioned beside the thumbnail, inside the monitor.
7. **Auto-hide.** With auto-hide on, the sidebar retracts after 1 s of no hover; it re-appears when the mouse comes within 4 px of the edge.
8. **Keyboard nav.** `Ctrl+Alt+]` cycles focus to the next sidebar thumbnail; `Ctrl+Alt+[` to the previous; `Ctrl+Alt+Enter` swaps it in. (Arrow-key bindings were considered and rejected: `Ctrl+Alt+ArrowLeft/Right/Up/Down` is the default Intel Graphics display-rotation hotkey on many systems and still enabled out-of-the-box in 2026.)
9. **First-run welcome.** Window appears exactly once per install (tracked by `general.firstRunCompleted` in settings). "Enable Stagehand now" and "Open Settings" buttons work; "Start with Windows" checkbox round-trips through the Startup folder.
10. **High contrast.** All Settings UI remains legible in High Contrast Black / White. Sidebar background falls back to system highlight tint.
11. **Hotkey.** Custom hotkey capture UX in Settings: click → "Press keys…" state → capture modifiers + key → save → active immediately. Conflict with an already-registered hotkey logs a clear error toast.
12. **Unit tests.** ≥25 new cases for settings/viewmodels/migrator/hotkey capture/animation state machine.
13. **Manual checklist** at `docs/manual-tests/plan-04.md` — all steps verified on both Windows 10 and Windows 11.

---

## Scope

### In scope

- Settings window MVVM with `CommunityToolkit.Mvvm`.
- Every `AppSettings` section populated with real fields and bound to the stage + overlay.
- `SettingsService` change-event plumbing to live-apply changes.
- Schema v0 → v1 migrator.
- First-run welcome window.
- Acrylic / Mica / opaque-tint fallback.
- Rounded corners, drop shadows, hover cards on thumbnails.
- Swap animation using the fake-thumbnail technique.
- Auto-hide sidebar.
- Keyboard navigation.
- High-contrast theming.
- Custom hotkey capture UI.
- Excluded-apps manager in Settings (browse running processes + manual add/remove).
- "Start with Windows" (Startup folder shortcut, not registry).

### Out of scope

- Auto-update integration → Plan 05.
- Release pipeline, code signing → Plan 05.
- Localisation beyond English (UI is structured for future localisation, but only an `en-US` resource file ships) — actual translations arrive post-v1.
- Themed title bars on Settings/Welcome windows using custom WinUI-style chrome — deferred.
- Custom thumbnail shapes (non-rectangular) — rectangular-with-rounded-corners only.
- Per-monitor sidebar configuration differences — Plan 04 ships global settings; per-monitor overrides post-v1.

---

## Design

### 1. Settings schema v1 (final)

`AppSettings.cs` in `App.Services.Settings` — concrete fields this plan populates. Plan 01 shipped the skeleton; this plan fills it:

```csharp
public sealed record AppSettings(
    int SchemaVersion,
    GeneralSection General,
    AppearanceSection Appearance,
    BehaviorSection Behavior,
    IReadOnlyList<ExcludedApp> ExcludedApps,
    UpdatesSection Updates,
    LoggingSection Logging);

public sealed record GeneralSection(
    bool StartWithWindows,
    bool FirstRunCompleted,
    HotkeySpec ToggleHotkey,   // default: Ctrl+Alt+S
    string Language);          // default: "en-US"

public sealed record AppearanceSection(
    SidebarEdge Edge,            // Left (default) | Right
    int SidebarWidthPx,          // default 200, range 160–320
    int ThumbnailSpacingPx,      // default 12, range 0–40
    int ThumbnailCornerRadiusPx, // default 8, range 0–24
    bool ShowAppIconOverlay,     // default true
    bool ShowWindowTitleOnHover, // default true
    SidebarBackground Background,// None | Tinted | Acrylic (default) | MicaExperimental
    double BackgroundBlurStrength,// 0.0-1.0, default 0.6
    ThemeMode Theme);            // System (default) | Light | Dark

public sealed record BehaviorSection(
    MultiMonitorMode MultiMonitor, // Independent (default) | PrimaryOnly
    bool AutoHideSidebar,          // default false
    AnimationSpeed AnimationSpeed, // Off | Fast | Normal (default) | Slow
    NewWindowPolicy OnNewWindow,   // ActivateIt (default) | AddToStage
    DisableRestorePolicy OnDisable);// RestorePositions (default) | LeaveInPlace

public sealed record ExcludedApp(string Identifier, ExcludeMatchKind Kind); // process name | class name

public sealed record UpdatesSection(
    UpdateChannel Channel,  // Stable (default) | Beta
    bool AutoCheck);        // default true

public sealed record LoggingSection(
    IReadOnlyDictionary<string, string> Levels); // category → level
```

Defaults chosen for sensible first-run feel: left sidebar, 200 px, acrylic blur, normal animation speed, multi-monitor independent, restore on disable.

### 2. Schema v0 → v1 migration

Plan 01 ships schema **v0** (`schemaVersion: 0`) — empty nested sections, placeholder shape. This plan ships schema **v1** with concrete fields and a real `v0 → v1` migrator inside `SettingsMigrator`:

- Parses the raw v0 JSON via `JsonDocument`.
- Reads every v0 leaf that survives in v1 (e.g. `general.startWithWindows`, `general.language`, `updates.channel`, `updates.autoCheck`, `excludedApps` array).
- Fills v1 defaults for every new field (§Design.1).
- Merges captured v0 values into the v1 defaults (defensive: if a user tweaked any v0 field in the scaffold, the value survives the migration).
- Writes the result with `schemaVersion: 1` via atomic write.

If the loader sees a `schemaVersion` higher than the one it understands, it logs `Critical`, loads defaults read-only for the session, and does NOT overwrite the file (protects forward-compat). A real schema v2 (future) would bump to 2 and extend `SettingsMigrator.Migrate(JsonDocument raw, int targetVersion)` with an additional leg.

### 3. Settings window architecture

MVVM. One `MainViewModel` with `ObservableObject` sub-viewmodels per section. `CommunityToolkit.Mvvm` source-generators for observable properties and relay commands.

```text
SettingsWindow.xaml
 └── NavigationView (selector on the left)
      ├── General
      ├── Appearance
      ├── Behavior
      ├── Excluded Apps
      ├── Updates
      └── About
SettingsWindow.xaml.cs
 └── DataContext = MainSettingsViewModel

MainSettingsViewModel
 ├── GeneralViewModel
 ├── AppearanceViewModel
 ├── BehaviorViewModel
 ├── ExcludedAppsViewModel
 ├── UpdatesViewModel
 └── AboutViewModel
```

Binding: every observable property is wired to a setter that constructs a new `AppSettings` record (immutable), calls `ISettingsService.SaveAsync(...)`. The `Changed` event triggers the overlay / stage to re-read.

Validation: slider ranges are enforced by binding converters; invalid values are clamped before save. Hotkey conflicts (registration failure) surface as an inline error below the control.

Live-apply strategy:

- `ISettingsService.Changed` is subscribed by:
  - `StageOverlayHost` (for sidebar position/width/spacing/corner/background/blur/theme).
  - `StageController` (for multi-monitor mode, on-new-window policy).
  - `HotkeyService` (for toggle hotkey changes).
  - `AutoHideCoordinator` (for auto-hide on/off).
- Each subscriber applies the diff idempotently (no-op if the relevant field didn't change).

### 4. Settings window — sections in detail

#### General

- **Enable Stagehand** toggle (mirrors tray state).
- **Start with Windows** toggle (Startup-folder `.lnk`).
- **Toggle hotkey** (custom-capture control, §9).
- **Language** dropdown (only `en-US` for now; scaffolded for future).
- **Reset all settings** button (confirmation dialog → writes defaults).

#### Appearance

- **Sidebar position**: Left / Right radio.
- **Sidebar width**: slider 160–320 (live preview in the real sidebar).
- **Thumbnail spacing**: slider 0–40.
- **Thumbnail corner radius**: slider 0–24.
- **Show app icon overlay**: toggle.
- **Show window title on hover**: toggle.
- **Sidebar background**: None / Tinted / Acrylic (default) / Mica (experimental) radio. The Mica radio is labelled "Mica (experimental)" and is disabled on non-Windows-11 or builds where the Mica probe has previously failed. Tooltip: "Mica requires Windows 11 and may fall back to Acrylic on caption-less overlays. Click anyway to try."
- **Blur strength**: slider 0.0–1.0 (active only for Acrylic).
- **Theme**: System / Light / Dark radio.

#### Behavior

- **Multi-monitor**: Independent / Primary only radio.
- **Auto-hide sidebar**: toggle.
- **Animation speed**: Off / Fast / Normal / Slow radio.
- **On new window**: Activate it / Add to stage radio.
- **On disable**: Restore positions / Leave in place radio.

#### Excluded Apps

- DataGrid with columns: Identifier, Kind (Process / Class).
- **Add by process**: dropdown of currently running processes (via `Process.GetProcesses()`).
- **Add by class name**: text input.
- **Remove**: button per row.
- Default exclusions seeded on first run (filled in by Plan 05 edge-case handling; here: empty list + placeholder comment).

#### Updates

- Current version + build date + commit hash (read from assembly metadata).
- **Check for updates** button (calls `IUpdateService.CheckAsync` — stub throws "Available in v1.1"; real impl in Plan 05).
- **Auto-check on startup**: toggle.
- **Channel**: Stable / Beta radio.

#### About

- Logo, product name, version.
- Links: GitHub repo, issue tracker, docs (all use `Process.Start` with a validated URL).
- Open-source licence notice + link to `THIRD-PARTY-NOTICES.md`.
- "DRM content will render as black thumbnails" disclaimer (from §Plan 05 §Design).

### 5. Acrylic / Mica / Fallback

`SidebarBackgroundApplicator` — applies the right system composition based on OS and user choice:

```csharp
internal sealed class SidebarBackgroundApplicator
{
    public void Apply(Window window, SidebarBackground kind, double strength);
}
```

- **MicaExperimental** (Win 11 22H2+, opt-in): attempt `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW)` after extending the frame via `DwmExtendFrameIntoClientArea` with negative margins and setting `DWMWA_USE_IMMERSIVE_DARK_MODE`. Microsoft's documented Mica path expects `WS_CAPTION`, which our caption-less `WS_EX_NOACTIVATE` sidebar does not have — so this is best-effort. The applicator runs a **probe**: after applying, sample a test pixel via `GetPixel` or query `DwmGetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE)` to confirm the attribute took. If the probe returns the wrong value, fall back to Acrylic and persist a "Mica unavailable on this build" flag in memory so Settings can grey the option.
- **Acrylic (default)** (Win 10 18362+): `SetWindowCompositionAttribute(hwnd, new AccentPolicy { AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientColor = argb })`. `argb` computed from `strength` (higher strength = more saturation on a neutral tint).
- **Tinted** fallback: solid 40 %-alpha neutral-tinted brush applied to the WPF window `Background`. No Win32 composition trick.
- **None**: fully transparent, no composition. Wallpaper shines through directly with no blur.

**Automatic fallback matrix** (what the user actually gets):

| User choice         | Win 11 22H2+                   | Win 11 <22H2 | Win 10 21H2+ | Win 10 <21H2 | Composition disabled |
| ------------------- | ------------------------------ | ------------ | ------------ | ------------ | -------------------- |
| Mica (experimental) | Mica if probe OK, else Acrylic | Acrylic      | Acrylic      | Tinted       | Tinted               |
| Acrylic             | Acrylic                        | Acrylic      | Acrylic      | Tinted       | Tinted               |
| Tinted              | Tinted                         | Tinted       | Tinted       | Tinted       | Tinted               |
| None                | None                           | None         | None         | None         | Tinted               |

Detection: `OperatingSystem.IsWindowsVersionAtLeast(major, minor, build)` at runtime; composition availability via `DwmIsCompositionEnabled`. Results cached per session.

On fallback: log Info; surface a one-time toast to the user ("Mica unavailable on this build — using Acrylic"). The toast is suppressed on subsequent sessions via a "user was told" flag in settings.

### 6. Rounded corners, drop shadows, hover cards

Rounding:

- DWM thumbnails are rectangular. To fake rounded corners, we overlay a WPF `Border` with matching `CornerRadius` and `Background="{x:Null}"`, and use the Windows 11 rounded-window-corner API (`DwmSetWindowAttribute` with `DWMWA_WINDOW_CORNER_PREFERENCE`) on the overlay itself.
- For each thumbnail, render a WPF `Rectangle` with a `RadialGradientBrush` inner shadow, and use `DropShadowEffect` with `BlurRadius = 16` at 40 % opacity.
- Because thumbnails are drawn by DWM behind the overlay's WPF content, the WPF corner mask must be transparent **where the thumbnail is** — we achieve this with an `OpacityMask` on a containing `Grid`.

Hover card:

- Separate topmost `HoverCardWindow` (non-activating, transparent) showing:
  - App icon (32×32, extracted via `ExtractIconEx` on the process executable; cached).
  - Window title (truncated to 48 chars).
- Positioned flush beside the thumbnail (right of a Left sidebar, left of a Right sidebar), vertically centred on the thumbnail, clamped inside the monitor.
- Appears after 400 ms dwell, fades in 120 ms, fades out 80 ms on leave.
- Replaces the placeholder tooltip from Plan 03.

Icon cache: `AppIconCache` singleton keyed by process executable path; LRU-evicted to keep ≤100 entries.

### 7. Swap animation — the fake-thumbnail trick

The problem: you can't smoothly animate a foreign window's position (`SetWindowPos` in a loop is janky, fights the app's rendering). Solution: animate a DWM thumbnail of the window over a still-imaged background, then snap the real window to its final position _under the cover of the animation_.

#### Contract with Plan 03

`SwapAnimationController` implements `ISwapExecutor` from Plan 03 §Design.5a and is registered in place of `InstantSwapExecutor` when Plan 04 services are wired. The `StageController.SwapAsync` flow from Plan 03 §Design.7 already:

1. Takes the swap semaphore.
2. Mutates state (logical swap) atomically.
3. **Releases the swap semaphore.**
4. Awaits `ISwapExecutor.RunAsync`.

So by the time `SwapAnimationController.RunAsync` is entered, the state is already consistent and the semaphore is free. Concurrency during the animation is the **executor's** problem, not the controller's.

#### Choreography (sequential, synchronous on the UI thread unless noted)

For a swap of outgoing window **O**, incoming window **I**, on target monitor **M**, at the chosen `AnimationSpeed`:

1. Register `outgoingThumb` (source = O, destination = M's `AnimationLayerWindow` HWND) at rect = current main area.
2. Register `incomingThumb` (source = I, destination = M's `AnimationLayerWindow` HWND) at rect = I's current sidebar slot.
3. Set I's live sidebar thumbnail opacity to 0 (keeps the sidebar's visual slot reserved without double-rendering I).
4. `Show()` the `AnimationLayerWindow` on M (transparent topmost; covers the monitor).
5. `SetWindowPos(O, newRect = sidebar slot for O, flags = NOREDRAW | NOACTIVATE)`.
6. `SetWindowPos(I, newRect = main area, flags = NOREDRAW | NOACTIVATE)`.
   — At this point the real windows are in their final positions but are visually covered by the animation layer, which still shows both thumbnails at their starting rects.
7. Drive a WPF `Storyboard` (duration depends on `AnimationSpeed`: Off = 0 ms, Fast = 120 ms, Normal = 250 ms, Slow = 400 ms; easing: `CubicEase(EasingMode=EaseInOut)`). The storyboard's per-frame callback calls `outgoingThumb.UpdateDestinationRect` interpolated from main-area toward O's sidebar slot, and `incomingThumb.UpdateDestinationRect` interpolated from I's sidebar slot toward main area. With `Off` the storyboard completes in one tick with the end-state values.
8. On storyboard completion: dispose both thumbnails, hide `AnimationLayerWindow`, re-sync the sidebar (register a live thumbnail for O in its new slot; unregister I's sidebar thumbnail; restore opacity 255 on any thumbnails we muted).
9. `BringToFront(I)` — best effort.

No real clock references ("T0+0ms", "T0+1ms") because every step above runs on the UI thread synchronously up to step 7, then the storyboard yields; the "ms" ordering in the previous version of this plan was misleading — it's a sequence, not a schedule.

#### Queue and concurrency

`SwapAnimationController` keeps:

```csharp
private SwapPlan? _current;   // animation in flight
private SwapPlan? _next;      // at most one queued plan
private readonly object _gate = new();
```

`RunAsync` semantics:

- If `_current == null`: assign `_current = plan`, run steps 1–9, then check `_next`. If `_next` is non-null, recurse (await) on it. Clear `_current`.
- If `_current != null` and `_next == null`: set `_next = plan`, return a `Task` that completes when `_next` eventually runs to step 9.
- If `_current != null` and `_next != null`: replace `_next` with `plan` (the most-recent queued plan wins), complete the previously queued plan's `Task` with a `Canceled` result, log at Debug.

This matches the "exactly one more swap after this one, newer wins" behaviour described in Plan 03 §Design.7.

#### Cancellation

`RunAsync` accepts `CancellationToken`. On cancellation during steps 1–6: stop the storyboard, dispose any registered thumbnails, hide the animation layer, and **leave the real windows in their final positions** — they have already been moved. The state mutation in `StageController` already happened before `RunAsync` was awaited, so cancellation here only skips the animation; it does not revert the swap.

#### Frame budget

The animation runs on the WPF composition thread; the only per-frame work is updating two `DWM_THUMBNAIL_PROPERTIES` structs (sub-millisecond). 60 fps is comfortable. Plan 05's `--verbose` flag logs per-frame durations so regressions are visible.

### 8. Auto-hide sidebar

`AutoHideCoordinator` (only enabled when `Behavior.AutoHideSidebar == true`):

- Tracks a hidden `MouseHookThread` using `SetWindowsHookEx(WH_MOUSE_LL)` — actually this is too heavy. Alternative: a 4-px-wide transparent "reveal zone" strip along the monitor edge, implemented as a second topmost non-activating HWND. On `WM_MOUSEMOVE` inside the zone, show the overlay with a slide-in animation. On `WM_MOUSELEAVE` from the overlay, start a 1 s dismissal timer.
- Hit priority: the reveal zone has no effect on clicks (transparent), only on hover. The overlay itself is fully interactive.
- Animation: 160 ms slide-in from edge + fade-in; 120 ms slide-out.

The reveal-zone approach is preferred over a low-level mouse hook because low-level hooks degrade on older machines and interact badly with games.

### 9. Custom hotkey capture

`HotkeyCaptureControl` (WPF `UserControl`):

- Displays current hotkey as "Ctrl + Alt + S".
- On click, enters capture mode: button reads "Press keys…".
- Captures `Keyboard.IsKeyDown` modifiers and the first non-modifier key (rejected: lone modifier, single letter without modifier).
- On valid capture, calls `HotkeyService.ReRegister(newSpec)`:
  - Unregisters old.
  - Attempts `RegisterHotKey` with new.
  - If fails (e.g. taken by another app): rollback to old; surface error toast "Hotkey already in use".
  - If succeeds: persist via `SettingsService`.

Accepted combinations: any of (Ctrl, Alt, Shift, Win) + any of (A–Z, 0–9, F1–F12). At least one modifier required.

### 10. First-run welcome window

`WelcomeWindow.xaml`:

- Fullscreen on primary monitor, but not topmost. Shown after `App.OnStartup` resolves settings and detects `General.FirstRunCompleted == false`.
- Three pages (tab-less, advanced via Next button):
  1. "Welcome to Stagehand" — product explanation + a 6-second looping GIF of the core interaction (placeholder `.gif` asset shipped; replaced in a future design pass).
  2. "Let's set it up" — toggles for "Enable now", "Start with Windows", hotkey chooser.
  3. "You're ready" — one button: **Open Settings** (closes Welcome, opens Settings) or **Got it** (closes Welcome).
- On close: `General.FirstRunCompleted = true`, saved.

### 11. Accessibility

- All `TextBlock` / `TextBox` / `Button` in Settings use `SystemColors.*Brush` keys so high-contrast themes take effect automatically.
- Focus visuals: default WPF focus rect for keyboard navigation.
- Keyboard-only flow:
  - `Tab` order across Settings sections validated.
  - `Ctrl+Alt+]` next thumbnail; `Ctrl+Alt+[` previous thumbnail — installed as additional global hotkeys, navigate sidebar focus. Focused thumbnail draws a 2 px accent border. Bracket keys were chosen deliberately over arrow-based combos to avoid the Intel Graphics display-rotation hotkey conflict; they are also configurable in Settings → General.
  - `Ctrl+Alt+Enter` — swap focused thumbnail.
- Sidebar: if `SystemParameters.HighContrast == true`, override `Appearance.Background` to `Tinted` using `SystemColors.HighlightBrush` and disable the acrylic/Mica option.

### 12. Theming

`ThemeManager`:

- `System` follows `WindowsSystemTheme` (via registry watch on `AppsUseLightTheme`).
- `Light` / `Dark` forces.
- Resource dictionaries: `Themes/Light.xaml`, `Themes/Dark.xaml` merged into the app at runtime. Settings UI and Welcome use these. The sidebar overlay uses them only for the accent line + focus rect; the background is acrylic/Mica, which already follows system theme.

---

## Subtasks

### S1 — Settings schema v1 + migrator

**Files**: `app/src/App.Services/Settings/AppSettings.cs` (expand), `app/src/App.Services/Settings/Sections/*.cs`, `SettingsMigrator.cs` (v0→v1), `HotkeySpec.cs`.

**Tests**: `SettingsMigratorTests` (round-trip old & new shapes), `AppSettingsTests` (default values, record equality, immutable update pattern).

**Claude Code prompt**:

> Populate `AppSettings` and its section records per Plan 04 §Design.1. Implement `SettingsMigrator` for v0→v1 per §Design.2. Write the listed unit tests.

### S2 — Live-apply plumbing

**Files**: extend `SettingsService.SaveAsync` to raise a typed `SettingsChanged` event carrying the diff (old + new). Add subscriber classes:

- `app/src/App.Shell/Overlay/SidebarAppearanceBinder.cs` — reacts to Appearance changes.
- `app/src/App.Core/Stage/StageBehaviorBinder.cs` — reacts to Behavior changes.
- `app/src/App.Services/Hotkey/HotkeyRebindBinder.cs` — reacts to General.ToggleHotkey changes.

**Tests**: each binder has a unit test verifying the relevant action is taken on diff (and not on no-op).

**Claude Code prompt**:

> Implement live-apply binders per Plan 04 §Design.3. The `SettingsChanged` event payload is `SettingsChangedEventArgs(AppSettings Previous, AppSettings Current)`. Subscribers compute their own diff. Unit-test each binder with fake services.

### S3 — Settings window and ViewModels

**Files**: `app/src/App.Shell/Settings/SettingsWindow.xaml{.cs}` (replace Plan 01 stub), `ViewModels/MainSettingsViewModel.cs`, `ViewModels/{General,Appearance,Behavior,ExcludedApps,Updates,About}ViewModel.cs`, `Converters/*.cs`.

**Dependencies**: `CommunityToolkit.Mvvm`. Add to `Directory.Packages.props` if not already from Plan 01.

**Tests**: `ViewModelTests` for each VM — covers that property changes propagate to `SettingsService.SaveAsync` once (no thrashing), that validation clamps, that "Reset to defaults" confirmation flow works.

**Claude Code prompt**:

> Build the Settings window per Plan 04 §Design.4. Each section is its own `UserControl` under `Settings/Sections/`. Main window uses a `NavigationView`-style layout (custom, not WinUI control — keep it WPF-native). ViewModels use `CommunityToolkit.Mvvm` `[ObservableProperty]` and `[RelayCommand]`. Changes debounce 150 ms before calling `SaveAsync`.

### S4 — Excluded-apps manager

**Files**: `app/src/App.Shell/Settings/Sections/ExcludedAppsView.xaml{.cs}`, `ViewModels/ExcludedAppsViewModel.cs`, `app/src/App.Services/Processes/IProcessEnumerator.cs`, `ProcessEnumerator.cs`.

**API**: `ProcessEnumerator.EnumerateVisible()` returns distinct process names for windows currently managed by the stage (plus all running processes as an "all" fallback).

**Tests**: `ExcludedAppsViewModelTests` — add/remove/duplicate-guard.

**Claude Code prompt**:

> Build the Excluded Apps manager per Plan 04 §Design.4 "Excluded Apps". The add-by-process dropdown sources from `ProcessEnumerator.EnumerateVisible()`; prefer currently-visible apps to the full list. Guard against duplicates. Write the VM tests.

### S5 — Acrylic / Mica / fallback applicator

**Files**: `app/src/App.Interop/Composition/SidebarBackgroundApplicator.cs`, `INativeCompositionApi.cs`, `NativeCompositionApi.cs`, `AccentPolicy.cs`, DWM constant definitions.

**API**: §Design.5.

**Tests**: `SidebarBackgroundApplicatorTests` with fake `INativeCompositionApi` + fake OS-version provider; verifies the correct call is made per (OS, user-choice) combination. 8 cases covering the combinations.

**Claude Code prompt**:

> Implement `SidebarBackgroundApplicator` per Plan 04 §Design.5. OS detection via a small `IOsVersionProvider` seam so tests are deterministic. When the chosen option is unavailable, fall back per the matrix in §Design.5 and log Info.

### S6 — Thumbnail rounding, shadows, hover card

**Files**: `app/src/App.Shell/Overlay/Thumbnails/ThumbnailFrame.xaml{.cs}` (WPF overlay frame for rounded corners + shadow), `app/src/App.Shell/Overlay/Thumbnails/HoverCardWindow.xaml{.cs}`, `app/src/App.Services/Icons/AppIconCache.cs`, `IAppIconProvider.cs`, `AppIconProvider.cs`.

**API**:

```csharp
public sealed class HoverCardWindow : Window
{
    public void ShowFor(WindowSnapshot snapshot, Rect thumbnailScreenRect, SidebarEdge edge);
    public void HideFast();
}

public interface IAppIconProvider { ImageSource GetIcon(int processId); }
```

**Tests**: `AppIconCacheTests` (LRU eviction, miss → load, hit → no reload).

**Claude Code prompt**:

> Build `ThumbnailFrame`, `HoverCardWindow`, and `AppIconCache` per Plan 04 §Design.6. The hover card is a topmost non-activating WPF window; reuse one instance across all thumbnails (re-position instead of re-create). Use `ExtractIconEx` via P/Invoke (declare in `NativeMethods.Shell32.cs`).

### S7 — Swap animation — `SwapAnimationController`

**Files**: `app/src/App.Shell/Animation/SwapAnimationController.cs`, `AnimationLayerWindow.xaml{.cs}` (transparent topmost per monitor).

**API**:

```csharp
internal sealed class SwapAnimationController : ISwapExecutor   // ISwapExecutor defined in Plan 03 §Design.5a
{
    public SwapAnimationController(
        IDwmThumbnailFactory thumbnails,
        IWindowController windows,
        UiDispatcher ui,
        AnimationLayerWindowFactory animationLayers,
        ILogger<SwapAnimationController> log);
    public Task RunAsync(SwapPlan plan, AnimationSpeed speed, CancellationToken ct);  // SwapPlan + AnimationSpeed also from Plan 03
}
```

`SwapPlan` and `AnimationSpeed` are NOT redefined here — they live in `App.Core/Stage/` from Plan 03. This plan only adds the animated implementation of the `ISwapExecutor` contract and swaps the DI registration in `ServiceConfiguration` so `StageController.SwapAsync` picks it up.

**Tests**: `SwapAnimationControllerTests` with fake factory + controller + dispatcher. Verifies:

- Off speed: no animation frames; single snap.
- Normal speed: animation runs 250 ms, thumbnails updated each frame, real windows moved at T0+1ms.
- Cancellation mid-animation: both thumbnails disposed; windows still at final positions; state Idle.

**Claude Code prompt**:

> Implement `SwapAnimationController` per Plan 04 §Design.7. State machine is the core — follow the diagram exactly. The animation layer is a single transparent topmost WPF window per monitor, reused across swaps. `RunAsync` is awaitable; a second swap queues at most one "next target" and drops further calls.

### S8 — Auto-hide sidebar

**Files**: `app/src/App.Shell/Overlay/AutoHide/RevealZoneWindow.xaml{.cs}`, `AutoHideCoordinator.cs`.

**Tests**: `AutoHideCoordinatorTests` with fake mouse events — verifies show/dismiss timing, no double-show on rapid moves.

**Claude Code prompt**:

> Implement auto-hide per Plan 04 §Design.8. The reveal zone is a 4-px transparent topmost non-activating window per managed monitor, docked to the configured edge. On hover, fire the show signal; on overlay leave, dismiss timer 1 s.

### S9 — Keyboard navigation

**Files**: `app/src/App.Shell/Input/KeyboardNavigationCoordinator.cs`.

Registers three additional hotkeys (`Ctrl+Alt+[` previous, `Ctrl+Alt+]` next, `Ctrl+Alt+Enter` swap) via `HotkeyService`. Tracks a focused-thumbnail index per monitor. Focused thumbnail is rendered with an accent 2 px border.

**Tests**: `KeyboardNavigationCoordinatorTests` (focus cycles, wraps, Enter triggers swap).

**Claude Code prompt**:

> Implement keyboard navigation per Plan 04 §Design.11. Focus index is per monitor; left/right cycles within the focused monitor's sidebar. Enter triggers `StageController.SwapAsync`.

### S10 — Hotkey capture UI

**Files**: `app/src/App.Shell/Settings/Controls/HotkeyCaptureControl.xaml{.cs}`.

**Tests**: `HotkeyCaptureControlTests` — captures combinations, rejects modifier-only, rejects key-only, round-trips through `HotkeyService.ReRegister`, surfaces failure.

**Claude Code prompt**:

> Implement `HotkeyCaptureControl` per Plan 04 §Design.9. Capture via WPF `PreviewKeyDown` / `PreviewKeyUp`, not a low-level hook. On commit, call `HotkeyService.ReRegister`; on failure, rollback + toast.

### S11 — First-run welcome window

**Files**: `app/src/App.Shell/FirstRun/WelcomeWindow.xaml{.cs}` (replace Plan 01 stub), `FirstRunCoordinator.cs`.

**Tests**: `FirstRunCoordinatorTests` — runs welcome iff `FirstRunCompleted == false`; sets flag on close.

**Claude Code prompt**:

> Implement the 3-page welcome per Plan 04 §Design.10. The GIF asset is a placeholder — add a 64×64 static PNG if no GIF exists. Use a `Frame`-style navigation within a single window (no wizard library).

### S12 — Theming + high contrast

**Files**: `app/src/App.Shell/Themes/{Light,Dark}.xaml`, `ThemeManager.cs`, `app/src/App.Shell/Accessibility/HighContrastDetector.cs`.

**Tests**: `ThemeManagerTests` (switch light/dark), `HighContrastDetectorTests` (detects system HC; fires event on change).

**Claude Code prompt**:

> Implement theming per Plan 04 §Design.12 and high-contrast detection per §Design.11. ThemeManager exposes a `CurrentTheme` property; UI binds against a dynamic `ThemeDictionary` merged at runtime.

### S13 — Start-with-Windows

**Files**: `app/src/App.Services/Autostart/IAutostartService.cs`, `StartupFolderAutostartService.cs`, `app/src/App.Interop/Shell/WshInterop.cs`, `app/src/App.Interop/Shell/ShellLinkInterop.cs`.

**Implementation.** Creates/deletes a `.lnk` in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\` pointing at the app exe. Two shortcut-creation backends, tried in order:

1. **WshShell (primary)** — `IWshShell` via COM. Cleaner API but relies on the Windows Script Host service, which many corporate group policies disable (`HKLM\Software\Microsoft\Windows Script Host\Settings\Enabled = 0`).
2. **IShellLink (fallback)** — raw `IShellLinkW` + `IPersistFile` COM interop. Lower-level but always available.

The service calls `TryCreateViaWshShell`; on `COMException` / `UnauthorizedAccessException` / "ScriptHost disabled" detection, it falls back to `CreateViaShellLink` and logs at Info ("WSH unavailable — using IShellLink fallback").

**Tests**: `StartupFolderAutostartServiceTests` with a redirected temp folder. Covers: create via WSH, delete via WSH, forced fallback to IShellLink (simulate WSH failure), fallback create, fallback delete, idempotent create-when-already-exists, idempotent delete-when-missing.

**Claude Code prompt**:

> Implement `StartupFolderAutostartService` per Plan 04 §S13 with both WshShell and IShellLink backends. Declare `IWshShell` COM interop in `app/src/App.Interop/Shell/WshInterop.cs` and `IShellLinkW` + `IPersistFile` in `app/src/App.Interop/Shell/ShellLinkInterop.cs`. The service prefers WshShell; on any failure indicative of WSH-disabled (COMException HR 0x80040154 REGDB_E_CLASSNOTREG, or 0x800704EC policy-disabled), fall back to IShellLink. Write the seven test cases listed.

### S14 — Manual checklist + polish pass

**Files**: `docs/manual-tests/plan-04.md`.

**Checklist**:

1. Open Settings; each section renders correctly in Light and Dark themes; high contrast legible.
2. Toggle every setting; verify live-apply.
3. Change sidebar position; sidebar flips to the other edge within 200 ms.
4. Set background to Mica (experimental) on Win 11 22H2+; if the probe succeeds verify Mica; if it falls back verify Acrylic + the one-time toast appears. Set to Acrylic; verify Acrylic. Set to None; verify fully transparent (wallpaper visible with no blur).
5. Set animation speed to Normal; click thumbnails; verify smooth swap, no flicker, ≥60 fps in frame log.
6. Set animation speed to Off; click thumbnails; instant snap.
7. Hover a thumbnail for 400 ms; verify app-icon + title card appears beside it inside the monitor.
8. Toggle auto-hide; sidebar retracts after 1 s without hover; re-appears on edge hover.
9. Capture a new hotkey; old stops working, new works; conflict rejection shows toast.
10. Disable, force-uninstall, reinstall, launch; First-run Welcome appears; dismissing sets flag.
11. Exclude an app from Settings; enable Stagehand; that app is absent from the sidebar.
12. Change theme to Dark; Settings switches; sidebar accent respects theme; wallpaper/blur unchanged.
13. Keyboard nav: `Ctrl+Alt+]` cycles focus forward; `Ctrl+Alt+[` backward; `Ctrl+Alt+Enter` swaps; works across both monitors.

**Claude Code prompt**:

> Author `docs/manual-tests/plan-04.md` per Plan 04 §S14. After a Harness run of the checklist, file any regressions as issues and do a final polish pass (spacing, alignment, typography consistency).

---

## Risks & Mitigations

| Risk                                                                     | Impact                | Mitigation                                                                                                                                                                                 |
| ------------------------------------------------------------------------ | --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Mica + WS_EX_NOACTIVATE + no caption combo breaks on some Win11 builds   | Acrylic-only fallback | `SidebarBackgroundApplicator` probes for success (e.g. `DwmGetColorizationColor` side effect); falls back to Acrylic + logs Info.                                                          |
| Animation stutters with 30+ thumbnails                                   | Bad perf impression   | Only the two animated thumbnails (outgoing + incoming) run on the animation layer; others stay static in the real sidebar. Budget holds.                                                   |
| Hotkey capture steals keystrokes meant for another focused app           | User confusion        | Capture mode only active while the capture control has WPF focus and shows "Press keys…"; never installs low-level hooks.                                                                  |
| Live-apply thrashing during slider drag                                  | Flood of SaveAsync    | Debounce 150 ms on every observable property change.                                                                                                                                       |
| Settings schema bump corrupts in-flight changes                          | Lost user config      | Write-temp + rename is atomic on Windows; migration runs in-memory; only replaces file on successful produce.                                                                              |
| Startup `.lnk` creates phantom double-launch if user also pinned the exe | Two processes         | `App.OnStartup` checks `Mutex` on a well-known name; second instance activates the first's tray and exits.                                                                                 |
| Acrylic + live wallpaper engine kills perf on weaker GPUs                | Choppy sidebar        | Acrylic respects the OS's "Transparency effects" toggle — if off, auto-fallback to Tinted. Setting tooltip points at Windows accessibility settings.                                       |
| Thumbnail corner rounding trick leaks the square edges when DWM repaints | Visual glitch         | Overlay's own window uses `DWMWA_WINDOW_CORNER_PREFERENCE` so DWM rounds the _window_ corners; thumbnail edges inside are shaped by the `OpacityMask`. Verified on both Win 10 and Win 11. |
| Focused-thumbnail visual accents fight the hover-card visual             | UI noise              | Focused ≠ hovered; when a focused thumbnail is hovered, suppress the focus accent and defer to the hover card.                                                                             |
| Custom hotkey conflicts with a user's game                               | App feels broken      | `HotkeyService.Register` reports conflict; Settings shows inline error. User can clear the hotkey entirely (empty → toggle only via tray).                                                 |
| Migrator misreads a future v2 settings file as v1                        | Silent data loss      | `SchemaVersion > currentSupported` logs Critical and keeps defaults for the session; does NOT overwrite the file.                                                                          |

---

## Verification

1. **Unit tests.** ≥25 new tests across all subtasks. CI green.
2. **Manual checklist** at `docs/manual-tests/plan-04.md`. All 13 steps verified on both Win 10 21H2 and Win 11 22H2+.
3. **Perf.** `--verbose` swap logs show no frame > 20 ms during animation.
4. **Settings round-trip.** Set every setting to non-default, close app, reopen — every setting preserved.
5. **Fault injection.** Simulate process kill mid-save (via `File.Replace` throwing): original `settings.json` intact, backup `settings.json.tmp` absent.
6. **Accessibility.** With high contrast on, Settings window all text legible; sidebar accent respects system colours.

---

## References

- `stage-manager-windows-plan.md` — §"Application Surface", Phase 5, Phase 7.
- Plan 01 §Design.10 — settings path and schema skeleton.
- Plan 03 §Design.7 — `SwapAsync`, into which the animation controller now plugs.
- `CommunityToolkit.Mvvm` — <https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/>
- Mica / DWM backdrop types — <https://learn.microsoft.com/windows/apps/design/style/mica>
- Acrylic via `SetWindowCompositionAttribute` (undocumented-but-stable) — <https://learn.microsoft.com/windows/apps/design/style/acrylic>
- `DWMWA_WINDOW_CORNER_PREFERENCE` — <https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute>
- `ExtractIconEx` — <https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-extracticonexw>
- `RegisterHotKey` — <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey>
- High-contrast detection — <https://learn.microsoft.com/windows/win32/winauto/high-contrast-parameter>
- Startup folder path — <https://learn.microsoft.com/windows/win32/shell/knownfolderid#FOLDERID_Startup>
- `IWshShell` shortcut creation — <https://learn.microsoft.com/windows/win32/shell/links#creating-shortcuts-programmatically>
