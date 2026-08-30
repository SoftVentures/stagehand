#Requires -Version 7
<#
.SYNOPSIS
    Debug-Build der gesamten Solution.
.DESCRIPTION
    Schnell, ohne TreatWarningsAsErrors. Nutze build-release.ps1 für die
    strenge CI-Parity-Variante. Argumente werden an `dotnet build`
    weitergereicht.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
dotnet build App.sln @args
