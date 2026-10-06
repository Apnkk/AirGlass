# Renders the AirGlass logo (same geometry as LogoImage in App.xaml) into a multi-size AirGlass.ico
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\AirGlass.ico')
)

Add-Type -AssemblyName System.Drawing

function New-LogoPng([int]$size) {
    $s = $size / 64.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Tile
        $x = 0.75 * $s; $w = 62.5 * $s; $r = 15 * $s; $d = $r * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $x, $d, $d, 180, 90)
        $path.AddArc($x + $w - $d, $x, $d, $d, 270, 90)
        $path.AddArc($x + $w - $d, $x + $w - $d, $d, $d, 0, 90)
        $path.AddArc($x, $x + $w - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.ColorTranslator]::FromHtml('#1E2333'),
            [System.Drawing.ColorTranslator]::FromHtml('#0B0D12'),
            45.0)
        $g.FillPath($fill, $path)
        $edge = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#2E3547'), [single](1.5 * $s))
        $g.DrawPath($edge, $path)

        # Screen outline
        $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#F2F4F8'), [single](3.5 * $s))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pts = @(
            @(25, 43), @(15, 43), @(15, 19), @(49, 19), @(49, 43), @(39, 43)
        ) | ForEach-Object { New-Object System.Drawing.PointF([single]($_[0] * $s), [single]($_[1] * $s)) }
        $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)

        # Accent triangle
        $accent = [System.Drawing.ColorTranslator]::FromHtml('#3D8BFF')
        $tri = @(@(32, 35), @(43, 49), @(21, 49)) |
            ForEach-Object { New-Object System.Drawing.PointF([single]($_[0] * $s), [single]($_[1] * $s)) }
        $brush = New-Object System.Drawing.SolidBrush($accent)
        $g.FillPolygon($brush, [System.Drawing.PointF[]]$tri)
        $triPen = New-Object System.Drawing.Pen($accent, [single](2 * $s))
        $triPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPolygon($triPen, [System.Drawing.PointF[]]$tri)
    }
    finally {
        $g.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { ,(New-LogoPng $sz) }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $bytes = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $images) { $bw.Write($bytes) }
$bw.Flush()

$full = [System.IO.Path]::GetFullPath($Output)
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
Write-Output "wrote $full ($($out.Length) bytes)"