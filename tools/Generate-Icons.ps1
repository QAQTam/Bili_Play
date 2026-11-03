# Rebuild the vector-based app assets on Windows. No image-generation service required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\Assets'

function Add-RoundedShape($graphics, $brush, [float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $d = 2 * $radius
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($x,$y,$d,$d,180,90); $path.AddArc(($x+$width-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$width-$d),($y+$height-$d),$d,$d,0,90); $path.AddArc($x,($y+$height-$d),$d,$d,90,90)
    $path.CloseFigure(); $graphics.FillPath($brush,$path); $path.Dispose()
}
function New-AppBitmap([int]$width, [int]$height) {
    $bitmap = [System.Drawing.Bitmap]::new(($width*4),($height*4))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Min($width,$height) * 4
    $graphics.TranslateTransform(($width*4-$size)/2,($height*4-$size)/2)
    $graphics.ScaleTransform($size/128.0,$size/128.0)
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#54C8E8'))
    $screen = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#163D59'))
    $pink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F278AB'))
    $white = [System.Drawing.Brushes]::White
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#54C8E8'),6)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pen,39,27,24,6); $graphics.DrawLine($pen,83,27,98,6)
    $graphics.DrawLine($pen,32,111,32,122); $graphics.DrawLine($pen,97,111,97,122)
    Add-RoundedShape $graphics $blue 6 28 116 84 16
    Add-RoundedShape $graphics $screen 13 35 102 70 11
    $pen.Color = [System.Drawing.Color]::White
    $graphics.DrawLine($pen,38,57,38,74); $graphics.DrawLine($pen,83,57,83,74)
    $pen.Width = 4
    $graphics.DrawBezier($pen,49,86,59,95.333,68.667,95.333,78,86)
    $graphics.FillEllipse($pink,66,66,60,60)
    $graphics.TranslateTransform(96,96)
    $graphics.ScaleTransform(2.02,2.02)
    $graphics.TranslateTransform(-104.5,-96.5)
    $note = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $note.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(103,88),[System.Drawing.PointF]::new(114,85),
        [System.Drawing.PointF]::new(114,92),[System.Drawing.PointF]::new(107,94),
        [System.Drawing.PointF]::new(107,105),[System.Drawing.PointF]::new(103,105)))
    $graphics.FillPath($white,$note); $graphics.FillEllipse($white,95,100,12,8)
    $note.Dispose(); $pen.Dispose(); $blue.Dispose(); $screen.Dispose(); $pink.Dispose(); $graphics.Dispose()
    $output = [System.Drawing.Bitmap]::new($width,$height)
    $resize = [System.Drawing.Graphics]::FromImage($output)
    $resize.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resize.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $resize.DrawImage($bitmap,0,0,$width,$height)
    $resize.Dispose(); $bitmap.Dispose()
    return $output
}

$images = @{
    'StoreLogo.png' = @(50,50)
}
foreach ($scale in @(100,125,150,200,400)) {
    foreach ($asset in @(
        @('Square44x44Logo',44,44), @('Square150x150Logo',150,150),
        @('StoreLogo',50,50), @('Wide310x150Logo',310,150), @('SplashScreen',620,300)
    )) {
        $images["$($asset[0]).scale-$scale.png"] = @([int][Math]::Ceiling($asset[1]*$scale/100),[int][Math]::Ceiling($asset[2]*$scale/100))
    }
}
foreach ($target in @(16,20,24,30,32,36,40,44,48,60,64,72,80,96,128,256)) {
    foreach ($variant in @('', '_altform-unplated', '_altform-lightunplated')) {
        $images["Square44x44Logo.targetsize-$target$variant.png"] = @($target,$target)
    }
}
foreach ($name in $images.Keys) {
    $dimensions = $images[$name]
    $bitmap = New-AppBitmap $dimensions[0] $dimensions[1]
    $bitmap.Save((Join-Path $assetDirectory $name),[System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$frames = foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-AppBitmap $size $size
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose(); $bitmap.Dispose()
}
$iconPath = Join-Path $assetDirectory 'AppIcon.ico'
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Bytes.Length); $writer.Write([UInt32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
