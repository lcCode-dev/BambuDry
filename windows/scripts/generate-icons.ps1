# Regenerates the BambuDry tray PNGs and the app.ico bundle.
# Run from the repo root:
#     powershell -ExecutionPolicy Bypass -File windows\scripts\generate-icons.ps1
#
# - app.ico is built from the macOS app-icon PNGs in
#   macos/BambuDryApp/Assets.xcassets/AppIcon.appiconset/ so both platforms
#   ship identical branding. Multi-image DIB-format ICO (16/32/64/128/256) —
#   PNG-payload ICOs are rejected by csc when embedded as Win32 resources.
#
# - Tray icons stay programmatic: simple droplet (idle/warm/offline) + flame
#   (drying) shapes drawn with System.Drawing Beziers so they read crisply
#   at 16x16 in the system tray. The macOS app generates its tray icons
#   from SF Symbols at runtime; on Windows we ship pre-rasterised PNGs.

Add-Type -AssemblyName System.Drawing

$root     = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$assets   = Join-Path $root 'src\BambuDry.App\Assets'
$macIcons = Join-Path $repoRoot 'macos\BambuDryApp\Assets.xcassets\AppIcon.appiconset'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

# ---------------------------------------------------------------------------
#  Tray icons (programmatic)
# ---------------------------------------------------------------------------

function New-Bitmap([int]$size) { New-Object System.Drawing.Bitmap $size, $size }

function New-Graphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return $g
}

function Add-Droplet($g, [int]$size, [System.Drawing.Color]$fill) {
    $cx     = $size / 2.0
    $top    = $size * 0.12; $bottom = $size * 0.92
    $halfW  = $size * 0.30
    $left   = $cx - $halfW; $right = $cx + $halfW
    $upper  = $size * 0.45; $lower = $size * 0.78
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier($cx, $top,    $left,  $upper, $left,  $lower, $cx, $bottom)
    $path.AddBezier($cx, $bottom, $right, $lower, $right, $upper, $cx, $top)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush $fill
    $g.FillPath($brush, $path); $brush.Dispose(); $path.Dispose()
}

function Add-Flame($g, [int]$size) {
    $cx = $size / 2.0
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddBezier($cx,           $size*0.10, $size*0.20, $size*0.40, $size*0.18, $size*0.72, $size*0.36, $size*0.93)
    $outer.AddBezier($size*0.36,    $size*0.93, $size*0.50, $size*0.78, $size*0.42, $size*0.78, $size*0.55, $size*0.95)
    $outer.AddBezier($size*0.55,    $size*0.95, $size*0.85, $size*0.85, $size*0.83, $size*0.55, $size*0.62, $size*0.30)
    $outer.AddBezier($size*0.62,    $size*0.30, $size*0.55, $size*0.20, $size*0.65, $size*0.18, $cx,         $size*0.10)
    $outer.CloseFigure()
    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 231, 76, 60))
    $g.FillPath($red, $outer); $red.Dispose(); $outer.Dispose()

    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddBezier($cx,         $size*0.32, $size*0.36, $size*0.50, $size*0.34, $size*0.72, $size*0.48, $size*0.88)
    $inner.AddBezier($size*0.48,  $size*0.88, $size*0.70, $size*0.78, $size*0.66, $size*0.55, $cx,        $size*0.32)
    $inner.CloseFigure()
    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 200, 80))
    $g.FillPath($amber, $inner); $amber.Dispose(); $inner.Dispose()
}

function Add-Slash($g, [int]$size, [System.Drawing.Color]$slashColor) {
    $stroke = [Math]::Max(2.0, $size * 0.10)
    $pen = New-Object System.Drawing.Pen $slashColor, $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [single]($size*0.18), [single]($size*0.82), [single]($size*0.82), [single]($size*0.18))
    $pen.Dispose()
}

function Make-TrayIcon([string]$kind) {
    $bmp = New-Bitmap 32; $g = New-Graphics $bmp
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
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose(); $g.Dispose()
    Write-Host "  $out"
}

'idle','warm','drying','offline' | ForEach-Object { Make-TrayIcon $_ }

# ---------------------------------------------------------------------------
#  app.ico (built from macOS source PNGs)
# ---------------------------------------------------------------------------

function Convert-BitmapToDibPayload($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER (40 bytes)
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1);  $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]0);  $bw.Write([int32]0);   $bw.Write([int32]0)
    $bw.Write([uint32]0);  $bw.Write([uint32]0)
    # XOR mask: 32bpp BGRA, bottom-up
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride; $rowBytes = $w * 4
        $buf = New-Object byte[] $stride
        for ($y = $h - 1; $y -ge 0; $y--) {
            $ptr = [IntPtr]::Add($data.Scan0, $y * $stride)
            [System.Runtime.InteropServices.Marshal]::Copy($ptr, $buf, 0, $stride)
            $bw.Write($buf, 0, $rowBytes)
        }
    } finally { $bmp.UnlockBits($data) }
    # AND mask (1bpp transparency, all-zero — alpha lives in BGRA already)
    $maskRow = (([int][Math]::Ceiling($w / 8.0)) + 3) -band -bnot 3
    $bw.Write((New-Object byte[] ($maskRow * $h)))
    return $ms.ToArray()
}

function Save-Ico([string]$path, [byte[][]]$dibs, [int[]]$sizes) {
    $fs = [System.IO.File]::Create($path)
    try {
        $bw = New-Object System.IO.BinaryWriter $fs
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$dibs.Count)
        $offset = 6 + 16 * $dibs.Count
        for ($i = 0; $i -lt $dibs.Count; $i++) {
            $w = $sizes[$i]; $h = $sizes[$i]
            if ($w -ge 256) { $w = 0 }; if ($h -ge 256) { $h = 0 }
            $bw.Write([byte]$w); $bw.Write([byte]$h); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$dibs[$i].Length); $bw.Write([uint32]$offset)
            $offset += $dibs[$i].Length
        }
        foreach ($d in $dibs) { $bw.Write($d) }
    } finally { $fs.Close() }
}

$sizes = 16, 32, 64, 128, 256
$dibs = $sizes | ForEach-Object {
    $src = Join-Path $macIcons "icon-$_.png"
    if (-not (Test-Path $src)) { throw "Missing macOS source icon: $src" }
    $bmp = [System.Drawing.Bitmap]::FromFile($src)
    $payload = Convert-BitmapToDibPayload $bmp
    $bmp.Dispose()
    return ,$payload
}
$icoPath = Join-Path $assets 'app.ico'
Save-Ico $icoPath $dibs $sizes
Write-Host "  $icoPath ($((Get-Item $icoPath).Length) bytes; sizes $($sizes -join ', '))"
