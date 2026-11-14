# Genera src/MiniMixer/app.ico (16, 24, 32, 48, 64, 256 px; entradas PNG dentro del ICO).
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fondo redondeado oscuro
    $r = [Math]::Max(2, [int]($s * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1B, 0x23, 0x28))), $path)

    # Tres faders verdes a distintas alturas
    $green = [System.Drawing.Color]::FromArgb(255, 0x79, 0xDB, 0xA6)
    $track = [System.Drawing.Color]::FromArgb(255, 0x3A, 0x4C, 0x57)
    $levels = @(0.55, 0.30, 0.70)
    $barW = [Math]::Max(2.0, $s * 0.13)
    for ($i = 0; $i -lt 3; $i++) {
        $cx = $s * (0.27 + $i * 0.23)
        $top = $s * 0.18; $bottom = $s * 0.82
        $penT = New-Object System.Drawing.Pen $track, ([Math]::Max(1.0, $barW * 0.45))
        $penT.StartCap = 'Round'; $penT.EndCap = 'Round'
        $g.DrawLine($penT, [float]$cx, [float]$top, [float]$cx, [float]$bottom)
        $knobY = $top + ($bottom - $top) * $levels[$i]
        $penF = New-Object System.Drawing.Pen $green, ([Math]::Max(1.0, $barW * 0.45))
        $penF.StartCap = 'Round'; $penF.EndCap = 'Round'
        $g.DrawLine($penF, [float]$cx, [float]$knobY, [float]$cx, [float]$bottom)
        $k = $barW * 1.25
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $green), [float]($cx - $k / 2), [float]($knobY - $k / 2), [float]$k, [float]$k)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        # 256 px: PNG comprimido (formato estándar desde Vista)
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Tamaños pequeños: DIB clásico 32 bpp (BITMAPINFOHEADER + BGRA de abajo arriba + máscara AND)
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $bw.Write([uint32]($s * $s * 4)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) { , (New-IconPng $s) }

$out = Join-Path $PSScriptRoot '..\src\MiniMixer\app.ico'
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
"Icono creado: $((Resolve-Path $out).Path)"
