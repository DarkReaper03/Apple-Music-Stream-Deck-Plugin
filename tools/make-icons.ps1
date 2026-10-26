# Generates every image the plugin uses into <plugin>.sdPlugin\imgs.
#  - keys\*.svg     144x144 key faces (white glyph on the Apple Music red gradient)
#  - actions\*.svg  monochrome glyphs for Stream Deck's action list
#  - category.svg   action list category icon
#  - plugin.png / plugin@2x.png  plugin icon (Stream Deck wants PNG here)
param([Parameter(Mandatory)][string]$OutDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Glyphs drawn on a 144x144 canvas, white.
$speaker = '<path d="M30 58 H46 L68 40 V100 L46 82 H30 Z" fill="#fff" stroke="#fff" stroke-width="6" stroke-linejoin="round"/>'
$glyphs = [ordered]@{
    play     = '<path d="M56 40 L104 72 L56 104 Z" fill="#fff" stroke="#fff" stroke-width="10" stroke-linejoin="round"/>'
    pause    = '<rect x="46" y="40" width="18" height="64" rx="5" fill="#fff"/><rect x="80" y="40" width="18" height="64" rx="5" fill="#fff"/>'
    next     = '<g fill="#fff" stroke="#fff" stroke-width="6" stroke-linejoin="round"><path d="M36 46 L72 72 L36 98 Z"/><path d="M72 46 L108 72 L72 98 Z"/></g>'
    previous = '<g fill="#fff" stroke="#fff" stroke-width="6" stroke-linejoin="round"><path d="M108 46 L72 72 L108 98 Z"/><path d="M72 46 L36 72 L72 98 Z"/></g>'
    volumeup   = "$speaker<path d=""M86 70 H114 M100 56 V84"" stroke=""#fff"" stroke-width=""9"" stroke-linecap=""round""/>"
    volumedown = "$speaker<path d=""M86 70 H114"" stroke=""#fff"" stroke-width=""9"" stroke-linecap=""round""/>"
    sound    = "$speaker<path d=""M82 56 Q92 70 82 84 M94 46 Q110 70 94 94"" fill=""none"" stroke=""#fff"" stroke-width=""7"" stroke-linecap=""round""/>"
    muted    = "$speaker<path d=""M86 58 L110 82 M110 58 L86 82"" stroke=""#fff"" stroke-width=""8"" stroke-linecap=""round""/>"
    note     = '<g fill="#fff"><path d="M58 40 L106 30 L106 44 L58 54 Z"/><rect x="54" y="44" width="8" height="54"/><rect x="102" y="34" width="8" height="54"/><ellipse cx="46" cy="98" rx="14" ry="11" transform="rotate(-20 46 98)"/><ellipse cx="94" cy="88" rx="14" ry="11" transform="rotate(-20 94 88)"/></g>'
}

$red  = @('#FA5C70', '#FA233B')
$gray = @('#636366', '#3A3A3C')

function Write-Svg([string]$path, [string]$body) {
    $svg = "<svg xmlns=""http://www.w3.org/2000/svg"" width=""144"" height=""144"" viewBox=""0 0 144 144"">$body</svg>"
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $svg)  # UTF-8 without BOM
}

function Key-Svg([string]$glyph, [string[]]$colors, [int]$shiftUp = 0) {
    "<defs><linearGradient id=""bg"" x1=""0"" y1=""0"" x2=""0"" y2=""1""><stop offset=""0"" stop-color=""$($colors[0])""/><stop offset=""1"" stop-color=""$($colors[1])""/></linearGradient></defs>" +
    "<rect width=""144"" height=""144"" fill=""url(#bg)""/><g transform=""translate(0 -$shiftUp)"">$glyph</g>"
}

# Key faces. Volume keys sit a little higher to leave room for the "45%" title underneath.
Write-Svg "$OutDir\keys\play.svg"       (Key-Svg $glyphs.play $red)
Write-Svg "$OutDir\keys\pause.svg"      (Key-Svg $glyphs.pause $red)
Write-Svg "$OutDir\keys\next.svg"       (Key-Svg $glyphs.next $red)
Write-Svg "$OutDir\keys\previous.svg"   (Key-Svg $glyphs.previous $red)
Write-Svg "$OutDir\keys\volumeup.svg"   (Key-Svg $glyphs.volumeup $red 12)
Write-Svg "$OutDir\keys\volumedown.svg" (Key-Svg $glyphs.volumedown $red 12)
Write-Svg "$OutDir\keys\sound.svg"      (Key-Svg $glyphs.sound $red)
Write-Svg "$OutDir\keys\muted.svg"      (Key-Svg $glyphs.muted $gray)

# Action list icons: just the glyph on transparent.
Write-Svg "$OutDir\actions\playpause.svg"  $glyphs.play
Write-Svg "$OutDir\actions\next.svg"       $glyphs.next
Write-Svg "$OutDir\actions\previous.svg"   $glyphs.previous
Write-Svg "$OutDir\actions\volumeup.svg"   $glyphs.volumeup
Write-Svg "$OutDir\actions\volumedown.svg" $glyphs.volumedown
Write-Svg "$OutDir\actions\mute.svg"       $glyphs.muted
Write-Svg "$OutDir\category.svg"           $glyphs.note

# Plugin icon: rounded red square with a music note, drawn with GDI+ at 256 and 512 px.
function Write-PluginPng([string]$path, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 144.0
    $g.ScaleTransform($s, $s)

    $r = 32; $box = New-Object System.Drawing.Drawing2D.GraphicsPath
    $box.AddArc(0, 0, $r, $r, 180, 90); $box.AddArc(144 - $r, 0, $r, $r, 270, 90)
    $box.AddArc(144 - $r, 144 - $r, $r, $r, 0, 90); $box.AddArc(0, 144 - $r, $r, $r, 90, 90); $box.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, 144), ([System.Drawing.ColorTranslator]::FromHtml($red[0])), ([System.Drawing.ColorTranslator]::FromHtml($red[1]))
    $g.FillPath($bg, $box)

    $white = [System.Drawing.Brushes]::White
    $g.FillPolygon($white, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 58, 40), (New-Object System.Drawing.PointF 106, 30), (New-Object System.Drawing.PointF 106, 44), (New-Object System.Drawing.PointF 58, 54)))
    $g.FillRectangle($white, 54, 44, 8, 54)
    $g.FillRectangle($white, 102, 34, 8, 54)
    foreach ($head in @(@(46, 98), @(94, 88))) {
        $state = $g.Save()
        $g.TranslateTransform($head[0], $head[1]); $g.RotateTransform(-20)
        $g.FillEllipse($white, -14, -11, 28, 22)
        $g.Restore($state)
    }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $bg.Dispose(); $box.Dispose()
}
Write-PluginPng "$OutDir\plugin.png" 256
Write-PluginPng "$OutDir\plugin@2x.png" 512
