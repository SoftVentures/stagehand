#Requires -Version 7
<#
.SYNOPSIS
    First-time-Setup auf einer frischen Maschine.
.DESCRIPTION
    Installiert npm-DevDeps (prettier), .NET-Tools (csharpier, xamlstyler)
    und macht den initialen NuGet-Restore.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."

Write-Host '→ npm ci (prettier)' -ForegroundColor Cyan
npm ci

Write-Host '→ dotnet tool restore (csharpier, xamlstyler)' -ForegroundColor Cyan
dotnet tool restore

Write-Host '→ dotnet restore (NuGet, packages.lock.json)' -ForegroundColor Cyan
dotnet restore App.sln

Write-Host '✓ setup fertig — ./scripts/build.ps1 zum Bauen' -ForegroundColor Green
