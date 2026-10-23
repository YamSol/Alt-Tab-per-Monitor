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

# Active is the default; on a developer machine a real Alt+Tab during the smoke run must hide nothing.
New-Item -ItemType Directory -Force $dataDir | Out-Null
Set-Content -Encoding utf8 -Path (Join-Path $dataDir 'settings.json') -Value '{ "Mode": "ViewOnly" }'

# The CLR writes fail-fast reasons and stacks to stderr; a GUI exe's stderr is otherwise lost.
$stderr = Join-Path ([IO.Path]::GetTempPath()) ("monitorswitch-smoke-stderr-" + [guid]::NewGuid().ToString('N') + '.txt')
$process = Start-Process -FilePath $Exe -ArgumentList '--verbose', '--data-dir', $dataDir -PassThru -RedirectStandardError $stderr

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'INFO Ready in' -Quiet)) { break }
        Start-Sleep -Milliseconds 250
    }

    Start-Sleep -Seconds 2
    if (Test-Path $log) { Get-Content $log }

    if ($process.HasExited) {
        if ((Test-Path $stderr) -and (Get-Item $stderr).Length -gt 0) { '---- stderr'; Get-Content $stderr }

        # A crash before the first log line (runtime/host level) only shows up in the event log.
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-5) } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting' } |
            Select-Object -First 5 |
            ForEach-Object { "---- $($_.TimeCreated) $($_.ProviderName) $($_.Id)"; $_.Message }
        throw "MonitorSwitch exited during startup with code $($process.ExitCode) (0x$('{0:X8}' -f $process.ExitCode))."
    }
    if (-not (Select-String -Path $log -Pattern 'INFO Ready in' -Quiet)) { throw "MonitorSwitch did not become ready within $TimeoutSeconds s." }
    if (Select-String -Path $log -Pattern ' ERROR ' -Quiet) { throw 'MonitorSwitch logged an error during startup.' }
    Write-Host 'Tray smoke test passed.'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
    Remove-Item -Force $stderr -ErrorAction SilentlyContinue
}
