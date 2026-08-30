# Plan 03 — Schritt-für-Schritt Walkthrough

> Linearer Durchlauf zum Abhaken. Jeder Schritt ist atomar (eine Aktion + eine Erwartung). Schreib unter jeden `Behave:`-Block, was passiert ist. Wenn das Verhalten der Erwartung entspricht, reicht `behave`. Bei Abweichung kurz beschreiben, was du beobachtet hast.

## Vorbereitung

**1.** PowerShell 7+ im Repo-Root öffnen (`D:\Organizations\SoftVentures\stagehand`).

> Behave:

**2.** `./scripts/check.ps1` laufen lassen — Build + 231 Tests + 4 Format-Gates müssen grün sein.

> Behave:

**3.** Folgende 6 Fenster aus 5 verschiedenen Prozessen öffnen, alle sichtbar auf dem primären Monitor:

- 2× Notepad (zwei Instanzen, gleicher Prozess-Pfad)
- 1× Taschenrechner (Calculator)
- 1× Microsoft Edge
- 1× Datei-Explorer
- 1× freie Wahl (Spotify / VS Code / etc.)

> Behave:

**4.** Aktuelle Position und Größe jedes Fensters merken (Screenshot oder mental). Diese Layout muss nach `Disable` exakt wiederhergestellt sein.

> Behave:

**5.** `./scripts/harness.ps1` starten. Fenster „Stagehand Harness" mit Buttons `Refresh`, `Enable Stage (Plan 03)`, `Disable Stage (Plan 03)` öffnet sich.

> Behave:

## Single-Monitor: Toggle-Round-Trip

**6.** Auf `Refresh` klicken. Liste füllt sich mit den 6 Fenstern.

> Behave:

**7.** Auf `Enable Stage (Plan 03)` klicken.

> Behave:

**8.** Sidebar-Overlay erscheint am linken Rand des Monitors (~200 px breit, halbtransparent, mit weißer Border-Akzentlinie rechts).

> Behave:

**9.** Sidebar zeigt **5 Tiles** (die zwei Notepads sind zu **einer** Scene zusammengefasst).

> Behave:

**10.** Das vorher aktive Fenster füllt den Hauptbereich rechts neben der Sidebar (also ab Pixel 200 vom linken Rand bis Bildschirmrand).

> Behave:

**11.** Die anderen 4 (bzw. 5 inkl. zweitem Notepad) Fenster sind off-screen geparkt — du siehst sie nirgends mehr außer als Live-Thumbnail in der Sidebar.

> Behave:

**12.** Taskleiste ist auf die freie Fläche rechts neben der Sidebar verkürzt (Work Area angepasst).

> Behave:

## Single-Monitor: Click-Swap (Single-Window-Scene)

**13.** Auf ein Sidebar-Tile klicken, das **kein** Notepad ist (z.B. Calculator-Tile).

> Behave:

**14.** Das geklickte Fenster kommt nach vorn und füllt den Hauptbereich.

> Behave:

**15.** Das vorher im Hauptbereich aktive Fenster wandert als Tile in die Sidebar.

> Behave:

## Single-Monitor: Click-Swap (Multi-Window-Scene)

**16.** Auf das Notepad-Tile in der Sidebar klicken.

> Behave:

**17.** **Beide** Notepad-Fenster werden gleichzeitig sichtbar im Hauptbereich, an ihren ursprünglichen Pre-Enable-Positionen.

> Behave:

**18.** Das vorher aktive Fenster wandert als **eines** Tile zurück in die Sidebar.

> Behave:

## Single-Monitor: Alt-Tab Foreground-Swap

**19.** Alt-Tab drücken, ein anderes geparktes Fenster (nicht das aktuell aktive) auswählen, loslassen.

> Behave:

**20.** Stagehand erkennt den Foreground-Wechsel und swappt die ganze Scene des angetabten Fensters in den Hauptbereich.

> Behave:

**21.** Die Sidebar reorganisiert sich entsprechend (vorher aktives Tile geht zurück, neu aktives ist weg).

> Behave:

## Single-Monitor: Win+Up Snap respektiert Sidebar

**22.** Aktives Fenster im Hauptbereich fokussieren, `Win+Up` drücken.

> Behave:

**23.** Fenster maximiert nur in den Hauptbereich (linker Rand des Fensters = rechter Rand der Sidebar = Pixel 200, **nicht** 0). Sidebar bleibt sichtbar.

> Behave:

## Single-Monitor: Disable Round-Trip

**24.** Auf `Disable Stage (Plan 03)` klicken.

> Behave:

**25.** Sidebar-Overlay verschwindet.

> Behave:

**26.** Alle 6 Fenster sind exakt an ihren Pre-Enable-Positionen und in ihrer Pre-Enable-Größe zurück (Vergleich mit Schritt 4).

> Behave:

**27.** Taskleiste reicht wieder bis zum linken Bildschirmrand (Work Area restauriert).

> Behave:

## Soak (Regression-Resistance)

**28.** Schritte 7→24 (Enable→Disable ohne Click-Swaps dazwischen) **20-mal** hintereinander wiederholen, ohne den Harness neu zu starten. Jede Iteration muss exakte Restaurierung liefern.

> Behave:

**29.** Harness schließen (X oder Alt+F4).

> Behave:

## Production App: Tray-Linksklick

**30.** `./scripts/run.ps1` starten. Tray-Icon (Stagehand-Logo) erscheint im System-Tray.

> Behave:

**31.** **Linksklick** auf das Tray-Icon. Stage wird enabled — Sidebar erscheint.

> Behave:

**32.** Erneut **Linksklick** auf das Tray-Icon. Stage wird disabled — Sidebar weg, Fenster zurück.

> Behave:

## Production App: Hotkey

**33.** **Ctrl+Alt+S** drücken. Stage wird enabled.

> Behave:

**34.** Erneut **Ctrl+Alt+S** drücken. Stage wird disabled.

> Behave:

## Crash-Recovery

**35.** Stage via Tray-Linksklick enablen.

> Behave:

**36.** Im Task-Manager den Prozess `App.Shell` (oder `dotnet` falls per `run.ps1` gestartet) **Force-Kill** (`taskkill /F` oder „Task beenden").

> Behave:

**37.** Erwartung: Geparkte Fenster bleiben off-screen (dokumentierte Plan-03-Limitierung — Plan 04 fixt das mit Tray-Balloon).

> Behave:

**38.** `./scripts/run.ps1` erneut starten.

> Behave:

**39.** Innerhalb von 1–2 Sekunden sind alle Fenster zurück an ihren Pre-Enable-Positionen, Work Area restauriert.

> Behave:

**40.** Snapshot-Datei `%LOCALAPPDATA%\Stagehand\state\snapshot.json` existiert nicht mehr (gelöscht nach erfolgreichem Restore).

> Behave:

**41.** Im Log unter `%LOCALAPPDATA%\Stagehand\logs\stagehand-YYYY-MM-DD.log` steht eine Zeile wie `Crash recovery: prior Stagehand left N stages with M windows; auto-restoring`.

> Behave:

## Elevation Boundary (Task Manager)

**42.** Task Manager öffnen (er läuft elevated).

> Behave:

**43.** Stage via Tray-Linksklick enablen.

> Behave:

**44.** Im Log steht eine Zeile wie `HWND 0x… runs at a higher integrity level; left in place, marked IsElevated`.

> Behave:

**45.** Task Manager wird **nicht** geparkt — er schwebt weiter sichtbar über/neben der Sidebar.

> Behave:

**46.** Stage disablen. Task Manager bleibt an seiner Position; alle anderen Fenster gehen wie gewohnt zurück.

> Behave:

## Multi-Monitor (überspringen, falls nur 1 Monitor)

**47.** Mit zwei angeschlossenen Monitoren `./scripts/run.ps1` starten. Auf jedem Monitor je ~3 Fenster verteilen.

> Behave:

**48.** Stage via Tray-Linksklick enablen.

> Behave:

**49.** **Zwei** Sidebars erscheinen — eine pro Monitor — mit jeweils unabhängigen Tiles.

> Behave:

**50.** Fenster, die auf Monitor A waren, sind nur in A's Sidebar; B's Fenster nur in B's Sidebar.

> Behave:

**51.** Per Alt-Tab eines der Monitor-A-Fenster aktivieren, dann manuell auf Monitor B ziehen, kurz warten.

> Behave:

**52.** Innerhalb ~500 ms re-homed das Fenster: Tile verschwindet aus A's Sidebar und erscheint in B's Sidebar.

> Behave:

**53.** Monitor B physisch abziehen (Kabel raus oder im Display-Setup deaktivieren).

> Behave:

**54.** B's Sidebar verschwindet sofort. Kein Crash. Log enthält `MonitorChangeCoordinator` Eintrag.

> Behave:

**55.** Monitor B wieder anschließen.

> Behave:

**56.** Innerhalb ~500 ms erscheint B's Sidebar wieder; Tile-Liste pro Monitor passt zur jetzigen Verteilung.

> Behave:

## Cleanup

**57.** Stage via Tray-Linksklick disablen, falls noch enabled.

> Behave:

**58.** Über Tray-Kontextmenü → `Exit` die App beenden. Tray-Icon verschwindet, Prozess ist weg.

> Behave:

## Bekannte Plan-03-Limitierungen (kein Fail, sondern Plan 04/05 Scope)

- **Tile-Größen unterschiedlich** — Aspect-Ratio-erhaltend, by design.
- **Kein `+N`-Badge auf Multi-Window-Scene-Tiles** — visueller Polish in Plan 04.
- **Keine Animation beim Swap** — instant `SetWindowPos` (Plan 04 ergänzt animierten Executor).
- **Kein Tray-Balloon „Restore / Discard?"** beim Crash — Auto-Restore (Plan 04 wraps).
- **Kein Drag-and-Drop UI für `MoveWindowToScene`** — API existiert, UI ist Plan 04.
- **Sidebar-Position/-Breite/-Animationsgeschwindigkeit nicht konfigurierbar** — Plan 04 Settings UI.
- **Hot-unplug Re-Homing** — Scenes vom abgezogenen Monitor bleiben Plan-03 geparkt; Plan 04 re-homed sie auf den verbliebenen Monitor.
