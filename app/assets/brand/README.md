# Stagehand brand assets

Source of truth for all application icons.

## Layout

```
icons/
  colored/ — default variant used in most contexts (tray, installer, alt-tab, window)
  dark/    — for light-theme OS chrome (high-contrast-on-light)
  light/   — for dark-theme OS chrome
stagehand-icons.zip — archived originals, provenance only
```

## Editing rule

- `icon.svg` is the master. When it changes, regenerate `icon.ico`
  (16 / 32 / 48 / 64 / 128 / 256 px frames recommended) with ImageMagick or
  icotool, and commit both the `.svg` and the `.ico`.

## Licence

TODO: confirm licence terms with the author before external distribution.
Treat as "all rights reserved, in-project use only" until that's clarified.

## Build integration

`app/build/copy-brand-assets.targets` copies the three `.ico` variants into
`app/src/App.Shell/Assets/TrayIcon{,.Dark,.Light}.ico` at every build.
That destination folder is `.gitignore`d — the source of truth for icons
lives here.
