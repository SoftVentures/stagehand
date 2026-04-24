# Stagehand

Stagehand is a lightweight Windows desktop app (.NET 8 / WPF) that brings a
macOS-Stage-Manager-style workflow to Windows 11: the current app stays front
and centre while the rest of your windows are grouped into "stages" that live
as thumbnails along the edge of the screen. Click a stage to swap in its
windows; everything else gets tucked away. It runs in the tray, is keyboard-
driven, and tries hard to stay out of the way of fullscreen games and
video calls.

## Quickstart

Prerequisites: **.NET 8 SDK** (8.0.420 or newer), **Windows 10 2004 / Windows 11**,
and **Node 22+** (for the prettier/markdown tooling only).

```pwsh
# From the repo root
dotnet tool restore                         # csharpier + xstyler
dotnet build                                # Stage 2 onward
dotnet run --project app/src/App.Shell  # launch the app
```

Nothing will build yet at Stage 1 — the solution skeleton arrives in Stage 2.

## Docs

- Implementation plans: [`docs/plans/`](docs/plans/) — staged roadmap, one
  document per track (architecture, window mechanics, lifecycle, settings,
  distribution).
- Architecture notes (as they land): [`docs/architecture/`](docs/architecture/).
- Decisions log: [`NOTES.md`](NOTES.md).

## License

MIT — see [`LICENSE`](LICENSE).
