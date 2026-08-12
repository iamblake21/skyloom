<#
.SYNOPSIS
    Runs one of the migration Python scripts inside UnrealEditor-Cmd.

.DESCRIPTION
    Every migration step is an idempotent Python script under Content/Python that
    writes a JSON report under Migration/. This wrapper keeps the editor
    invocation identical across steps: unattended, no RHI, dedicated TEMP (the
    system drive is small), and a per-script log.
#>
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string]$LogName,
    [hashtable]$Environment = @{},
    # -NullRHI skips real shader compilation, which is fast but cannot prove a
    # ported material actually compiles. Steps that author materials must run at
    # least once with -RealRHI so the shader compiler reports genuine errors.
    [switch]$RealRHI
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ChangingMyLife.uproject'
$scriptFile = Join-Path $projectRoot "Content\Python\$Script"
if (-not (Test-Path -LiteralPath $scriptFile)) {
    throw "Migration script not found: $scriptFile"
}
if (-not $LogName) {
    $LogName = [System.IO.Path]::GetFileNameWithoutExtension($Script)
}

$editor = 'D:\Program Files (x86)\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$processTemp = 'D:\Changing My Life\UnrealGenerated\ProcessTemp'
New-Item -ItemType Directory -Path $processTemp -Force | Out-Null

$previous = @{ TEMP = $env:TEMP; TMP = $env:TMP }
foreach ($key in $Environment.Keys) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key)
    [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key])
}
try {
    $env:TEMP = $processTemp
    $env:TMP = $processTemp
    # -ABSLOG is the variant that honours a full path; -log only renames the
    # file inside Saved/Logs and silently ignores a path prefix.
    $rhiArgs = if ($RealRHI) { @() } else { @('-NullRHI') }
    & $editor $projectFile `
        -unattended -nop4 -nosplash @rhiArgs `
        "-ExecutePythonScript=$scriptFile" `
        "-ABSLOG=$projectRoot\Saved\Logs\$LogName.log"
    $editorExitCode = $LASTEXITCODE
}
finally {
    foreach ($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key])
    }
}

$logPath = Join-Path $projectRoot "Saved\Logs\$LogName.log"
# The Unreal Python commandlet does not propagate sys.exit reliably, so the
# scripts emit an explicit success marker and the log is the source of truth.
$log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
if ($log -match 'CML_[A-Z_]+_FAILED') {
    $failures = [regex]::Matches($log, '(?m)^.*(LogPython: Error|CML_[A-Z_]+_FAILED).*$') |
        ForEach-Object { $_.Value } | Select-Object -Last 40
    throw "$Script reported failure:`n$($failures -join "`n")"
}
if ($log -notmatch 'CML_[A-Z_]+_SUCCEEDED') {
    throw "$Script did not reach its success marker. See $logPath"
}
# A material that fails to compile is reported by the shader compiler, not by
# the Python API, so the log is the only place the failure surfaces.
$shaderErrors = [regex]::Matches(
    $log,
    '(?m)^.*(Failed to compile Material|Failed to compile Material Instance|LogMaterial: Error).*$'
) | ForEach-Object { $_.Value } | Select-Object -Unique
if ($shaderErrors) {
    throw "$Script produced material compile errors:`n$(($shaderErrors | Select-Object -First 25) -join "`n")"
}
if ($editorExitCode -ne 0) {
    Write-Warning "UnrealEditor-Cmd returned $editorExitCode but the script reported success."
}
Write-Host "$Script completed. Log: $logPath"
