<#
.SYNOPSIS
  Fails when line coverage drops below the thresholds. Reads ReportGenerator's JsonSummary.

.EXAMPLE
  ./build/check-coverage.ps1 -Summary coverage/Summary.json -MinTotal 80 -MinCore 90
#>
param(
    [Parameter(Mandatory)] [string] $Summary,
    [double] $MinTotal = 80,
    [double] $MinCore = 90
)

$ErrorActionPreference = 'Stop'
$json = Get-Content $Summary -Raw | ConvertFrom-Json
$total = [double] $json.summary.linecoverage
$core = [double] ($json.coverage.assemblies | Where-Object name -eq 'MonitorSwitch.Core').coverage

Write-Host ("Line coverage: total {0}% (min {1}%), MonitorSwitch.Core {2}% (min {3}%)" -f $total, $MinTotal, $core, $MinCore)

$failed = $false
if ($total -lt $MinTotal) { Write-Host "::error::Total line coverage $total% is below $MinTotal%"; $failed = $true }
if ($core -lt $MinCore) { Write-Host "::error::MonitorSwitch.Core line coverage $core% is below $MinCore%"; $failed = $true }
if ($failed) { exit 1 }
