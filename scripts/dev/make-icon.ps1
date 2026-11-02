<#
  killer-sense - gera o icone (.ico com imagens PNG 16/24/32/48/256) no estilo Grafite.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)
  Uso: make-icon.ps1 -OutFile <caminho.ico>
#>
param([Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Png([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $r = [Math]::Max(2, [int]($size * 0.28))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2; $m = $size - 1
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($m - $d, 0, $d, $d, 270, 90)
  $path.AddArc($m - $d, $m - $d, $d, $d, 0, 90); $path.AddArc(0, $m - $d, $d, $d, 90, 90); $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x8B, 0x7B, 0xFF))), $path)
  $w = [Math]::Max(1.5, $size * 0.09)
  $inset = $size * 0.27
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x1C, 0x1C, 0x20)), $w
  $g.DrawEllipse($pen, [single]$inset, [single]$inset, [single]($size - 2 * $inset), [single]($size - 2 * $inset))
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$pngs = $sizes | ForEach-Object { ,(New-Png $_) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $pngs[$i].Length
  $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
"icone gerado: $OutFile ($($out.Length) bytes)"
