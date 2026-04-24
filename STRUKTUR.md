# Struktur und Aufbau

Dieses Dokument erklärt, wie das Repo organisiert ist und was wo passiert. Es richtet sich an Leser, die mit .NET/C#/WPF wenig oder keine Erfahrung haben. Details zu jeder Entscheidung stehen in den Plan-Dokumenten unter [`docs/plans/`](docs/plans/).

## Was das Repo ist

Stagehand ist eine Desktop-App für Windows, die macOS-Stage-Manager nachbaut: Fenster werden als Live-Miniaturen an einer Sidebar geparkt, das aktive Fenster bekommt den Hauptbereich.

Die App besteht aus **einer einzigen ausführbaren Datei** (`App.Shell.exe`), die im **System-Tray** läuft. Sichtbare Bedienelemente:

- **Tray-Icon** — Rechtsklick öffnet das Kontextmenü (Settings, Diagnostics, About, Exit), Linksklick schaltet die Bühne ein/aus (Plan 03).
- **Sidebar-Overlay** — die eigentliche Bühne mit Live-Miniaturen der Fenster; transparent, ohne Titelleiste, dockt an eine Bildschirmkante (Plan 02/04).
- **Settings-Fenster** — öffnet sich aus dem Tray, erlaubt Einstellungen wie Sidebar-Position (links/rechts/oben/unten), Auto-Start, Update-Kanal, Acrylic/Mica-Look, ausgeschlossene Apps, Hotkeys (Plan 04).
- **Welcome-Fenster** — einmaliger Willkommens-Dialog beim ersten Start (Plan 04).
- **About-Dialog** — Versionsinfo, aufrufbar aus dem Tray-Menü oder Settings.

Was die App **nicht** ist:

- **Kein Windows-Dienst** — läuft ausschließlich im User-Kontext, braucht keine Admin-Rechte.
- **Kein Server** — keine Netzwerk-Listener. Ausgehende Netzwerkaufrufe nur gegen den Update-Kanal (Plan 05), sonst keine.
- **Kein Autostart per Default** — der Start mit Windows ist ein Opt-in in den Settings (Plan 04 verdrahtet es über einen Registry-Eintrag unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` bzw. einen Shell-Link im Autostart-Ordner; keine Gruppenrichtlinien, kein globaler Scope).
- **Keine Telemetrie** — keine Analytics, keine Crash-Reports nach außen, keine Nutzungsstatistik (siehe `SECURITY.md` und `docs/architecture/threat-model.md`).

## Tech-Stack in einem Absatz

- **.NET 8** ist Microsofts Laufzeit- und Compiler-Plattform (Nachfolger von .NET Framework). Plattformunabhängig als Ganzes, aber einzelne Teile wie WPF sind Windows-spezifisch.
- **C#** ist die Programmiersprache. Typisiert, objektorientiert mit funktionalen Zügen. Dateien enden auf `.cs`.
- **WPF** (Windows Presentation Foundation) ist das UI-Toolkit. Oberflächen werden in **XAML** beschrieben (eine XML-artige Deklaration, `.xaml`-Dateien), die Verhaltenslogik in `.cs` („Code-behind").
- **MSBuild** ist das Build-System. Konfiguration liegt in `.csproj`-Dateien (XML). `dotnet build` liest sie und erzeugt Assemblies (`.dll` und `.exe`).
- **NuGet** ist der Paketmanager (analog zu npm). Paketversionen sind zentral in `Directory.Packages.props` gepinnt; jedes Projekt referenziert sie ohne eigene Version.
- **xUnit** ist das Test-Framework, vergleichbar mit Jest/Vitest.

## Ordner-Layout

```text
Stagehand/
├─ app/
│  ├─ src/
│  │  ├─ App.Interop/     # Schicht 1: Brücke zu Windows (P/Invoke)
│  │  ├─ App.Core/        # Schicht 2: reine Domänen-Logik
│  │  ├─ App.Services/    # Schicht 3: IO, Settings, Logging
│  │  └─ App.Shell/         # Schicht 4: WPF-UI + Komposition
│  ├─ assets/brand/             # Icon-Quelldateien
│  └─ build/                    # MSBuild-Targets (Build-Glue)
├─ tests/
│  ├─ App.Tests/          # Unit-Tests (xUnit)
│  └─ App.Harness/        # Manueller Test-Runner (ab Plan 02)
├─ docs/
│  ├─ plans/                    # Phasenpläne 01–05, Implementierungs-Briefe
│  ├─ architecture/             # Dauerhafte Referenz-Doku
│  └─ manual-tests/             # Manuelle Checklisten
├─ .github/                     # Issue-Templates, PR-Template, Dependabot, CI-Workflows
├─ .config/dotnet-tools.json    # Lokale .NET-Tools (CSharpier, XAML Styler)
├─ Directory.Build.props        # Globale MSBuild-Einstellungen (alle Projekte)
├─ Directory.Packages.props     # Zentrale NuGet-Versionen
├─ App.sln                # Solution (Projekt-Sammelmappe)
├─ .editorconfig                # Code-Style-Regeln, Analyzer-Konfiguration
├─ .prettierrc.json / .prettierignore
├─ .csharpierignore
├─ .gitattributes / .gitignore
├─ package.json / package-lock.json  # nur für Prettier als Dev-Dependency
├─ global.json                  # pinnt die .NET-SDK-Version
├─ NOTES.md                     # laufendes Entscheidungslogbuch
├─ README.md, LICENSE, CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, THIRD-PARTY-NOTICES.md
└─ stage-manager-windows-plan.md  # die ursprüngliche Vision
```

Nur **`app/`, `tests/`, `docs/`, `.github/`** und Root-Config liegen direkt im Repo-Root. Das hält das Top-Level übersichtlich.

## Die vier Schichten und ihre Regeln

Die App ist in vier Projekte zerlegt, die streng in eine Richtung voneinander abhängen:

```text
App  →  Services  →  Core  →  Interop
```

Das heißt: `App.Shell` (die UI-Schicht) darf alles andere nutzen, `App.Interop` darf **nichts** anderes nutzen. Die Pfeile sind in den `<ProjectReference>`-Einträgen der `.csproj`-Dateien verdrahtet, und der Compiler setzt sie durch. Wenn jemand in `App.Core` ausversehen `System.Windows.Application` importiert, schlägt der Build fehl.

> **Namensschema ist brand-neutral.** Ordner, Projekte, Namespaces und die Solution-Datei benutzen den Präfix `App.*`, nicht den Produktnamen. Dadurch bleibt ein späterer Rebrand leichtgewichtig: man ändert nur `app/src/App.Core/Branding/BrandConstants.cs` plus die User-sichtbaren Strings in Doku und GitHub-URLs. Code-Dateien bleiben unverändert. Details siehe `NOTES.md` und `BrandConstants`-XML-Doc.

### Schicht 1 — `App.Interop`: Brücke zu Windows

Windows stellt Fenster- und Grafik-Operationen über C-Funktionen in DLLs bereit (`user32.dll`, `dwmapi.dll`, …). C# ruft diese über **P/Invoke** auf: die Signatur wird in C# deklariert und mit `[DllImport]` oder `[LibraryImport]` markiert.

Diese Schicht enthält **nur Signaturen und Interfaces**, keine Logik. Die Logik kommt ab Plan 02.

Wichtige Dateien:

- `NativeMethods.User32.cs`, `.Dwmapi.cs`, `.Shcore.cs`, `.Kernel32.cs` — rohe P/Invoke-Signaturen für rund 50 Win32-Funktionen (Fenster enumerieren, verschieben, DWM-Thumbnails erzeugen, …). Jede Signatur hat einen Kommentar, welcher Plan sie später konsumiert, plus einen Link zur Microsoft-Dokumentation.
- `WindowSnapshot.cs` — Datensatz mit HWND (Fenster-Handle), Titel, Klasse, Prozess-ID, Rechteck.
- `Rect.cs` — eigener Rechteck-Typ. Wir benutzen nicht den WPF-Rect oder den GDI-Rectangle, damit diese Schicht keine Abhängigkeit zu UI-Bibliotheken hat.
- `SafeDwmThumbnailHandle.cs`, `SafeWinEventHookHandle.cs` — Wrapper, die native Ressourcen deterministisch freigeben (`Dispose()` ruft automatisch die Native-Release-Funktion auf).
- Fünf Interfaces (`IWindowEnumerator`, `IWindowController`, `IDwmThumbnail`, `IWinEventHook`, `IWorkAreaManager`) als Verträge ohne Implementierung, plus je eine `NotImplemented*`-Stub-Klasse. Die Stubs werfen `NotImplementedException`, wenn jemand sie aufruft — das ist Absicht, bis Plan 02 die echten Implementierungen liefert.

### Schicht 2 — `App.Core`: pure Domäne

Keine Win32-Aufrufe, keine Datei-IO, kein UI. Nur reine Datenstrukturen und Zustandsmaschinen. Diese Schicht ist ohne Windows testbar.

Wichtige Dateien:

- `Stage/StageState.cs` — der zentrale **immutable Snapshot** (englisch _record_): aktueller Phasenzustand (`Disabled` / `Enabling` / `Enabled` / `Disabling`), Liste geparkter Fenster, gespeicherte Work-Area-Rechtecke pro Monitor. Immutable bedeutet: Änderung erzeugt eine neue Instanz, die alte bleibt gültig. Das macht nebenläufigen Code drastisch einfacher.
- `Stage/StagePhase.cs` — Enum mit den vier Zuständen.
- `Stage/WindowIdentity.cs` — Tripel aus HWND + Prozess-ID + Prozess-Startzeit. Wichtig, weil HWNDs nach Fensterschluss recycled werden. Die Prozess-Startzeit unterscheidet „das Fenster mit HWND 0x1234 jetzt" von „das HWND 0x1234 nach einem Explorer-Neustart".
- `Stage/IStageController.cs` — das Interface für das Ding, das die Bühne aktiviert, deaktiviert und Fenster tauscht. Implementiert wird es in Plan 03.
- `Errors/` — drei typisierte Exceptions: `WindowNotManageableException` (Fenster ist cloaked / UWP / System), `ElevationBoundaryException` (unelevated Prozess kann elevated Fenster nicht anfassen), `Win32InteropException` (Win32-Aufruf fehlgeschlagen, trägt den Error-Code).
- `Time/IClock.cs` + `SystemClock.cs` — abstrahierte Zeit. Tests nutzen `FakeClock` statt Wall-Clock, damit zeit-abhängige Logik deterministisch wird.

### Schicht 3 — `App.Services`: Infrastruktur

Alles, was IO macht, aber noch keine UI ist.

Wichtige Dateien:

- `Settings/AppSettings.cs` — das Root-Record plus fünf Unter-Records (`General`, `Appearance`, `Behavior`, `Updates`, `Logging`). In Plan 01 sind die Unter-Records leer; Plan 04 füllt sie.
- `Settings/SettingsService.cs` — **das einzige nicht-stub Stück Logik in Plan 01**. Lädt und speichert JSON aus `%APPDATA%\<BrandConstants.ProductFolderName>\settings.json` (aktuell `Stagehand`), schreibt atomar (Temp-Datei + `File.Replace`, verhindert Korruption bei Stromausfall), meldet `Changed`-Ereignis nach erfolgreichem Save, erkennt zu-neue Schema-Versionen und fällt dann auf Defaults zurück.
- `Settings/AppDataSettingsPathProvider.cs` — kennt die Windows-Standard-Pfade (`%APPDATA%` für Roaming-Settings, `%LOCALAPPDATA%` für Logs, letzterer wird nicht über Geräte synchronisiert). Produkt-Unterordner kommt aus `BrandConstants`.
- `Settings/SettingsMigrator.cs` — Schema-Migration-Platzhalter; v0→v1 kommt in Plan 04.
- `Hotkeys/StubHotkeyService.cs`, `Updates/StubUpdateService.cs` — Platzhalter, geben bei erstem Aufruf eine Warning ins Log („not yet implemented"). Plan 04 bzw. 05 ersetzen sie.
- `Logging/LoggingConfiguration.cs` — konfiguriert **Serilog** (Logging-Bibliothek) mit rolling file sink: eine Datei pro Tag, 7 Tage Aufbewahrung, 5 MB-Größenlimit. In Debug-Builds zusätzlich Visual-Studio-Debug-Ausgabe.

### Schicht 4 — `App.Shell`: die sichtbare App

Hier sitzt das UI, die Komposition und die Event-Verteilung.

Wichtige Dateien:

- `App.xaml` + `App.xaml.cs` (Klasse `ShellApp`) — **Eintrittspunkt** der App. Der WPF-SDK generiert automatisch den `Main` aus `App.xaml`. Die Methode `OnStartup` in `ShellApp` baut den DI-Container (siehe unten), registriert drei globale Exception-Handler (`AppDomain.UnhandledException`, `Dispatcher.UnhandledException`, `TaskScheduler.UnobservedTaskException`), lädt die Settings synchron und startet das Tray-Icon. Die App nutzt `ShutdownMode=OnExplicitShutdown`: sie läuft weiter, auch wenn alle Fenster geschlossen sind. Beendet wird nur über den "Exit"-Eintrag im Tray. (Die Klasse heißt `ShellApp` und nicht `App`, damit der Name im generierten `App.g.cs` nicht mit dem Namespace-Segment `App` kollidiert.)
- `Composition/ServiceConfiguration.cs` — die **DI-Verdrahtung** (Dependency Injection). DI bedeutet: Klassen bauen ihre Abhängigkeiten nicht selbst, sondern bekommen sie per Konstruktor übergeben. Ein Container verwaltet das. Beispiel:

  ```csharp
  services.AddSingleton<ISettingsService, SettingsService>();
  ```

  Heißt: „Wenn jemand `ISettingsService` anfordert, gib ihm eine einzige gemeinsame `SettingsService`-Instanz." Die späteren Pläne tauschen hier nur Registrierungen aus (`NotImplementedWindowEnumerator` → echte `WindowEnumerator`-Klasse), ohne Aufrufer-Code anzufassen.

- `Tray/TrayIconHost.cs` — baut das Systray-Icon (via `H.NotifyIcon.Wpf`) samt Kontextmenü ("Settings…", "Open Diagnostics", "About", "Exit") und reagiert auf Linksklick (aktuell nur Log-Eintrag, Plan 03 hängt den echten Stage-Toggle dran).
- `Settings/SettingsWindow.xaml` + `.cs` — das Settings-Fenster, in Plan 01 nur ein Platzhalter-Textblock. Plan 04 implementiert die echte Oberfläche.

## Die zwei Test-Projekte

- **`App.Tests`** — Unit-Tests mit xUnit, `FluentAssertions` für lesbare Assertions (`result.Should().Be(42)`), `NSubstitute` als Mock-Bibliothek. Läuft in CI. Aktuell 15 Tests. Starten mit `dotnet test`.
- **`App.Harness`** — ein WPF-Programm zum **manuellen Ausprobieren**. In Plan 01 nur ein Platzhalter. Plan 02 nutzt es, um z.B. eine Debug-Overlay mit Live-Thumbnails aller offenen Fenster anzuzeigen. **Nicht** Teil der CI.

## Root-Konfiguration

- `Directory.Build.props` — Einstellungen, die **alle** Projekte automatisch erben: Ziel-Framework (`net8.0-windows10.0.19041.0`), nullable reference types, `TreatWarningsAsErrors` in Release, deterministische Builds, `packages.lock.json`-Generierung.
- `Directory.Packages.props` — **Central Package Management**. Alle Paketversionen stehen hier; die einzelnen Projekte referenzieren Pakete nur noch per `<PackageReference Include="…" />` ohne Version. Verhindert Version-Drift.
- `global.json` — pinnt die .NET-SDK-Version auf `8.0.x`.
- `.editorconfig` — Code-Style-Regeln (Einrückung, Zeilenenden, nullable, file-scoped namespaces). CSharpier liest dieselbe Datei.
- `.prettierrc.json` + `.prettierignore` — Prettier-Konfiguration für Markdown, YAML und JSON. Prettier wird über npm installiert (daher `package.json`).
- `.csharpierignore` — schließt XAML aus der CSharpier-Verarbeitung aus (XAML macht XAML Styler).
- `.gitignore`, `.gitattributes` — Git-Standards plus Projekt-spezifische Zeilenend-Regeln.
- `.config/dotnet-tools.json` — lokale .NET-Tools (CSharpier, XAML Styler), abrufbar über `dotnet tool restore`.

## Formatter-Kette (vier Werkzeuge, vier Dateitypen)

| Werkzeug        | Zuständig für            | CI-Check                                               |
| --------------- | ------------------------ | ------------------------------------------------------ |
| `prettier`      | Markdown, YAML, JSON     | `npx prettier --check .`                               |
| `csharpier`     | C#-Layout                | `dotnet csharpier check .`                             |
| `xstyler`       | XAML                     | `dotnet xstyler --recursive --directory app --passive` |
| `dotnet format` | C#-Whitespace, `.csproj` | `dotnet format --verify-no-changes`                    |

Alle vier laufen auf jeder CI-Build, alle vier lassen sich lokal als "--write"-Variante starten, um Fehler automatisch zu beheben. Style-Diskussionen im Review sind damit ausgeschlossen — was der Formatter will, gilt.

## Build- und Dev-Loop

```bash
dotnet build                                 # alles kompilieren
dotnet test                                  # Tests rennen
dotnet run --project app/src/App.Shell   # App starten
```

Vor dem Pushen empfohlen:

```bash
npx prettier --check .
dotnet csharpier check .
dotnet xstyler --recursive --directory app --passive
dotnet format --verify-no-changes
```

CI (`.github/workflows/ci.yml`) rennt genau das auf `windows-latest`, plus den Vulnerability-Scan (`dotnet list package --vulnerable`). Kein Review ist nötig, um festzustellen „Formatierung falsch" — das macht die Pipeline.

## Wo ändere ich was?

| Wenn du das ändern willst …                    | Schau in …                                                           |
| ---------------------------------------------- | -------------------------------------------------------------------- |
| Paket-Versionen                                | `Directory.Packages.props`                                           |
| Compiler-Einstellungen für alle Projekte       | `Directory.Build.props`                                              |
| Code-Style, Analyzer-Regeln                    | `.editorconfig`                                                      |
| Welche Services im DI-Container existieren     | `app/src/App.Shell/Composition/ServiceConfiguration.cs`              |
| Was passiert beim App-Start                    | `app/src/App.Shell/App.xaml.cs`                                      |
| Wo Settings und Logs liegen                    | `app/src/App.Services/Settings/AppDataSettingsPathProvider.cs`       |
| Produktname, Tray-Tooltip, Fenstertitel        | `app/src/App.Core/Branding/BrandConstants.cs`                        |
| CI-Pipeline                                    | `.github/workflows/ci.yml`                                           |
| Produkt-Entscheidungen, Pin-Gründe             | `NOTES.md`                                                           |
| Was wo gebaut wird (Phasenbriefe)              | `docs/plans/01–05*.md`                                               |
| Langfristige Architektur-Referenz (kein Brief) | `docs/architecture/overview.md`, `docs/architecture/threat-model.md` |

## Was in Plan 01 absichtlich **nicht** drin ist

Die komplette Kernfunktionalität. `WindowEnumerator`, `WindowController`, `DwmThumbnail`, `SidebarOverlay`, `StageController`, Hotkeys, echte Settings-UI, Auto-Update, Mica/Acrylic — all das ist aktuell nur Interface plus `NotImplemented*`-Stub. Das ist kein Bug, das ist Stufen-Aufbau:

- **Plan 02** liefert die DWM-Thumbnail-Mechanik (das technische Kernstück — Live-Miniaturen fremder Fenster).
- **Plan 03** baut die Bühnen-Logik (Enable/Disable/Swap) und Multi-Monitor.
- **Plan 04** die UI-Politur (echtes Settings-Fenster, Swap-Animationen, Mica, Welcome-Fenster).
- **Plan 05** die Distribution (Velopack-Auto-Update, Code-Signing, Release-Workflow).

Ab Plan 02 wird es anfangen wie eine echte App zu wirken. Plan 01 liefert das Fundament, auf dem die nächsten vier Pläne aufbauen, ohne dass sich die Architektur dabei grundsätzlich ändert.

## Ein paar Begriffe, die oft fallen

- **HWND** — Handle auf ein Window. Ein `IntPtr`, der ein Fenster bei Windows eindeutig identifiziert. Wird recycled, daher die Kombination mit Prozess-Startzeit.
- **DWM** — Desktop Window Manager. Die Komponente in Windows, die alles auf den Bildschirm zeichnet. Stellt auch die Thumbnail-API bereit, mit der man Live-Miniaturen eines fremden Fensters in seinem eigenen UI zeigt.
- **Work Area** — der Teil des Bildschirms, der nicht von der Taskbar oder ähnlichen AppBars verdeckt ist. Maximierte Fenster füllen nur die Work Area. Stage Manager passt die Work Area an, um Platz für die Sidebar zu machen.
- **Cloaked Window** — ein Fenster, das technisch existiert aber nicht sichtbar ist (typisch für UWP-Apps im Hintergrund). Muss man herausfiltern.
- **STA-Thread** — "Single-Threaded Apartment". COM/UI-Objekte gehören bestimmten Threads; der Main-Thread einer WPF-App ist STA. Ohne dieses Modell geht WPF nicht an.
- **P/Invoke** — "Platform Invoke". Der Mechanismus, mit dem C# C-Funktionen aus nativen DLLs aufruft.
- **Code-behind** — die `.cs`-Datei zu einer `.xaml`-Datei. XAML beschreibt die Oberfläche, Code-behind die Logik (Event-Handler, etc). Modernere WPF-Projekte bevorzugen MVVM statt Code-behind; wir mischen das pragmatisch.
- **DI** — Dependency Injection. Siehe `ServiceConfiguration.cs`.

## Weiter lesen

- Architektur-Referenz: [`docs/architecture/overview.md`](docs/architecture/overview.md)
- Sicherheitsmodell: [`docs/architecture/threat-model.md`](docs/architecture/threat-model.md)
- Die Pläne selbst: [`docs/plans/`](docs/plans/)
