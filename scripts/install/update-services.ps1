<#
  killer-sense - atualiza os servicos instalados com uma nova publicacao (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Para o servico principal primeiro (ele devolve o Auto ao parar), depois o watchdog; troca os arquivos
  em "C:\Program Files\KillerSense" e inicia os dois de novo (watchdog antes).
  Uso: update-services.ps1 -PublishDir <pasta com Service\ e Watchdog\>
#>
param([Parameter(Mandatory)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:ProgramFiles 'KillerSense'
foreach ($d in 'Service', 'Watchdog') {
  if (-not (Test-Path (Join-Path $PublishDir $d))) { throw "Publicacao nao encontrada: $PublishDir\$d" }
}
foreach ($name in 'KillerSense', 'KillerSenseWatchdog') {
  $svc = Get-Service $name -ErrorAction Stop
  if ($svc.Status -ne 'Stopped') { Stop-Service $name -Force; $svc.WaitForStatus('Stopped', '00:00:20') }
  "parado: $name"
}
foreach ($d in 'Service', 'Watchdog') {
  $target = Join-Path $dest $d
  Remove-Item $target -Recurse -Force
  Copy-Item (Join-Path $PublishDir $d) $target -Recurse
  "atualizado: $target"
}
foreach ($name in 'KillerSenseWatchdog', 'KillerSense') {
  Start-Service $name
  "iniciado: $name ($((Get-Service $name).Status))"
}
