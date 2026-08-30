#Requires -Version 7
<#
.SYNOPSIS
    Vollständiger Clean: Build-Artefakte, Lock-Files, generierte Brand-Assets.
.DESCRIPTION
    Anschließend ist `dotnet restore` lauffähig — der nächste Build ist
    wieder komplett "from scratch". Nur ausführen wenn der Build oder
    Asset-Copy Ärger macht.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."

Write-Host '→ artifacts/ und alle bin/ obj/ Ordner' -ForegroundColor Cyan
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    artifacts, `
    app/src/App.Core/bin, app/src/App.Core/obj, `
    app/src/App.Interop/bin, app/src/App.Interop/obj, `
    app/src/App.Services/bin, app/src/App.Services/obj, `
    app/src/App.Shell/bin, app/src/App.Shell/obj, `
    tests/App.Tests/bin, tests/App.Tests/obj, `
    tests/App.Harness/bin, tests/App.Harness/obj

Write-Host '→ generierte Brand-Asset-Icons' -ForegroundColor Cyan
Get-ChildItem -Path app/src/App.Shell/Assets -Filter 'TrayIcon*.ico' -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Host '→ packages.lock.json (werden vom restore neu geschrieben)' -ForegroundColor Cyan
Get-ChildItem -Recurse -Filter 'packages.lock.json' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\node_modules\\' } |
    Remove-Item -Force

Write-Host '→ dotnet restore' -ForegroundColor Cyan
dotnet restore App.sln

Write-Host '✓ Clean fertig — nächster build ist frisch' -ForegroundColor Green
