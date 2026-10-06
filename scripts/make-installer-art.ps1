# Generates the Inno Setup wizard images from AirGlass.ico and the app palette.
Add-Type -AssemblyName System.Drawing

$root    = Split-Path $PSScriptRoot -Parent
$outDir  = Join-Path $root 'Installer\assets'
$icoPath = Join-Path $root 'AirGlass.ico'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path $icoPath)) { throw "Icon not found: $icoPath" }

function Get-Col([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

$bg     = Get-Col '#0B0C0F'
$text   = Get-Col '#ECEDEF'
$muted  = Get-Col '#8B909C'
$accent = Get-Col '#3D8BFF'

# Reads the largest image of an .ico (PNG-compressed or BMP) into a Bitmap.
function Get-IcoBitmap([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $count = [int][BitConverter]::ToUInt16($bytes, 4)
    $best = $null
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + 16 * $i
        $w = [int]$bytes[$o]
        if ($w -eq 0) { $w = 256 }
        if (($null -eq $best) -or ($w -gt $best.W)) {
            $best = @{
                W    = $w
                Size = [int][BitConverter]::ToUInt32($bytes, $o + 8)
                Off  = [int][BitConverter]::ToUInt32($bytes, $o + 12)
                Dir  = $o
            }
        }
    }
    if ($null -eq $best) { throw "No image found in $path" }

    $data = New-Object byte[] $best.Size
    [Array]::Copy($bytes, $best.Off, $data, 0, $best.Size)
    $isPng = ($data.Length -gt 4 -and $data[0] -eq 0x89 -and $data[1] -eq 0x50 -and $data[2] -eq 0x4E -and $data[3] -eq 0x47)

    if ($isPng) {
        $ms  = New-Object System.IO.MemoryStream(,$data)
        $src = [System.Drawing.Image]::FromStream($ms)
        $bmp = New-Object System.Drawing.Bitmap($src)
        $src.Dispose(); $ms.Dispose()
        return $bmp
    }

    # BMP/DIB entry: wrap it in a single-image .ico so System.Drawing can decode it.
    $single = New-Object byte[] (22 + $best.Size)
    [Array]::Copy($bytes, 0, $single, 0, 4)
    $single[4] = 1; $single[5] = 0
    [Array]::Copy($bytes, $best.Dir, $single, 6, 16)
    [Array]::Copy([BitConverter]::GetBytes([uint32]22), 0, $single, 18, 4)
    [Array]::Copy($data, 0, $single, 22, $best.Size)
    $ms   = New-Object System.IO.MemoryStream(,$single)
    $icon = New-Object System.Drawing.Icon($ms)
    $bmp  = New-Object System.Drawing.Bitmap($icon.ToBitmap())
    $icon.Dispose(); $ms.Dispose()
    return $bmp
}

$iconBmp = Get-IcoBitmap $icoPath

$eAcute = [string][char]0x00E9
$tagline = "iPhone, iPad, Mac`net Android`nsur Windows"

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear($bg)
    return @{ Bmp = $bmp; G = $g }
}

function New-Font([double]$px, [string]$name) {
    New-Object System.Drawing.Font($name, [single]$px, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
}

function Save-Large([int]$w, [int]$h) {
    $c = New-Canvas $w $h
    $g = $c.G
    $s = $w / 164.0

    $iconSize = 72 * $s
    $g.DrawImage($iconBmp, [single](24 * $s), [single](40 * $s), [single]$iconSize, [single]$iconSize)

    $titleFont = New-Font (22 * $s) 'Segoe UI Semibold'
    $bodyFont  = New-Font (11 * $s) 'Segoe UI'
    $smallFont = New-Font (9 * $s) 'Segoe UI'

    $textBrush   = New-Object System.Drawing.SolidBrush($text)
    $mutedBrush  = New-Object System.Drawing.SolidBrush($muted)
    $accentBrush = New-Object System.Drawing.SolidBrush($accent)

    $g.DrawString('AirGlass', $titleFont, $textBrush, [single](24 * $s), [single](128 * $s))
    $g.FillRectangle($accentBrush, [single](24 * $s), [single](164 * $s), [single](28 * $s), [single](3 * $s))
    $g.DrawString($tagline, $bodyFont, $mutedBrush, [single](24 * $s), [single](178 * $s))
    $g.DrawString('Version 1.0.0', $smallFont, $mutedBrush, [single](24 * $s), [single]($h - 34 * $s))

    $path = Join-Path $outDir ("wizard-{0}.bmp" -f $w)
    $c.Bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $c.Bmp.Dispose()
    "wrote $path"
}

function Save-Small([int]$size) {
    $c = New-Canvas $size $size
    $pad = $size * 0.1
    $c.G.DrawImage($iconBmp, [single]$pad, [single]$pad, [single]($size - 2 * $pad), [single]($size - 2 * $pad))
    $path = Join-Path $outDir ("small-{0}.bmp" -f $size)
    $c.Bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $c.G.Dispose(); $c.Bmp.Dispose()
    "wrote $path"
}

Save-Large 164 314
Save-Large 246 459
Save-Large 328 604
Save-Small 55
Save-Small 110

$iconBmp.Dispose()