<#
  killer-sense - testes com os servicos INSTALADOS (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  T3: parada normal (Stop-Service) -> o servico devolve o Auto sozinho; depois reinicia.
  T4: processo do servico morto a forca -> watchdog devolve o Auto; o Windows reinicia o servico.
  Uso: service-recovery-test.ps1 -OutFile <arquivo>
#>
param([Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$env:DOTNET_ROOT = Join-Path $root 'tools\dotnet'
$hw = Join-Path $root 'tests\KillerSense.HwTests\bin\x64\Debug\net10.0-windows\win-x64\KillerSense.HwTests.exe'
function Note($m) { "$((Get-Date).ToString('HH:mm:ss.fff')) $m" | Out-File -Encoding utf8 -Append $OutFile }
function State { (& $hw read 2>&1 | Select-Object -Last 1) }
function SvcPid { (Get-CimInstance Win32_Service -Filter "Name='KillerSense'").ProcessId }

Note "=== T3 parada normal; antes: $(State)"
Stop-Service KillerSense
Note "parado ($((Get-Service KillerSense).Status)): $(State)"
Start-Service KillerSense
Start-Sleep -Seconds 4
Note "reiniciado ($((Get-Service KillerSense).Status)): $(State)"

Start-Sleep -Seconds 4
$old = SvcPid
Note "=== T4 kill; pid=$old; antes: $(State)"
Stop-Process -Id $old -Force
$t0 = Get-Date
$autoSeen = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 700
  $st = State
  $p = SvcPid
  Note ("t+{0:0.0}s svc={1} pid={2} | {3}" -f ((Get-Date) - $t0).TotalSeconds, (Get-Service KillerSense).Status, $p, $st)
  if (-not $autoSeen -and $st -match 'CPU: Auto') { $autoSeen = $true; Note '>>> Auto pelo watchdog' }
  if ($p -and $p -ne $old -and $p -ne 0) { Note ">>> servico reiniciado pelo Windows (pid=$p)"; Start-Sleep -Seconds 3; Note "depois do reinicio: $(State)"; break }
}
Note "=== fim: servico=$((Get-Service KillerSense).Status) watchdog=$((Get-Service KillerSenseWatchdog).Status)"
