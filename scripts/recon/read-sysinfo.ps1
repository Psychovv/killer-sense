<#
  killer-sense / Fase 1 / teste A6 - leitura de telemetria via GetGamingSysInfo (WMBH id 5).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Aprovado pelo usuario em 2026-11-05. Efeito colateral conhecido (ACPI SSDD WMBH):
  cada chamada grava EC.PSEE = 1 (o PredatorSense faz o mesmo continuamente).
  Nenhum outro metodo e chamado.
  Entrada UInt32: byte0 = grupo (1 = sensores EC, 2 = BBMS, 0 = versao), byte1 = indice.
  Saida UInt64: byte0 = status, byte1..2 = valor (16 bits, little-endian).
  Uso (admin): read-sysinfo.ps1 -OutFile <caminho.json> [-Samples 3] [-IntervalMs 2000]
#>
param(
  [Parameter(Mandatory)][string]$OutFile,
  [ValidateRange(1, 20)][int]$Samples = 3,
  [ValidateRange(500, 10000)][int]$IntervalMs = 2000
)
$ErrorActionPreference = 'Stop'

# (byte0, byte1, nome do campo EC no ACPI)
$queries = @(
  @(0, 0, 'versao'),
  @(1, 1, 'CTML'), @(1, 2, 'C1FR'), @(1, 3, 'EST1'),
  @(1, 6, 'G1FR'), @(1, 9, 'G2FR'), @(1, 10, 'SKTC'),
  @(2, 0, 'BBMS')
)

$inst = Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction
$samplesOut = for ($s = 1; $s -le $Samples; $s++) {
  $row = [ordered]@{ Time = (Get-Date).ToString('HH:mm:ss.fff') }
  foreach ($q in $queries) {
    $b0, $b1, $name = $q
    $in = [uint32]($b0 -bor ($b1 -shl 8))
    try {
      $r = Invoke-CimMethod -InputObject $inst -MethodName 'GetGamingSysInfo' -Arguments @{ gmInput = $in }
      $v = [uint64]$r.gmOutput
      $status = $v -band 0xFF
      $value = ($v -shr 8) -band 0xFFFF
      # Grupo 1: valor nos bytes 1..2. Grupos 0 e 2: o ACPI usa bytes 3..5 -> guardar bruto.
      $row[$name] = if ($status -ne 0) { ('status={0} raw=0x{1:X}' -f $status, $v) }
                    elseif ($b0 -eq 1) { $value }
                    else { ('raw=0x{0:X16}' -f $v) }
    } catch {
      $row[$name] = "ERRO: $($_.Exception.Message)"
    }
  }
  [pscustomobject]$row
  if ($s -lt $Samples) { Start-Sleep -Milliseconds $IntervalMs }
}
[pscustomobject]@{ Time = (Get-Date).ToString('s'); Samples = $samplesOut } |
  ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $OutFile
