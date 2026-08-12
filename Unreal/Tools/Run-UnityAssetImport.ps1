param(
    [ValidateSet('mesh', 'texture', 'mesh,texture')]
    [string]$Kinds = 'mesh,texture',
    [int]$Limit = 0,
    [int]$BatchSize = 32
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ChangingMyLife.uproject'
$scriptFile = Join-Path $projectRoot 'Content\Python\cml_asset_import.py'
$editor = 'D:\Program Files (x86)\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$processTemp = 'D:\Changing My Life\UnrealGenerated\ProcessTemp'

$env:CML_IMPORT_KINDS = $Kinds
$env:CML_IMPORT_LIMIT = [string]$Limit
$env:CML_IMPORT_BATCH_SIZE = [string]$BatchSize

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
New-Item -ItemType Directory -Path $processTemp -Force | Out-Null
try {
    $env:TEMP = $processTemp
    $env:TMP = $processTemp
    & $editor $projectFile `
        -unattended -nop4 -nosplash -NullRHI `
        "-ExecutePythonScript=$scriptFile" `
        "-log=$projectRoot\Saved\Logs\UnityAssetImport.log"
    $editorExitCode = $LASTEXITCODE
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}

if ($editorExitCode -ne 0) {
    throw "Unreal asset import process failed with exit code $editorExitCode"
}

$reportPath = Join-Path $projectRoot 'Migration\unity_asset_import_report.json'
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "CML asset importer did not create its report: $reportPath"
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if (-not [bool]$report.complete -or [int]$report.processed -ne [int]$report.requested) {
    throw "CML asset importer report is incomplete: $($report.processed)/$($report.requested). See $reportPath"
}
if ([int]$report.failed -ne 0) {
    throw "CML asset importer reported $($report.failed) failed assets. See $reportPath"
}

Write-Host "Unity asset import completed: $($report.imported)/$($report.requested). Report: $reportPath"
