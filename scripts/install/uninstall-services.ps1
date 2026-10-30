<#
  killer-sense - remove o servico e o watchdog (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Para o servico principal primeiro (ele devolve o Auto ao parar), depois o watchdog; apaga as entradas
  e a pasta em "C:\Program Files\KillerSense". Os logs em %ProgramData%\KillerSense ficam.
#>
$ErrorActionPreference = 'Stop'
foreach ($name in 'KillerSense', 'KillerSenseWatchdog') {
  $svc = Get-Service $name -ErrorAction SilentlyContinue
  if (-not $svc) { "nao instalado: $name"; continue }
  sc.exe failure $name reset= 0 actions= '""' | Out-Null   # sem reinicio automatico durante a remocao
  if ($svc.Status -ne 'Stopped') { Stop-Service $name -Force; $svc.WaitForStatus('Stopped', '00:00:20') }
  sc.exe delete $name | Out-Null
  "removido: $name"
}
$dest = Join-Path $env:ProgramFiles 'KillerSense'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force; "pasta removida: $dest" }
