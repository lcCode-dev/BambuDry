# Regenerates the BambuDry tray PNGs and the app.ico bundle.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File windows\scripts\generate-icons.ps1
#
# All artwork is plain System.Drawing — humidity-droplet motif (idle/warm/offline)
# plus a flame for the "drying" state. The droplet & flame are deliberately
# simple geometry so they read at 16×16 in the system tray.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\BambuDry.App\Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

function New-Bitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    return $bmp
}

function New-Graphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return $g
}

function Add-Droplet($g, [int]$size, [System.Drawing.Color]$fill) {
    $cx = $size / 2.0
    $top = $size * 0.12
    $bottom = $size * 0.92
    $halfW = $size * 0.30
    $left = $cx - $halfW
    $right = $cx + $halfW
    $upper = $size * 0.45
    $lower = $size * 0.78

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier($cx, $top, $left, $upper, $left, $lower, $cx, $bottom)
    $path.AddBezier($cx, $bottom, $right, $lower, $right, $upper, $cx, $top)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush $fill
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

function Add-Flame($g, [int]$size) {
    $cx = $size / 2.0
    # outer red flame
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddBezier($cx, $size*0.10, $size*0.20, $size*0.40, $size*0.18, $size*0.72, $size*0.36, $size*0.93)
    $outer.AddBezier($size*0.36, $size*0.93, $size*0.50, $size*0.78, $size*0.42, $size*0.78, $size*0.55, $size*0.95)
    $outer.AddBezier($size*0.55, $size*0.95, $size*0.85, $size*0.85, $size*0.83, $size*0.55, $size*0.62, $size*0.30)
    $outer.AddBezier($size*0.62, $size*0.30, $size*0.55, $size*0.20, $size*0.65, $size*0.18, $cx, $size*0.10)
    $outer.CloseFigure()

    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 231, 76, 60))
    $g.FillPath($red, $outer)
    $red.Dispose()
    $outer.Dispose()

    # inner amber flame
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddBezier($cx, $size*0.32, $size*0.36, $size*0.50, $size*0.34, $size*0.72, $size*0.48, $size*0.88)
    $inner.AddBezier($size*0.48, $size*0.88, $size*0.70, $size*0.78, $size*0.66, $size*0.55, $cx, $size*0.32)
    $inner.CloseFigure()
    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 200, 80))
    $g.FillPath($amber, $inner)
    $amber.Dispose()
    $inner.Dispose()
}

function Add-Slash($g, [int]$size, [System.Drawing.Color]$slashColor) {
    $stroke = [Math]::Max(2.0, $size * 0.10)
    $pen = New-Object System.Drawing.Pen $slashColor, $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [single]($size*0.18), [single]($size*0.82), [single]($size*0.82), [single]($size*0.18))
    $pen.Dispose()
}

function Save-Png($bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# --- tray icons ----------------------------------------------------------

function Make-TrayIcon([string]$kind) {
    $bmp = New-Bitmap 32
    $g   = New-Graphics $bmp
    switch ($kind) {
        'idle'    { Add-Droplet $g 32 ([System.Drawing.Color]::FromArgb(255, 160, 168, 180)) }
        'warm'    { Add-Droplet $g 32 ([System.Drawing.Color]::FromArgb(255, 255, 170, 40)) }
        'drying'  { Add-Flame   $g 32 }
        'offline' {
            Add-Droplet $g 32 ([System.Drawing.Color]::FromArgb(255, 100, 105, 115))
            Add-Slash   $g 32 ([System.Drawing.Color]::FromArgb(235, 235, 238, 245))
        }
    }
    $out = Join-Path $assets "tray-$kind.png"
    Save-Png $bmp $out
    $bmp.Dispose(); $g.Dispose()
    Write-Host "  wrote $out"
}

'idle','warm','drying','offline' | ForEach-Object { Make-TrayIcon $_ }

# --- app.ico (multi-resolution) -------------------------------------------

function Make-AppPng([int]$size) {
    $bmp = New-Bitmap $size
    $g   = New-Graphics $bmp

    # Rounded-square dark backplate so the droplet reads on light themes too.
    $bgRect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $bg     = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r      = [int]([Math]::Max(2, $size * 0.18))
    $bg.AddArc($bgRect.X, $bgRect.Y, $r * 2, $r * 2, 180, 90)
    $bg.AddArc($bgRect.Right - $r * 2, $bgRect.Y, $r * 2, $r * 2, 270, 90)
    $bg.AddArc($bgRect.Right - $r * 2, $bgRect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $bg.AddArc($bgRect.X, $bgRect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $bg.CloseFigure()
    $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 22, 22, 27))
    $g.FillPath($bgBrush, $bg)
    $bgBrush.Dispose(); $bg.Dispose()

    # Cyan droplet — the primary BambuDry mark.
    Add-Droplet $g $size ([System.Drawing.Color]::FromArgb(255, 90, 180, 220))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose(); $g.Dispose()
    return $ms.ToArray()
}

function Save-Ico([string]$path, [byte[][]]$pngs, [int[]]$sizes) {
    $fs = [System.IO.File]::Create($path)
    try {
        $bw = New-Object System.IO.BinaryWriter $fs
        # ICONDIR
        $bw.Write([uint16]0)        # reserved
        $bw.Write([uint16]1)        # type = ICO
        $bw.Write([uint16]$pngs.Count)

        # ICONDIRENTRYs
        $offset = 6 + 16 * $pngs.Count
        for ($i = 0; $i -lt $pngs.Count; $i++) {
            $w = if ($sizes[$i] -ge 256) { 0 } else { [byte]$sizes[$i] }
            $h = if ($sizes[$i] -ge 256) { 0 } else { [byte]$sizes[$i] }
            $bw.Write([byte]$w)
            $bw.Write([byte]$h)
            $bw.Write([byte]0)        # palette
            $bw.Write([byte]0)        # reserved
            $bw.Write([uint16]1)      # planes
            $bw.Write([uint16]32)     # bpp
            $bw.Write([uint32]$pngs[$i].Length)
            $bw.Write([uint32]$offset)
            $offset += $pngs[$i].Length
        }
        # image data
        foreach ($p in $pngs) { $bw.Write($p) }
    } finally {
        $fs.Close()
    }
}

$sizes = 16, 32, 48, 64, 128, 256
$pngs  = $sizes | ForEach-Object { Make-AppPng $_ }
$icoPath = Join-Path $assets 'app.ico'
Save-Ico $icoPath $pngs $sizes
Write-Host "  wrote $icoPath ($(($sizes -join ', '))-size bundle)"
