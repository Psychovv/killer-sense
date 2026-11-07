<#
  killer-sense - gera o icone (.ico com PNGs de 16 a 256) a partir da arte da marca.
  (Sem acentos de proposito: o PowerShell 5.1 le UTF-8 sem BOM como ANSI.)

  A arte (docs/marca/icone-original.webp) tem o fundo xadrez desenhado, nao transparente:
  recortamos o quadrado do icone e aplicamos uma mascara arredondada com borda suave.
  Uso: make-icon.ps1 -OutFile <caminho.ico> [-Source <imagem>]
#>
param(
  [Parameter(Mandatory)][string]$OutFile,
  [string]$Source,
  # Quadrado do icone dentro da arte de 1024 px e o raio do canto (medidos na arte).
  [int]$TileX = 102, [int]$TileY = 102, [int]$TileSize = 820, [double]$Radius = 143
)
$ErrorActionPreference = 'Stop'
# No PowerShell 5.1 o $PSScriptRoot ainda vem vazio nos valores padrao do param().
if (-not $Source) { $Source = Join-Path $PSScriptRoot '..\..\docs\marca\icone-original.webp' }
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing

# WebP via WIC (codec do Windows) -> PNG em memoria -> System.Drawing.
$fs = [IO.File]::OpenRead((Resolve-Path $Source))
$frame = [Windows.Media.Imaging.BitmapDecoder]::Create($fs, 'PreservePixelFormat', 'OnLoad').Frames[0]
$fs.Close()
$enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($frame))
$srcMs = New-Object IO.MemoryStream; $enc.Save($srcMs); $srcMs.Position = 0
$art = [System.Drawing.Bitmap]::FromStream($srcMs)

function New-Png([int]$size) {
  # 1) recorta e reduz o quadrado do icone. Nos tamanhos da bandeja (ate 32 px) a moldura vira ruido
  #    e o K cinza some no fundo preto: aproxima um pouco e sobe o contraste.
  $small = $size -le 32
  $inset = if ($small) { [int]($TileSize * 0.07) } else { 0 }
  $attr = New-Object System.Drawing.Imaging.ImageAttributes
  if ($small) {
    $k = [single]1.5; $o = [single]-0.06
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix00 = $k; $cm.Matrix11 = $k; $cm.Matrix22 = $k; $cm.Matrix33 = 1
    $cm.Matrix40 = $o; $cm.Matrix41 = $o; $cm.Matrix42 = $o; $cm.Matrix44 = 1
    $attr.SetColorMatrix($cm)
  }
  $tile = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($tile)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.DrawImage($art, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $TileX + $inset, $TileY + $inset, $TileSize - 2 * $inset, $TileSize - 2 * $inset, [System.Drawing.GraphicsUnit]::Pixel, $attr)
  $g.Dispose()

  # 2) pinta o recorte dentro de um retangulo arredondado (o xadrez fica de fora)
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $d = [single]([Math]::Max(2.0, $Radius * $size / $TileSize) * 2)
  $m = [single]$size
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($m - $d, 0, $d, $d, 270, 90)
  $path.AddArc($m - $d, $m - $d, $d, $d, 0, 90); $path.AddArc(0, $m - $d, $d, $d, 90, 90); $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.TextureBrush $tile), $path)
  $g.Dispose(); $tile.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  return ,$ms.ToArray()
}

# 16/20/24/32 cobrem a bandeja em 100-200% de escala; o resto, janela, barra e Explorer.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = $sizes | ForEach-Object { ,(New-Png $_) }
$art.Dispose()
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
