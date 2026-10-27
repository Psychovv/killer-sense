<#
  killer-sense / Fase 1 - PRIMEIRA ESCRITA, aprovada pelo usuario em 2026-11-05.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Faz exatamente UMA escrita: AcerGamingFunction.SetGamingFanBehavior(gmInput = 0x00410009)
    = mascara 0x0009 (CPU bit0 + GPU bit3), modos 0x41 (CPU=1 Auto, GPU=1 Auto).
  Le GetGamingFanBehavior(0x09) antes e depois. Nenhum outro Set*.
  Uso (admin): write-fan-auto.ps1 -OutFile <caminho.json>
#>
param([Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference = 'Stop'

$AUTO_BOTH = [uint64]0x00410009   # unico valor permitido neste script

function Read-Behavior($inst) {
  $r = Invoke-CimMethod -InputObject $inst -MethodName 'GetGamingFanBehavior' -Arguments @{ gmInput = [uint32]0x09 }
  return [uint64]$r.gmOutput
}

$inst = Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction
$log = [ordered]@{ Time = (Get-Date).ToString('s'); Input = '0x{0:X8}' -f $AUTO_BOTH }
try {
  $before = Read-Behavior $inst
  $log.Before = '0x{0:X}' -f $before
  $w = Invoke-CimMethod -InputObject $inst -MethodName 'SetGamingFanBehavior' -Arguments @{ gmInput = $AUTO_BOTH }
  $log.SetOutput = '0x{0:X}' -f [uint64]$w.gmOutput
  Start-Sleep -Milliseconds 500
  $after = Read-Behavior $inst
  $log.After = '0x{0:X}' -f $after
  $log.Error = $null
} catch {
  $log.Error = $_.Exception.Message
}
[pscustomobject]$log | ConvertTo-Json | Set-Content -Encoding utf8 $OutFile
