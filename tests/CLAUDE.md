# CLAUDE.md (`tests/`)

Scope: test projects. Root [`../CLAUDE.md`](../CLAUDE.md) covers the big picture; this file lists conventions that apply inside `tests/` only.

## Two projects, two purposes

```
tests/
├─ App.Tests/       # xUnit — runs in CI, deterministic, no Win32 / no DWM
└─ App.Harness/     # WPF manual test runner — not in CI, exercises real APIs
```

- **`App.Tests`** is the unit-test project. It must not touch `user32.dll`, real file paths outside a temp folder, real registry, real process enumeration, or any WPF dispatcher. Everything interop-facing is mocked via `NSubstitute` or stubbed via `Fakes/`.
- **`App.Harness`** is a small WPF executable for manual exploration: enumerate the real desktop, register real DWM thumbnails, exercise park/restore against a real Notepad window. It is launched by hand (`dotnet run --project tests/App.Harness`), does not run in CI, and has no `[Fact]` methods.

If you're about to write a test that needs a live HWND or a real window, stop — add a scenario to the harness instead.

## Project references

`App.Tests.csproj` references `App.Core`, `App.Services`, `App.Interop`, and `App.Shell`. The last one is unusual (a test project referencing a `WinExe`) but required so smoke tests can verify DI registration of UI-layer singletons without invoking them. Do **not** resolve `TrayIconHost` or `SettingsWindow` from a test's `ServiceProvider` — they need a WPF dispatcher. Assert they are _registered_ instead (see `ProjectReferenceSmokeTests.DI_Registers_WPF_Services`).

## Test conventions

- File path mirrors the subject one-to-one: `app/src/App.Core/Stage/StageState.cs` → `tests/App.Tests/State/StageStateTests.cs`. The folder under `tests/App.Tests/` matches the last namespace segment of the subject, not the full path.
- Test class names end in `Tests`. Method names use underscores describing the scenario: `Empty_IsDisabled_WithNoParkedWindows`, `SaveAndReloadRoundTrips`. CA1707 is suppressed per class with a justification attribute; keep the suppression class-scoped, not global.
- Use `FluentAssertions` for readable failure messages: `result.Should().Be(42)`, `collection.Should().BeEmpty()`, `action.Should().Throw<InvalidOperationException>()`. Do not mix in raw `Assert.*` within the same test.
- `NSubstitute` is the mock library: `var log = Substitute.For<ILogger<SettingsService>>();`. When stubbing a generic `Log` method, remember that NSubstitute needs `log.IsEnabled(Arg.Any<LogLevel>()).Returns(true)` first or the cached `LoggerMessage.Define` delegates will no-op.
- Async tests use `.ConfigureAwait(true)` to satisfy xUnit1030 + CA2007 together.
- Tests must not depend on test ordering. xUnit runs them in parallel by default; do not disable that.

## The `Fakes/` folder

Reusable test doubles live in `tests/App.Tests/Fakes/`. Today: `FakeClock` (implements `IClock`, lets a test drive `UtcNow` and `TimestampTicks` deterministically). When Plan 02 arrives add `FakeWindowEnumerator`, `FakeWindowController`, etc. here — one class per interface, named `Fake*`, same folder.

If a fake is used by exactly one test class, keep it nested in that test file. Promote to `Fakes/` only when two or more test classes need it.

## Temp-file patterns

Tests that touch the real filesystem (e.g. `SettingsServiceTests`) create a unique path under `Path.GetTempPath()`:

```csharp
private readonly string _tempDir = Path.Combine(
    Path.GetTempPath(), "stagehand-tests-" + Guid.NewGuid().ToString("N"));
```

and clean up in `IDisposable.Dispose` or an `IAsyncLifetime.DisposeAsync`. Never hard-code absolute paths. Never write under `%APPDATA%` or `%LOCALAPPDATA%` from a test — the real `AppDataSettingsPathProvider` is off-limits; use an `InMemorySettingsPathProvider` (defined nested inside `SettingsServiceTests.cs`) or equivalent.

## Running tests

```bash
dotnet test App.sln                                                      # all
dotnet test App.sln --filter FullyQualifiedName~StageStateTests          # one class
dotnet test App.sln --filter Name=Empty_IsDisabled_WithNoParkedWindows   # one method
dotnet test App.sln --logger "trx;LogFileName=test-results.trx"          # CI-style trx output
```

The harness runs separately:

```bash
dotnet run --project tests/App.Harness
```

## Coverage

`coverlet.collector` is wired up but not enforced. Plan 01 §9 targets ≥80 % line coverage on `App.Core`, reported in CI but not a gate. Don't optimise for the number; optimise for meaningful assertions on observable behaviour.

## Integration and E2E tiers (not yet in place)

Plan 01 §9 defines three tiers (Unit / Harness / manual checklists), but classic automated **integration tests** (running against real Win32 on CI windows runners) and **E2E UI automation** (FlaUI / WinAppDriver driving the tray and settings window) are **not** scaffolded yet. If you're adding one of those, it needs a new test project and a plan addendum — do not smuggle them into `App.Tests`.
