<#
  killer-sense - publica o app e os servicos (NAO precisa de administrador).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Gera em <OutDir>:
    App\       janela + bandeja: autocontido, com trimming, sem .pdb
    Services\  KillerSense.Service.exe e KillerSense.Watchdog.exe dividindo UMA copia do .NET
               (cada um com o seu .deps.json e .runtimeconfig.json)
  Sempre autocontido: o PC so tem o .NET 6 instalado (docs/testes-hardware.md, T11).
  Os servicos sao publicados separados e depois juntados; se um arquivo em comum for diferente entre os
  dois, o script para (nada de misturar versoes).
  Depois: install-services.ps1 / update-services.ps1 e install-app.ps1 com -PublishDir <OutDir>.
  Uso: publish.ps1 [-OutDir <pasta>]   (padrao: publish\ na raiz do repositorio)
#>
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'publish' }
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

function Invoke-Publish([string]$Project, [string]$Target) {
  & $dotnet publish (Join-Path $root "src\$Project\$Project.csproj") -c Release -r win-x64 --self-contained true -o $Target --nologo
  if ($LASTEXITCODE -ne 0) { throw "publish de $Project falhou ($LASTEXITCODE)" }
}

function Get-Size([string]$Dir) {
  $f = Get-ChildItem $Dir -Recurse -File
  '{0:N1} MB em {1} arquivos' -f (($f | Measure-Object Length -Sum).Sum / 1MB), $f.Count
}

$staging = Join-Path $OutDir '_staging'
foreach ($d in 'App', 'Services', 'Service', 'Watchdog', '_staging') {
  $p = Join-Path $OutDir $d
  if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Invoke-Publish 'KillerSense.App' (Join-Path $OutDir 'App')
Invoke-Publish 'KillerSense.Service' (Join-Path $staging 'Service')
Invoke-Publish 'KillerSense.Watchdog' (Join-Path $staging 'Watchdog')

# Junta os dois servicos: todo arquivo que existe nos dois precisa ser identico.
$svcDir = Join-Path $staging 'Service'
$wdDir = Join-Path $staging 'Watchdog'
foreach ($f in Get-ChildItem $wdDir -Recurse -File) {
  $rel = $f.FullName.Substring($wdDir.Length + 1)
  $other = Join-Path $svcDir $rel
  if ((Test-Path $other) -and (Get-FileHash $other).Hash -ne (Get-FileHash $f.FullName).Hash) {
    throw "Arquivo diferente entre o servico e o watchdog: $rel. Nao da para dividir a pasta."
  }
}
$services = Join-Path $OutDir 'Services'
Copy-Item $svcDir $services -Recurse
Copy-Item (Join-Path $wdDir '*') $services -Recurse -Force
Remove-Item $staging -Recurse -Force

"App:      $(Get-Size (Join-Path $OutDir 'App'))"
"Services: $(Get-Size $services)"
