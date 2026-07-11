param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

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
    $sourceRect = [System.Drawing.Rectangle]::new(0, 0, $source.Width, $source.Height)

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

            $graphics.Clear([System.Drawing.Color]::Transparent)

            $scale = [Math]::Min(
                ($size * 0.94) / $sourceRect.Width,
                ($size * 0.94) / $sourceRect.Height)
            $targetWidth = [Math]::Max(1, [int][Math]::Round($sourceRect.Width * $scale))
            $targetHeight = [Math]::Max(1, [int][Math]::Round($sourceRect.Height * $scale))
            $target = [System.Drawing.Rectangle]::new(
                [int](($size - $targetWidth) / 2),
                [int](($size - $targetHeight) / 2),
                $targetWidth,
                $targetHeight)
            $graphics.DrawImage($source, $target, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)

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
