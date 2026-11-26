# Builds Focus Volume for Stream Dock and Stream Deck.
#   .\build.ps1            build into dist\
#   .\build.ps1 -Install   also install into whichever host is on this PC and restart it
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$id   = 'com.iakacer.focusvolume'
$dist = Join-Path $root 'dist'
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force "$dist\bin" | Out-Null

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:"$dist\bin\FocusVolume.exe" `
    /r:"$fw\System.Web.Extensions.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "$root\src\FocusVolume.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }

# Speaker icon, drawn at any size
Add-Type -AssemblyName System.Drawing
function New-Icon([string]$path, [int]$size, [bool]$bg) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 72.0
    if ($bg) { $g.Clear([System.Drawing.Color]::FromArgb(36, 40, 48)) }
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pts = @(
        (New-Object System.Drawing.PointF (14*$s), (28*$s)), (New-Object System.Drawing.PointF (24*$s), (28*$s)),
        (New-Object System.Drawing.PointF (38*$s), (16*$s)), (New-Object System.Drawing.PointF (38*$s), (56*$s)),
        (New-Object System.Drawing.PointF (24*$s), (44*$s)), (New-Object System.Drawing.PointF (14*$s), (44*$s)))
    $g.FillPolygon($white, $pts)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (4*$s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 34*$s, 24*$s, 16*$s, 24*$s, -50, 100)
    $g.DrawArc($pen, 34*$s, 14*$s, 28*$s, 44*$s, -50, 100)
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

function New-PluginFolder([string]$host_) {
    $out = "$dist\$host_\$id.sdPlugin"
    New-Item -ItemType Directory -Force "$out\images" | Out-Null
    Copy-Item "$dist\bin\FocusVolume.exe", "$root\plugin\$host_\manifest.json", "$root\plugin\shared\*" $out
    Copy-Item "$root\LICENSE" $out -ErrorAction SilentlyContinue
    foreach ($i in @(@('plugin', 256, $true), @('key', 72, $true), @('action', 20, $false), @('category', 28, $false))) {
        New-Icon "$out\images\$($i[0]).png" $i[1] $i[2]
        New-Icon "$out\images\$($i[0])@2x.png" ($i[1] * 2) $i[2]
    }
    return $out
}

$dock = New-PluginFolder 'streamdock'
$deck = New-PluginFolder 'streamdeck'

# Release packages: a zip for Stream Dock, a .streamDeckPlugin (a zip) for Stream Deck
Compress-Archive -Path $dock -DestinationPath "$dist\FocusVolume-StreamDock.zip"
Compress-Archive -Path $deck -DestinationPath "$dist\deck.zip"
Move-Item "$dist\deck.zip" "$dist\$id.streamDeckPlugin"
Write-Host "Built: $dist\FocusVolume-StreamDock.zip, $dist\$id.streamDeckPlugin"

if ($Install) {
    $targets = @(
        @{ Proc = 'StreamDock'; Src = $dock; Dir = "$env:APPDATA\HotSpot\StreamDock\plugins" },
        @{ Proc = 'StreamDeck'; Src = $deck; Dir = "$env:APPDATA\Elgato\StreamDeck\Plugins" })
    foreach ($t in $targets) {
        if (-not (Test-Path $t.Dir)) { continue }
        $running = Get-Process $t.Proc -ErrorAction SilentlyContinue | Select-Object -First 1
        $exePath = if ($running) { $running.Path } else { $null }
        Get-Process $t.Proc, FocusVolume -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        $dest = Join-Path $t.Dir "$id.sdPlugin"
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        Copy-Item $t.Src $dest -Recurse
        Write-Host "Installed to $dest"
        if ($exePath) { Start-Process $exePath; Write-Host "Restarted $($t.Proc)" }
    }
}
