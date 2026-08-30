#Requires -Version 7
<#
.SYNOPSIS
    CI-Parity-Check: Build + Tests + alle vier Formatter im Verify-Modus.
.DESCRIPTION
    Wenn das hier grün ist, ist auch die GitHub-Actions-CI grün. Bricht
    beim ersten Fehler ab, damit du sofort siehst welcher Gate hängt.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."

Write-Host '→ Release-Build (TreatWarningsAsErrors)' -ForegroundColor Cyan
dotnet build App.sln -c Release

Write-Host '→ Tests' -ForegroundColor Cyan
dotnet test App.sln -c Release --no-build

Write-Host '→ csharpier check' -ForegroundColor Cyan
dotnet csharpier check .

Write-Host '→ XAML Styler passive check' -ForegroundColor Cyan
dotnet xstyler --recursive --directory app --passive

Write-Host '→ prettier check' -ForegroundColor Cyan
npx prettier --check .

Write-Host '→ dotnet format verify' -ForegroundColor Cyan
dotnet format App.sln --verify-no-changes --severity warn

Write-Host '✓ alle Gates grün — bereit zum Push' -ForegroundColor Green
