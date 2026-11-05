<#
.SYNOPSIS
    Regenerates every Market Pulse icon and tile from one drawing.

.DESCRIPTION
    Draws the logo (a pulse line that breaks into a rising market tick on a dark rounded tile)
    with GDI+ and writes the MSIX assets plus the 256 px gallery icon. Run it with Windows
    PowerShell 5.1, which ships System.Drawing:

        powershell -NoProfile -File tools\New-Icons.ps1
#>
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $Root 'MarketTicker\Assets'
$gallery = Join-Path $Root 'gallery\darkace\market-pulse'
New-Item -ItemType Directory -Force $gallery | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the logo into a square of side $s at ($ox, $oy). $plated = $false omits the tile
# (used for the taskbar "unplated" asset, which Windows draws on its own background).
function Draw-Logo([System.Drawing.Graphics]$g, [float]$ox, [float]$oy, [float]$s, [bool]$plated = $true) {
    if ($plated) {
        $tile = New-RoundedRect ($ox + $s * 0.04) ($oy + $s * 0.04) ($s * 0.92) ($s * 0.92) ($s * 0.2)
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($ox, $oy)),
            (New-Object System.Drawing.PointF(($ox + $s), ($oy + $s))),
            [System.Drawing.Color]::FromArgb(255, 17, 24, 39),
            [System.Drawing.Color]::FromArgb(255, 14, 64, 74))
        $g.FillPath($bg, $tile)
    }

    # Pulse: flat, a heartbeat spike, then a climb to the top-right.
    $pts = @(
        @(0.14, 0.62), @(0.30, 0.62), @(0.37, 0.72), @(0.46, 0.36),
        @(0.54, 0.64), @(0.62, 0.52), @(0.72, 0.42), @(0.82, 0.28)
    ) | ForEach-Object { New-Object System.Drawing.PointF(($ox + $s * $_[0]), ($oy + $s * $_[1])) }

    $width = [Math]::Max(1.5, $s * 0.075)
    $glow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 52, 211, 153), ($width * 2.2))
    $glow.LineJoin = 'Round'; $glow.StartCap = 'Round'; $glow.EndCap = 'Round'
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 211, 153), $width)
    $line.LineJoin = 'Round'; $line.StartCap = 'Round'; $line.EndCap = 'Round'
    if ($s -ge 32) { $g.DrawLines($glow, $pts) }
    $g.DrawLines($line, $pts)

    # Live dot at the tip of the climb.
    $r = $s * 0.075
    $tip = $pts[-1]
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 236, 253, 245))), ($tip.X - $r), ($tip.Y - $r), ($r * 2), ($r * 2))
}

function Save-Png([string]$path, [int]$w, [int]$h, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    & $draw $g
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $path ($w x $h)"
}

function Save-Square([string]$path, [int]$size, [bool]$plated = $true) {
    Save-Png $path $size $size { param($g) Draw-Logo $g 0 0 $size $plated }
}

# Logo centred on a transparent wide canvas (splash screen and wide tile).
function Save-Wide([string]$path, [int]$w, [int]$h) {
    Save-Png $path $w $h { param($g) $s = $h * 0.6; Draw-Logo $g (($w - $s) / 2) (($h - $s) / 2) $s }
}

# Full-bleed dark gradient used behind the Store art.
function Fill-Backdrop([System.Drawing.Graphics]$g, [int]$w, [int]$h) {
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($w, $h)),
        [System.Drawing.Color]::FromArgb(255, 11, 17, 30),
        [System.Drawing.Color]::FromArgb(255, 12, 58, 66))
    $g.FillRectangle($bg, 0, 0, $w, $h)
}

function Draw-Text([System.Drawing.Graphics]$g, [string]$text, [float]$size, [string]$style, [System.Drawing.Color]$color, [float]$y, [int]$w) {
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $font = New-Object System.Drawing.Font('Segoe UI', $size, [System.Drawing.FontStyle]::$style, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $g.DrawString($text, $font, (New-Object System.Drawing.SolidBrush($color)), (New-Object System.Drawing.RectangleF(0, $y, $w, ($size * 1.5))), $fmt)
}

Write-Host 'Writing assets...'
Save-Square (Join-Path $assets 'StoreLogo.png') 50
Save-Square (Join-Path $assets 'Square44x44Logo.scale-200.png') 88
Save-Square (Join-Path $assets 'Square44x44Logo.targetsize-24_altform-unplated.png') 24 $false
Save-Square (Join-Path $assets 'Square150x150Logo.scale-200.png') 300
Save-Square (Join-Path $assets 'LockScreenLogo.scale-200.png') 48
Save-Wide (Join-Path $assets 'Wide310x150Logo.scale-200.png') 620 300
Save-Wide (Join-Path $assets 'SplashScreen.scale-200.png') 1240 600
Save-Square (Join-Path $gallery 'icon.png') 256

# Microsoft Store listing art: 1:1 box art (required) and 2:3 poster art (recommended).
$store = Join-Path $Root 'store'
New-Item -ItemType Directory -Force $store | Out-Null
foreach ($s in 1080, 2160) {
    Save-Png (Join-Path $store "BoxArt-$s.png") $s $s {
        param($g)
        Fill-Backdrop $g $s $s
        $logo = $s * 0.78
        Draw-Logo $g (($s - $logo) / 2) (($s - $logo) / 2) $logo $false
    }
}
foreach ($w in 720, 1440) {
    $h = $w * 1.5
    Save-Png (Join-Path $store "PosterArt-$w`x$h.png") $w $h {
        param($g)
        Fill-Backdrop $g $w $h
        $logo = $w * 0.62
        Draw-Logo $g (($w - $logo) / 2) ($h * 0.2) $logo $false
        Draw-Text $g 'Market Pulse' ($w * 0.115) 'Bold' ([System.Drawing.Color]::FromArgb(255, 236, 253, 245)) ($h * 0.6) $w
        Draw-Text $g 'Live prices on your Dock' ($w * 0.055) 'Regular' ([System.Drawing.Color]::FromArgb(255, 110, 231, 183)) ($h * 0.7) $w
        # [char]0xB7 is a middle dot; written as a code so Windows PowerShell 5.1 reads this file correctly.
        Draw-Text $g ((('Stocks', 'Indices', 'FX', 'Crypto')) -join "  $([char]0xB7)  ")($w * 0.045) 'Regular' ([System.Drawing.Color]::FromArgb(200, 203, 213, 225)) ($h * 0.77) $w
    }
}
