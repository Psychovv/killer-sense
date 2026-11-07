<#
  killer-sense - atualiza os servicos instalados com uma nova publicacao (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Para o servico principal primeiro (ele devolve o Auto ao parar), depois o watchdog; troca os arquivos
  em "C:\Program Files\KillerSense\Services" e inicia os dois de novo (watchdog antes).
  Migra a instalacao antiga (pastas Service\ e Watchdog\, cada uma com o seu .NET): aponta os servicos
  para Services\ e so depois apaga as pastas antigas.
  Uso: update-services.ps1 -PublishDir <pasta com Services\>   (gerada por publish.ps1)
#>
param([Parameter(Mandatory)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:ProgramFiles 'KillerSense'
$source = Join-Path $PublishDir 'Services'
$target = Join-Path $dest 'Services'
$exes = [ordered]@{ KillerSense = 'KillerSense.Service.exe'; KillerSenseWatchdog = 'KillerSense.Watchdog.exe' }
foreach ($exe in $exes.Values) {
  if (-not (Test-Path (Join-Path $source $exe))) { throw "Publicacao nao encontrada: $source\$exe" }
}

function Remove-WithRetry([string]$Path) {
  # Logo apos a parada algum arquivo pode seguir travado por alguns segundos: tenta de novo.
  for ($i = 1; ; $i++) {
    try { Remove-Item $Path -Recurse -Force; break }
    catch { if ($i -ge 10) { throw }; "aguardando liberar arquivos em $Path ($i)"; Start-Sleep -Seconds 1 }
  }
}

foreach ($name in 'KillerSense', 'KillerSenseWatchdog') {
  $svc = Get-Service $name -ErrorAction Stop
  if ($svc.Status -ne 'Stopped') { Stop-Service $name -Force; $svc.WaitForStatus('Stopped', '00:00:20') }
  "parado: $name"
}

if (Test-Path $target) { Remove-WithRetry $target }
Copy-Item $source $target -Recurse
"atualizado: $target"

foreach ($name in $exes.Keys) {
  $bin = '"' + (Join-Path $target $exes[$name]) + '"'
  sc.exe config $name binPath= $bin | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "sc config $name falhou ($LASTEXITCODE)" }
  "caminho: $name -> $bin"
}

foreach ($old in 'Service', 'Watchdog') {
  $path = Join-Path $dest $old
  if (Test-Path $path) { Remove-WithRetry $path; "pasta antiga removida: $path" }
}

foreach ($name in 'KillerSenseWatchdog', 'KillerSense') {
  Start-Service $name
  "iniciado: $name ($((Get-Service $name).Status))"
}
