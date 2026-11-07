<#
  killer-sense - instala o servico e o watchdog como servicos do Windows (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Copia a publicacao autocontida (publish.ps1) para "C:\Program Files\KillerSense\Services" (os dois
  executaveis dividem a mesma copia do .NET), cria os servicos (inicio automatico, LocalSystem, reinicio
  apos falha) e inicia. O servico sempre comeca colocando as fans no Auto do EC.
  Uso: install-services.ps1 -PublishDir <pasta com Services\>
#>
param([Parameter(Mandatory)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:ProgramFiles 'KillerSense'
$source = Join-Path $PublishDir 'Services'
$target = Join-Path $dest 'Services'
$services = @(
  @{ Name = 'KillerSenseWatchdog'; Exe = 'KillerSense.Watchdog.exe'; Display = 'killer-sense watchdog'; Desc = 'Devolve as fans ao Auto do EC se o servico killer-sense parar com fan em manual.' },
  @{ Name = 'KillerSense'; Exe = 'KillerSense.Service.exe'; Display = 'killer-sense'; Desc = 'Controle de fans do Acer Predator (curvas, failsafe).' }
)

foreach ($s in $services) {
  if (Get-Service $s.Name -ErrorAction SilentlyContinue) { throw "O servico $($s.Name) ja existe. Use update-services.ps1 ou rode uninstall-services.ps1 antes." }
  if (-not (Test-Path (Join-Path $source $s.Exe))) { throw "Publicacao nao encontrada: $source\$($s.Exe)" }
}

New-Item -ItemType Directory -Force $dest | Out-Null
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $source $target -Recurse
"copiado: $target"
foreach ($s in $services) {
  $bin = '"' + (Join-Path $target $s.Exe) + '"'
  sc.exe create $s.Name binPath= $bin start= auto obj= LocalSystem DisplayName= $s.Display | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "sc create $($s.Name) falhou ($LASTEXITCODE)" }
  sc.exe description $s.Name $s.Desc | Out-Null
  sc.exe failure $s.Name reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
  sc.exe failureflag $s.Name 1 | Out-Null
  "criado: $($s.Name) -> $bin"
}

foreach ($s in $services) {
  Start-Service $s.Name
  "iniciado: $($s.Name) ($((Get-Service $s.Name).Status))"
}
