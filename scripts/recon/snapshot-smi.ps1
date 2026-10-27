<#
  killer-sense / Fase 1 / teste A7 - snapshot SOMENTE LEITURA de Get* que viram SMI.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  O ACPI nao mostra o que o BIOS faz nessas chamadas; por isso a lista e fixa e pequena.
  Nenhum Set*. Entradas desconhecidas devem apenas retornar erro.
  Uso (admin): snapshot-smi.ps1 -OutFile <caminho.json> [-Label <texto>]
#>
param(
  [Parameter(Mandatory)][string]$OutFile,
  [string]$Label = ''
)
$ErrorActionPreference = 'Stop'

function Hex($v) {
  if ($null -eq $v) { return $null }
  if ($v -is [array]) { return (($v | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
  $b = [BitConverter]::GetBytes([uint64]$v)
  return ('0x{0:X16}  bytes(LSB..)= {1}' -f [uint64]$v, (($b | ForEach-Object { '{0:X2}' -f $_ }) -join ' '))
}

# (Classe, Metodo, Argumentos, descricao). Somente Get*.
$calls = @(
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x00 }, 'fan behavior, mascara 0x00'),
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x01 }, 'fan behavior, mascara 0x01 (CPU?)'),
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x04 }, 'fan behavior, mascara 0x04 (GPU?)'),
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x05 }, 'fan behavior, mascara 0x05 (CPU+GPU?)'),
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x08 }, 'fan behavior, mascara 0x08'),
  @('AcerGamingFunction', 'GetGamingFanBehavior', @{ gmInput = [uint32]0x09 }, 'fan behavior, mascara 0x09'),
  @('AcerGamingFunction', 'GetGamingProfile',     @{ gmInput = [uint32]0x00 }, 'perfil, entrada 0'),
  @('AcerGamingFunction', 'GetGamingProfile',     @{ gmInput = [uint32]0x01 }, 'perfil, entrada 1'),
  @('BatteryControl', 'GetBatteryHealthControlStatus',
      @{ uBatteryNo = [byte]1; uFunctionQuery = [byte]1; uReserved = [byte[]](0, 0) }, 'bateria 1, query 1 (uReserved MAX=2 no MOF)')
)
$allowedClasses = 'AcerGamingFunction', 'BatteryControl'

$cache = @{}
$results = foreach ($c in $calls) {
  $cls, $m, $cimArgs, $desc = $c
  if ($cls -notin $allowedClasses -or $m -notlike 'Get*') { throw "bloqueado: $cls.$m" }
  $inText = ($cimArgs.GetEnumerator() | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name, (Hex $_.Value) }) -join '; '
  try {
    if (-not $cache[$cls]) { $cache[$cls] = Get-CimInstance -Namespace root\WMI -ClassName $cls }
    $r = Invoke-CimMethod -InputObject $cache[$cls] -MethodName $m -Arguments $cimArgs
    $outs = [ordered]@{}
    foreach ($p in $r.PSObject.Properties) {
      if ($p.Name -notin 'ReturnValue', 'PSComputerName') { $outs[$p.Name] = Hex $p.Value }
    }
    [pscustomobject]@{ Class = $cls; Method = $m; Input = $inText; Desc = $desc; Output = $outs; Error = $null }
  } catch {
    [pscustomobject]@{ Class = $cls; Method = $m; Input = $inText; Desc = $desc; Output = $null; Error = $_.Exception.Message }
  }
}
[pscustomobject]@{ Time = (Get-Date).ToString('s'); Label = $Label; Results = $results } |
  ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $OutFile
