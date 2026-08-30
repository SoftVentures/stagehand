#Requires -Version 7
<#
.SYNOPSIS
    Release-Build (TreatWarningsAsErrors aktiv).
.DESCRIPTION
    CA1848 und andere Analyzer werden zu Fehlern. Wenn dieser Build grün
    ist, ist auch die GitHub-Actions-CI grün.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
dotnet build App.sln -c Release @args
