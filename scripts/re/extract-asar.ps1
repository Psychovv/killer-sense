<#
  killer-sense - extrai arquivos de TEXTO de um app.asar (Electron) para leitura local.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Formato asar: [u32 4][u32 headerSize][u32 payload][u32 jsonLen][json...]; dados a partir de 8+headerSize.
  So le o arquivo de origem. Saida deve ficar em re/ (ignorado pelo git).
  Uso: extract-asar.ps1 -Asar <app.asar> -OutDir <pasta> [-IncludeNodeModules]
#>
param(
  [Parameter(Mandatory)][string]$Asar,
  [Parameter(Mandatory)][string]$OutDir,
  [switch]$IncludeNodeModules
)
$ErrorActionPreference = 'Stop'
$textExt = '.js', '.mjs', '.cjs', '.json', '.html', '.htm', '.css', '.map', '.txt', '.md', '.ts'

$fs = [IO.File]::OpenRead($Asar)
try {
  $br = New-Object IO.BinaryReader($fs)
  [void]$br.ReadUInt32(); $headerSize = $br.ReadUInt32(); [void]$br.ReadUInt32(); $jsonLen = $br.ReadUInt32()
  $json = [Text.Encoding]::UTF8.GetString($br.ReadBytes($jsonLen))
  $base = 8 + $headerSize
  Add-Type -AssemblyName System.Web.Extensions
  $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
  $ser.MaxJsonLength = [int]::MaxValue
  $root = $ser.DeserializeObject($json)

  $count = 0; $skipped = 0
  $stack = New-Object System.Collections.Stack
  $stack.Push(@('', $root))
  while ($stack.Count -gt 0) {
    $path, $node = $stack.Pop()
    foreach ($kv in $node['files'].GetEnumerator()) {
      $rel = if ($path) { "$path/$($kv.Key)" } else { $kv.Key }
      $e = $kv.Value
      if ($e.ContainsKey('files')) {
        if (-not $IncludeNodeModules -and $kv.Key -eq 'node_modules') { continue }
        $stack.Push(@($rel, $e)); continue
      }
      if ($e.ContainsKey('unpacked') -and $e['unpacked']) { $skipped++; continue }
      if ($textExt -notcontains [IO.Path]::GetExtension($rel).ToLower()) { $skipped++; continue }
      $dst = Join-Path $OutDir ($rel -replace '/', '\')
      $full = [IO.Path]::GetFullPath($dst)
      if (-not $full.StartsWith([IO.Path]::GetFullPath($OutDir))) { throw "caminho fora da saida: $rel" }
      New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
      $fs.Position = $base + [int64]$e['offset']
      [IO.File]::WriteAllBytes($full, $br.ReadBytes([int]$e['size']))
      $count++
    }
  }
  "extraidos=$count ignorados=$skipped"
} finally { $fs.Close() }
