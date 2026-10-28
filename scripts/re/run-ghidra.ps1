<#
  killer-sense - roda o Ghidra headless sobre uma COPIA de binario em re/bin (somente leitura).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Usa o JDK 25 e o Ghidra em tools/, e uma pasta de configuracoes propria (evita o cache OSGi
  do perfil do usuario). Os scripts .java sao copiados para fora do OneDrive antes de rodar.
  Uso: run-ghidra.ps1 -Binary <nome em re/bin> -Script <arquivo.java> -ScriptArgs <args...> [-Reanalyze]
#>
param(
  [Parameter(Mandatory)][string]$Binary,
  [Parameter(Mandatory)][string]$Script,
  [Parameter(Mandatory)][string[]]$ScriptArgs,
  [switch]$Reanalyze
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
# Pasta de trabalho do Ghidra. Observado: com ela em %APPDATA% ou %LOCALAPPDATA% o Felix falha;
# dentro de %TEMP% funciona. Pode ser trocada pela variavel KS_GHIDRA_WORK.
$work = if ($env:KS_GHIDRA_WORK) { $env:KS_GHIDRA_WORK } else { Join-Path $env:TEMP 'killer-sense-ghidra' }
New-Item -ItemType Directory -Force "$work\home", "$work\scripts" | Out-Null
Copy-Item (Join-Path $PSScriptRoot "ghidra\$Script") "$work\scripts\" -Force
# Ghidra 12.2 + JDK 25: o Felix (OSGi) falha ao reabrir um cache existente
# ("The data file must be inside the data dir"). Comecar sempre com o cache limpo.
$osgi = Join-Path $work 'home\ghidra\ghidra_12.2_PUBLIC\osgi'
if (Test-Path $osgi) { Remove-Item $osgi -Recurse -Force }

$env:APPDATA = "$work\home"
$env:JAVA_HOME = (Get-ChildItem (Join-Path $root 'tools\jdk25') -Directory | Select-Object -First 1).FullName
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"
$gh = Join-Path $root 'tools\ghidra\ghidra_12.2_PUBLIC\support'
$proj = Join-Path $root 're\ghidra-proj'
$name = [IO.Path]::GetFileNameWithoutExtension($Binary)
$exists = Test-Path (Join-Path $proj "$name.rep")

$a = @("`"$proj`"", $name)
if ($Reanalyze -or -not $exists) { $a += @('-import', "`"$(Join-Path $root "re\bin\$Binary")`"", '-overwrite') }
else { $a += @('-process', $Binary, '-noanalysis') }
$a += @('-scriptPath', "`"$work\scripts`"", '-postScript', $Script)
$a += $ScriptArgs | ForEach-Object { if ($_ -match '[\s\\]') { "`"$_`"" } else { $_ } }

$log = Join-Path $root "re\decomp\$name-headless.log"
$p = Start-Process "$gh\analyzeHeadless.bat" -ArgumentList $a -WorkingDirectory $gh -NoNewWindow -Wait -PassThru `
       -RedirectStandardOutput $log -RedirectStandardError "$log.err"
"exit=$($p.ExitCode)"
Get-Content $log, "$log.err" | Select-String -Pattern 'funcoes gravadas|ERROR|Exception|sem funcao' | ForEach-Object Line

