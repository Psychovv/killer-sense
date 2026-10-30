<#
  killer-sense - teste no hardware: matar o servico a forca e conferir que o watchdog devolve o Auto.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Rodar como ADMINISTRADOR. Escreve no firmware (servico em curva) - so com aprovacao do usuario.
  Uso: kill-service-test.ps1 -OutFile <arquivo> [-CpuEntry 40] [-RunSeconds 15] [-ExternalOverride]
  -ExternalOverride: em vez de matar, outro processo poe as fans em Auto (simula o PredatorSense);
  o servico deve pausar e NAO regravar o Custom.
#>
param(
  [Parameter(Mandatory)][string]$OutFile,
  [double]$CpuEntry = 40,
  [int]$RunSeconds = 15,
  [switch]$ExternalOverride
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$env:DOTNET_ROOT = Join-Path $root 'tools\dotnet'
$bin = 'bin\x64\Debug\net10.0-windows\win-x64'
$svc = Join-Path $root "src\KillerSense.Service\$bin\KillerSense.Service.exe"
$wd = Join-Path $root "src\KillerSense.Watchdog\$bin\KillerSense.Watchdog.exe"
$hw = Join-Path $root "tests\KillerSense.HwTests\$bin\KillerSense.HwTests.exe"
foreach ($f in $svc, $wd, $hw) { if (-not (Test-Path $f)) { throw "nao achei $f" } }
$logDir = Split-Path $OutFile

function Note($m) { "$((Get-Date).ToString('HH:mm:ss.fff')) $m" | Out-File -Encoding utf8 -Append $OutFile }
function State { (& $hw read 2>&1 | Select-Object -Last 1) }

Note "=== inicio; estado: $(State)"
$w = Start-Process $wd -PassThru -WindowStyle Hidden -RedirectStandardOutput "$logDir\wd.out" -RedirectStandardError "$logDir\wd.err"
$s = Start-Process $svc -ArgumentList '--cpu-entry', $CpuEntry -PassThru -WindowStyle Hidden -RedirectStandardOutput "$logDir\svc.out" -RedirectStandardError "$logDir\svc.err"
Note "watchdog pid=$($w.Id) servico pid=$($s.Id)"
try {
  Start-Sleep -Seconds $RunSeconds
  Note "antes: $(State)"
  if ($ExternalOverride) {
    & $hw auto 2>&1 | Out-Null
    Note '>>> fans postas em Auto por outro processo'
    for ($i = 1; $i -le 5; $i++) { Start-Sleep -Seconds 1; Note ("t+{0}s {1}" -f $i, (State)) }
    return
  }
  Stop-Process -Id $s.Id -Force
  $killed = Get-Date
  Note '>>> servico morto a forca'
  for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Milliseconds 700
    $st = State
    Note ("t+{0:0.0}s {1}" -f ((Get-Date) - $killed).TotalSeconds, $st)
    if ($st -match 'CPU: Auto' -and $st -match 'GPU: Auto') { Note '>>> Auto confirmado'; break }
  }
} finally {
  if (-not $s.HasExited) { Stop-Process -Id $s.Id -Force }
  Start-Sleep -Seconds 1
  if (-not $w.HasExited) { Stop-Process -Id $w.Id -Force }
  & $hw auto 2>&1 | Out-Null   # garantia final: Auto
  Note "=== fim; estado: $(State)"
}
