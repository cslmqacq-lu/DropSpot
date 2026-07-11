param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath([System.Drawing.Rectangle]$Rectangle, [int]$Radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = [Math]::Max(2, $Radius * 2)
    $arc = [System.Drawing.Rectangle]::new($Rectangle.X, $Rectangle.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rectangle.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rectangle.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rectangle.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

$sourceFullPath = [System.IO.Path]::GetFullPath($SourcePath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path $outputFullPath -Parent
if (-not (Test-Path -LiteralPath $sourceFullPath)) {
    throw "图标原图不存在：$sourceFullPath"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$source = New-Object System.Drawing.Bitmap $sourceFullPath
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]

try {
    $panelX = [int][Math]::Round($source.Width * 0.165)
    $panelY = [int][Math]::Round($source.Height * 0.37)
    $sourceRect = [System.Drawing.Rectangle]::new(
        $panelX,
        $panelY,
        [int][Math]::Round($source.Width * 0.68),
        [int][Math]::Round($source.Height * 0.49))
    $leftColor = $source.GetPixel(0, [Math]::Min($source.Height - 1, [int]($source.Height / 2)))
    $rightColor = $source.GetPixel($source.Width - 1, [Math]::Min($source.Height - 1, [int]($source.Height / 2)))

    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

            $canvas = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
            $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $canvas,
                $leftColor,
                $rightColor,
                0.0)
            try {
                $graphics.FillRectangle($background, $canvas)
            }
            finally {
                $background.Dispose()
            }

            $scale = [Math]::Min(
                ($size * 0.88) / $sourceRect.Width,
                ($size * 0.52) / $sourceRect.Height)
            $targetWidth = [Math]::Max(1, [int][Math]::Round($sourceRect.Width * $scale))
            $targetHeight = [Math]::Max(1, [int][Math]::Round($sourceRect.Height * $scale))
            $target = [System.Drawing.Rectangle]::new(
                [int](($size - $targetWidth) / 2),
                [int](($size - $targetHeight) / 2),
                $targetWidth,
                $targetHeight)
            $clipPath = New-RoundedRectanglePath $target ([Math]::Max(1, [int][Math]::Round($size * 0.07)))
            $graphicsState = $graphics.Save()
            try {
                $graphics.SetClip($clipPath)
                $graphics.DrawImage($source, $target, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Restore($graphicsState)
                $clipPath.Dispose()
            }

            $stream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $images.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$fileStream = [System.IO.File]::Create($outputFullPath)
$writer = New-Object System.IO.BinaryWriter $fileStream
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $bytes = $images[$index]
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }

    foreach ($bytes in $images) {
        $writer.Write($bytes)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

Write-Output $outputFullPath
