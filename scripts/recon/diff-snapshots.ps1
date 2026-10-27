# Compara dois snapshots JSON de snapshot-get.ps1 e mostra so o que mudou (somente leitura).
param([Parameter(Mandatory)][string]$Before, [Parameter(Mandatory)][string]$After)
$ErrorActionPreference = 'Stop'
foreach ($f in $Before, $After) { if (-not (Test-Path $f)) { throw "snapshot nao encontrado: $f" } }
$a = (Get-Content $Before -Raw | ConvertFrom-Json).Results
$b = (Get-Content $After -Raw | ConvertFrom-Json).Results
for ($i = 0; $i -lt $a.Count; $i++) {
  $x = $a[$i]; $y = $b[$i]
  $mark = if ("$($x.Output)|$($x.Return)" -ne "$($y.Output)|$($y.Return)") { '*' } else { ' ' }
  '{0} {1,-21} {2,-5} {3}  ->  {4}' -f $mark, $x.Method, $x.Input, $x.Output, $y.Output
}
