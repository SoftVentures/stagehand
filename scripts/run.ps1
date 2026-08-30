#Requires -Version 7
<#
.SYNOPSIS
    Startet die produktive Stagehand-App (Tray-Icon).
.DESCRIPTION
    Argumente werden an die App weitergereicht (z. B. --verbose,
    --diagnostics).
.EXAMPLE
    ./scripts/run.ps1 --verbose
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
dotnet run --project app/src/App.Shell -- @args
