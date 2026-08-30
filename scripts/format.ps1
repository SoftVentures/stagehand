#Requires -Version 7
<#
.SYNOPSIS
    Lässt alle vier Formatter im Auto-Fix-Modus laufen.
.DESCRIPTION
    Reihenfolge: csharpier (C#) → XAML Styler → prettier (Markdown/YAML/
    JSON) → dotnet format (Whitespace + csproj). Hält an, sobald einer
    fehlschlägt, damit du sofort siehst welcher.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."

Write-Host '→ csharpier (C#)' -ForegroundColor Cyan
dotnet csharpier format .

Write-Host '→ XAML Styler (XAML in app/)' -ForegroundColor Cyan
dotnet xstyler --recursive --directory app

Write-Host '→ prettier (Markdown / YAML / JSON)' -ForegroundColor Cyan
npx prettier --write .

Write-Host '→ dotnet format (Whitespace + csproj)' -ForegroundColor Cyan
dotnet format App.sln --severity warn

Write-Host '✓ alle Formatter durchgelaufen' -ForegroundColor Green
