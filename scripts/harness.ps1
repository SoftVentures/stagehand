#Requires -Version 7
<#
.SYNOPSIS
    Startet den manuellen Test-Harness.
.DESCRIPTION
    Plan-02- und Plan-03-Buttons (Enable/Disable Stage, Park, Restore,
    Hook-Log). Hat einen eigenen Tray-freien Pfad und öffnet eine
    Konsole für Logs.
#>
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
dotnet run --project tests/App.Harness -- @args
