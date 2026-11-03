<#
  killer-sense - instala ou atualiza a interface (janela + bandeja) (rodar como ADMINISTRADOR).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Copia a publicacao do app para "C:\Program Files\KillerSense\App" e cria o atalho "killer-sense" no menu
  Iniciar do usuario atual (%AppData%\Microsoft\Windows\Start Menu\Programs). Nao mexe nos servicos.
  Para desfazer: apagar a pasta App e o atalho (o uninstall-services.ps1 apaga os dois).
  Uso: install-app.ps1 -PublishDir <pasta com App\>
#>
param([Parameter(Mandatory)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PublishDir 'App'
if (-not (Test-Path (Join-Path $source 'KillerSense.exe'))) { throw "Publicacao do app nao encontrada: $source" }
if (Get-Process KillerSense -ErrorAction SilentlyContinue) { throw 'Feche o killer-sense (bandeja > Encerrar) antes de instalar.' }

$target = Join-Path $env:ProgramFiles 'KillerSense\App'
if (Test-Path $target) {
  for ($i = 1; ; $i++) {
    try { Remove-Item $target -Recurse -Force; break }
    catch { if ($i -ge 10) { throw }; "aguardando liberar arquivos em $target ($i)"; Start-Sleep -Seconds 1 }
  }
}
Copy-Item $source $target -Recurse
"copiado: $target"

$exe = Join-Path $target 'KillerSense.exe'
$link = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\killer-sense.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'killer-sense - controle das fans'
$shortcut.Save()
"atalho: $link"
