<#
  Le os sensores do HWiNFO pela memoria compartilhada (somente leitura, sem admin).
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Requer no HWiNFO: Configuracoes > Geral / Interface do usuario > "Suporte de memoria compartilhada"
  (na versao gratis desliga sozinho apos 12 h) e a janela de Sensores aberta.
  Formato: "Global\HWiNFO_SENS_SM2" (cabecalho com assinatura "HWiS", secao de sensores e de leituras).
  Uso:
    read-hwinfo.ps1                  resumo (PL1/PL2, potencia, clocks, temperaturas, limites da GPU)
    read-hwinfo.ps1 -Filter 'Fan'    leituras cujo nome original ou traduzido casa com a expressao
    read-hwinfo.ps1 -Csv saida.csv   todas as leituras em CSV
#>
param([string]$Filter, [string]$Csv)
$ErrorActionPreference = 'Stop'
try {
  $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('Global\HWiNFO_SENS_SM2', [System.IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
} catch {
  throw 'Memoria compartilhada do HWiNFO nao encontrada: ligue o suporte nas configuracoes e abra a janela de Sensores.'
}
$view = $mmf.CreateViewAccessor(0, 0, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
try {
  function U32($o) { $view.ReadUInt32($o) }
  function Str($o, $n) {
    $b = New-Object byte[] $n; [void]$view.ReadArray($o, $b, 0, $n)
    $z = [Array]::IndexOf($b, [byte]0); if ($z -lt 0) { $z = $n }
    [Text.Encoding]::GetEncoding(1252).GetString($b, 0, $z)
  }
  if ((U32 0) -ne 0x53695748) { throw 'Assinatura inesperada: o HWiNFO desligou a memoria compartilhada?' }
  $poll = [DateTimeOffset]::FromUnixTimeSeconds($view.ReadInt64(12)).ToLocalTime()
  $offS = U32 20; $sizeS = U32 24; $numS = U32 28; $offR = U32 32; $sizeR = U32 36; $numR = U32 40
  "HWiNFO SM v$(U32 4).$(U32 8), leitura de $($poll.ToString('HH:mm:ss')), $numS sensores, $numR leituras"
  # Sensor: id, instancia, nome original[128], nome do usuario[128].
  $sensors = for ($i = 0; $i -lt $numS; $i++) { Str ($offS + $i * $sizeS + 136) 128 }
  # Leitura: tipo, indice do sensor, id, rotulo original[128], rotulo do usuario[128], unidade[16], valor, min, max, media.
  $rows = for ($i = 0; $i -lt $numR; $i++) {
    $b = $offR + $i * $sizeR
    [pscustomobject]@{
      Sensor   = $sensors[(U32 ($b + 4))]
      Original = Str ($b + 12) 128
      Rotulo   = Str ($b + 140) 128
      Valor    = [math]::Round($view.ReadDouble($b + 284), 2)
      Unidade  = Str ($b + 268) 16
    }
  }
} finally {
  $view.Dispose(); $mmf.Dispose()
}
if ($Csv) { $rows | Export-Csv $Csv -NoTypeInformation -Encoding UTF8; "gravado: $Csv"; return }
$pattern = if ($Filter) { $Filter } else { '^(CPU Package|CPU Package Power|PL[12] Power Limit.*|GPU Temperature|GPU Power|GPU Power Limit.*|GPU Thermal Limit|GPU Clock|GPU Memory Clock|Core Effective Clocks)$' }
$rows | Where-Object { $_.Original -match $pattern -or $_.Rotulo -match $pattern } |
  Format-Table Sensor, Original, Valor, Unidade -AutoSize | Out-String -Width 200
