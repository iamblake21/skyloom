$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ChangingMyLife.uproject'
$scriptFile = Join-Path $projectRoot 'Content\Python\cml_embedded_texture_import.py'
$editor = 'D:\Program Files (x86)\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$processTemp = 'D:\Changing My Life\UnrealGenerated\ProcessTemp'

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
New-Item -ItemType Directory -Path $processTemp -Force | Out-Null
try {
    $env:TEMP = $processTemp
    $env:TMP = $processTemp
    & $editor $projectFile `
        -unattended -nop4 -nosplash -NullRHI `
        "-ExecutePythonScript=$scriptFile" `
        "-log=$projectRoot\Saved\Logs\UnityEmbeddedTextureImport.log"
    $editorExitCode = $LASTEXITCODE
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}

if ($editorExitCode -ne 0) {
    throw "Unreal embedded texture import process failed with exit code $editorExitCode"
}

$reportPath = Join-Path $projectRoot 'Migration\unity_embedded_texture_import_report.json'
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "CML embedded texture importer did not create its report: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ([int]$report.failed -ne 0) {
    throw "CML embedded texture importer reported $($report.failed) failures. See $reportPath"
}
Write-Host "Embedded texture import completed: $($report.imported)/$($report.requested). Report: $reportPath"
