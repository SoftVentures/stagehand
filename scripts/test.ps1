#Requires -Version 7
<#
.SYNOPSIS
    Führt die xUnit-Suite aus.
.DESCRIPTION
    Default-Configuration ist Release damit der TreatWarningsAsErrors-Pfad
    mitläuft. Ein optionaler Filter wird entweder als Klassennamen-Substring
    interpretiert oder als voller --filter-String wenn er '~' oder '='
    enthält.
.EXAMPLE
    ./scripts/test.ps1
    Führt alle Tests aus.
.EXAMPLE
    ./scripts/test.ps1 StageControllerTests
    Führt nur Tests in dieser Klasse aus (Substring-Match).
.EXAMPLE
    ./scripts/test.ps1 'FullyQualifiedName~Snapshot'
    Voller --filter-String wird durchgereicht.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Filter
)

$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."

if ([string]::IsNullOrWhiteSpace($Filter)) {
    dotnet test App.sln -c Release
}
else {
    if ($Filter -notmatch '[~=]') {
        $Filter = "FullyQualifiedName~$Filter"
    }
    dotnet test App.sln -c Release --filter $Filter
}
