<#
.SYNOPSIS
  Starts a published MonitorSwitch.exe in tray mode and fails unless it becomes ready and stays up.
  Guards the exact artifact users download (single-file, compressed, ReadyToRun), which the
  test suite does not run. v0.1.0 shipped a tray that crashed on start.

.EXAMPLE
  ./build/smoke-tray.ps1 -Exe out/self-contained/MonitorSwitch.exe
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$dataDir = Join-Path ([IO.Path]::GetTempPath()) ("monitorswitch-smoke-" + [guid]::NewGuid().ToString('N'))
$log = Join-Path $dataDir 'monitorswitch.log'
$process = Start-Process -FilePath $Exe -ArgumentList '--verbose', '--data-dir', $dataDir -PassThru

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'INFO Ready in' -Quiet)) { break }
        Start-Sleep -Milliseconds 250
    }

    Start-Sleep -Seconds 2
    if (Test-Path $log) { Get-Content $log }

    if ($process.HasExited) { throw "MonitorSwitch exited during startup with code $($process.ExitCode)." }
    if (-not (Select-String -Path $log -Pattern 'INFO Ready in' -Quiet)) { throw "MonitorSwitch did not become ready within $TimeoutSeconds s." }
    if (Select-String -Path $log -Pattern ' ERROR ' -Quiet) { throw 'MonitorSwitch logged an error during startup.' }
    Write-Host 'Tray smoke test passed.'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
}
