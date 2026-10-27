<#
  killer-sense / Fase 1 / passo 5 - snapshot SOMENTE LEITURA.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Chama apenas metodos Get* de AcerGamingFunction cujo codigo ACPI (SSDD, WMBH) foi
  verificado como leitura pura do EC, sem efeitos colaterais no EC e sem SMI.
  Lista fixa: nenhum metodo Set*, nenhuma outra classe.
  Excluidos de proposito:
    - GetGamingSysInfo (5): escreve EC.PSEE = 1 a cada chamada.
    - GetGamingProfile (3), GetGamingFanBehavior (15), GetGamingProfileSetting (9),
      GetCPUOverclockingProfile (25), MiscSetting b0=2/6/8/9: viram SMI (opaco).
  Uso (admin): snapshot-get.ps1 -OutFile <caminho.json> [-Label <texto>]
#>
param(
  [Parameter(Mandatory)][string]$OutFile,
  [string]$Label = ''
)
$ErrorActionPreference = 'Stop'

# (Metodo, gmInput, descricao) - gmInput e UInt32; byte0 = LSB.
$calls = @(
  @('GetGamingFanSpeed',      0x01, 'fan CPU % (EC.CPUF)'),
  @('GetGamingFanSpeed',      0x04, 'fan GPU % (EC.GPUF)'),
  @('GetGamingFanSpeed',      0x05, 'fan GPU2 % (EC.GPU2)'),
  @('GetGamingFanTable',      $null,'EC.FTBL'),
  @('GetGamingMiscSetting',   0x01, 'EC.TKST'),
  @('GetGamingMiscSetting',   0x0A, 'constante 0x73'),
  @('GetGamingMiscSetting',   0x0B, 'modo de operacao (EC.OPMS)'),
  @('GetGamingMiscSetting',   0x0F, 'EC.FMKY (1=on, 2=off)'),
  @('GetGamingLED',           0x01, 'TKST & 3'),
  @('GetGamingLED',           0x08, 'EC.KBST'),
  @('GetGamingLED',           0x10, 'EC.LBST'),
  @('GetGamingRgbKb',         0x01, 'zona 1 RGB'),
  @('GetGamingRgbKb',         0x02, 'zona 2 RGB'),
  @('GetGamingRgbKb',         0x04, 'zona 3 RGB'),
  @('GetGamingRgbKb',         0x08, 'zona 4 RGB'),
  @('GetGamingKBBacklight',   0x01, 'teclado: efeito/brilho/vel/cor'),
  @('GetGamingKBBacklight',   0x02, 'light bar: efeito/brilho/vel/cor'),
  @('GetGamingLEDBehavior',   0x01, 'EC.LBET'),
  @('GetGamingLEDColor',      0x01, 'light bar LBLR/G/B/T/F')
)
$allowed = 'GetGamingFanSpeed','GetGamingFanTable','GetGamingMiscSetting','GetGamingLED',
           'GetGamingRgbKb','GetGamingKBBacklight','GetGamingLEDBehavior','GetGamingLEDColor'

function Format-Out($v) {
  if ($null -eq $v) { return $null }
  if ($v -is [array]) { return (($v | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
  $b = [BitConverter]::GetBytes([uint64]$v)
  return ('0x{0:X16}  bytes(LSB..)= {1}' -f [uint64]$v, (($b | ForEach-Object { '{0:X2}' -f $_ }) -join ' '))
}

$inst = Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction
$results = foreach ($c in $calls) {
  $m, $in, $desc = $c
  if ($m -notin $allowed -or $m -notlike 'Get*') { throw "bloqueado: $m" }
  $inText = if ($null -ne $in) { '0x{0:X2}' -f $in } else { '-' }
  $cimArgs = @{}
  if ($null -ne $in) { $cimArgs.gmInput = [uint32]$in }
  try {
    $r = Invoke-CimMethod -InputObject $inst -MethodName $m -Arguments $cimArgs
    [pscustomobject]@{ Method = $m; Input = $inText; Desc = $desc
                       Output = Format-Out $r.gmOutput; Return = $r.gmReturn; Error = $null }
  } catch {
    [pscustomobject]@{ Method = $m; Input = $inText; Desc = $desc; Output = $null; Return = $null; Error = $_.Exception.Message }
  }
}
[pscustomobject]@{ Time = (Get-Date).ToString('s'); Label = $Label; Results = $results } |
  ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $OutFile
