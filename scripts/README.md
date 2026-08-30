# scripts/

PowerShell-Helfer für die häufigsten Workflows. Aufrufbar von überall —
die Scripts wechseln intern selbst ins Repo-Root.

> **Voraussetzung:** PowerShell 7+ (`pwsh`). Auf Windows 11 vorinstalliert
> oder via `winget install Microsoft.PowerShell`. Die Windows-eigene
> "Windows PowerShell" 5.1 reicht nicht — `#Requires -Version 7` schlägt fehl.

## Workflow-Cheatsheet

```powershell
./scripts/setup.ps1            # Einmal pro Maschine: npm + tools + restore
./scripts/build.ps1            # Schneller Debug-Build während der Entwicklung
./scripts/test.ps1             # Alle 229 Tests
./scripts/test.ps1 StageController   # Nur eine Test-Klasse (Substring-Match)
./scripts/run.ps1              # Stagehand starten (Tray-Icon erscheint)
./scripts/harness.ps1          # Manuellen Harness starten (Plan-02/03-Buttons)
./scripts/format.ps1           # Alle vier Formatter im Auto-Fix-Modus
./scripts/check.ps1            # CI-Parity: Release-Build + Tests + 4 Format-Gates
./scripts/clean.ps1            # Voller Clean wenn was hängt
```

## npm-run-Aliase

Dieselben Scripts sind in `package.json` als npm-Tasks eingetragen. Wer
ohnehin `npm run` im Muscle-Memory hat, kann das nutzen:

```bash
npm run setup
npm run build           # = build.ps1
npm run build:release   # = build-release.ps1
npm test                # = test.ps1   (Argumente nach `--`: npm test -- StageController)
npm start               # = run.ps1
npm run harness
npm run format
npm run check
npm run clean
```

Die PowerShell-Datei ist die Wahrheit — die npm-Aliase sind reine Convenience-Wrapper
und benötigen `pwsh` im PATH.

## Reihenfolge vor einem Commit

1. `./scripts/format.ps1` — räumt Whitespace und Stil auf
2. `./scripts/check.ps1` — beweist dass der Push grün durchläuft

## Reihenfolge nach `git pull` mit Versionsbumps

1. `./scripts/clean.ps1` — wirft alte `packages.lock.json` weg
2. `./scripts/setup.ps1` — wenn `package.json` oder Tool-Manifest geändert wurde
3. `./scripts/build.ps1`

## Hinweise

- Argumente werden an die zugrundeliegenden Tools weitergereicht:
  `./scripts/build.ps1 -v normal` ⇒ `dotnet build App.sln -v normal`.
- `test.ps1` akzeptiert entweder einen Klassennamen-Substring
  (`./scripts/test.ps1 SnapshotStore`) oder einen vollen `--filter`-String
  mit `~` / `=` (`./scripts/test.ps1 'Name=Empty_IsDisabled_WithNoScenes'`).
- `run.ps1` und `harness.ps1` reichen Argumente an die App durch, z. B.
  `./scripts/run.ps1 --verbose --diagnostics`.

## Execution-Policy-Hinweis

Falls PowerShell die Ausführung blockiert (`cannot be loaded because running
scripts is disabled`), einmalig pro User:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Die Scripts liegen lokal und sind nicht "remote" — `RemoteSigned` reicht.
