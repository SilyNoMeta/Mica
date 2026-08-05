param(
    [string]$OutputIco = (Join-Path $PSScriptRoot '..\Mica.Builder\Assets\AppIcon.ico'),
    [string]$OutputPng = (Join-Path $PSScriptRoot 'mica-icon.png')
)

# Draws Mica's own mark: a rounded tile with translucent layered sheets, after the
# mineral the product is named for. Run with Windows PowerShell or PowerShell 7.
#   powershell -ExecutionPolicy Bypass -File tools\Generate-MicaIcon.ps1

Add-Type -AssemblyName System.Drawing

$size = 512
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $d = $radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Mark([int]$w) {
    $bmp = New-Object System.Drawing.Bitmap($w, $w, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $s = $w / 512.0
    $tile = New-Object System.Drawing.RectangleF(($w * 0.055), ($w * 0.055), ($w * 0.89), ($w * 0.89))
    $path = New-RoundedPath $tile ([float]($w * 0.22))

    # Base: the deep, slightly violet blue of a mica flake seen edge on.
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($w, $w)),
        [System.Drawing.Color]::FromArgb(255, 47, 54, 128),
        [System.Drawing.Color]::FromArgb(255, 108, 74, 190))
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # Three stacked sheets, each thinner and brighter: the layering mica is known for.
    $g.SetClip($path)
    $sheets = @(
        @{ Offset = 0.30; Thickness = 0.035; Alpha = 55 },
        @{ Offset = 0.45; Thickness = 0.050; Alpha = 90 },
        @{ Offset = 0.63; Thickness = 0.075; Alpha = 150 }
    )

    foreach ($sheet in $sheets) {
        $y = $w * $sheet.Offset
        $t = $w * $sheet.Thickness
        $rise = $w * 0.17   # the sheets tilt, as cleaved flakes do
        $pts = @(
            (New-Object System.Drawing.PointF(0, ($y + $rise))),
            (New-Object System.Drawing.PointF($w, $y)),
            (New-Object System.Drawing.PointF($w, ($y + $t))),
            (New-Object System.Drawing.PointF(0, ($y + $rise + $t)))
        )
        $fill = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb($sheet.Alpha, 255, 255, 255))
        $g.FillPolygon($fill, $pts)
        $fill.Dispose()
    }

    $g.ResetClip()

    # A hairline highlight along the top edge keeps it from looking flat.
    $pen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb(70, 255, 255, 255), [float](2 * $s))
    $g.DrawPath($pen, $path)
    $pen.Dispose()

    $path.Dispose()
    $g.Dispose()
    return $bmp
}

function Encode-Dib([System.Drawing.Bitmap]$img) {
    $w = $img.Width; $h = $img.Height
    $rowBytes = $w * 4
    $xor = $rowBytes * $h
    $maskRow = [int][Math]::Ceiling($w / 32.0) * 4
    $mask = $maskRow * $h

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32](2 * $h))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($xor + $mask))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $img.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buf = New-Object byte[] $xor
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $xor)
    $img.UnlockBits($data)

    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($buf, ($y * $rowBytes), $rowBytes) }
    $bw.Write((New-Object byte[] $mask))
    $bw.Flush()
    return $ms.ToArray()
}

$master = New-Mark $size
$master.Save($OutputPng, [System.Drawing.Imaging.ImageFormat]::Png)

$payloads = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $img = New-Mark $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [void]$payloads.Add($ms.ToArray())
        $ms.Dispose()
    }
    else {
        [void]$payloads.Add((Encode-Dib $img))
    }
    $img.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$payloads[$i].Length); $bw.Write([uint32]$offset)
    $offset += $payloads[$i].Length
}
foreach ($p in $payloads) { $bw.Write([byte[]]$p) }
$bw.Flush()

$resolved = [System.IO.Path]::GetFullPath($OutputIco)
[System.IO.File]::WriteAllBytes($resolved, $ms.ToArray())
$ms.Dispose()
$master.Dispose()

Write-Host "wrote $resolved and $OutputPng"
