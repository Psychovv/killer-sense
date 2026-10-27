<#
  killer-sense / Fase 1 - roda os snapshots SOMENTE LEITURA num unico UAC e imprime um resumo.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Usa snapshot-get.ps1 e snapshot-smi.ps1 (Get* ja aprovados). SysInfo so com -WithSysInfo
  (grava EC.PSEE = 1 a cada chamada; exige aprovacao do usuario).
  Uso (nao-admin): run-snapshots.ps1 -Name <nome> [-WithSysInfo]
#>
param(
  [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Name,
  [switch]$WithSysInfo
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$rec  = Join-Path $root 'scripts\recon'
$out  = Join-Path $root 'recon-out'
New-Item -ItemType Directory -Force $out | Out-Null

$get = Join-Path $out "snap-$Name.json"; $smi = Join-Path $out "smi-$Name.json"; $sys = Join-Path $out "sysinfo-$Name.json"
$cmd = "& '$rec\snapshot-get.ps1' -OutFile '$get' -Label '$Name'; & '$rec\snapshot-smi.ps1' -OutFile '$smi' -Label '$Name'"
if ($WithSysInfo) { $cmd += "; & '$rec\read-sysinfo.ps1' -OutFile '$sys'" }
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
$p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden `
       -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $enc)
"exit=$($p.ExitCode)"

$g = (Get-Content $get -Raw | ConvertFrom-Json).Results
$s = (Get-Content $smi -Raw | ConvertFrom-Json).Results
function B1($o) { if ($o -match 'bytes\(LSB\.\.\)= (\w\w) (\w\w)') { [Convert]::ToInt32($Matches[2], 16) } }
$opms = B1 ($g | Where-Object { $_.Method -eq 'GetGamingMiscSetting' -and $_.Input -eq '0x0B' }).Output
$tkst = B1 ($g | Where-Object { $_.Method -eq 'GetGamingMiscSetting' -and $_.Input -eq '0x01' }).Output
$cpuf = B1 ($g | Where-Object { $_.Method -eq 'GetGamingFanSpeed' -and $_.Input -eq '0x01' }).Output
$gpuf = B1 ($g | Where-Object { $_.Method -eq 'GetGamingFanSpeed' -and $_.Input -eq '0x04' }).Output
$fb   = ($s | Where-Object { $_.Method -eq 'GetGamingFanBehavior' -and $_.Input -match 'gmInput=0x0+9 ' }).Output.gmOutput
$prof = ($s | Where-Object { $_.Method -eq 'GetGamingProfile' -and $_.Input -match 'gmInput=0x0+ ' }).Output.gmOutput
$bat  = $s | Where-Object { $_.Class -eq 'BatteryControl' }
"OPMS=$opms  TKST=$tkst  CPUF=$cpuf  GPUF=$gpuf"
"FanBehavior(0x09) = $fb"
"GetGamingProfile(0) = $prof"
"Bateria: list=$($bat.Output.uFunctionList) status=$($bat.Output.uFunctionStatus) $($bat.Error)"
if ($WithSysInfo) { (Get-Content $sys -Raw | ConvertFrom-Json).Samples | Format-Table -AutoSize | Out-String -Width 200 }
