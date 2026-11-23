$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $body = New-Object Drawing.Drawing2D.GraphicsPath
    $body.AddArc(4, 4, 104, 104, 180, 90)
    $body.AddArc(148, 4, 104, 104, 270, 90)
    $body.AddArc(148, 148, 104, 104, 0, 90)
    $body.AddArc(4, 148, 104, 104, 90, 90)
    $body.CloseFigure()
    $background = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#17131F'))
    $eyes = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#BCACFF'))
    $graphics.FillPath($background, $body)
    $graphics.FillEllipse($eyes, 61, 85, 48, 86)
    $graphics.FillEllipse($eyes, 147, 85, 48, 86)
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose(); $eyes.Dispose(); $background.Dispose(); $body.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$destination = Join-Path $PSScriptRoot '..\Assets\HelperBar.ico'
$stream = [IO.File]::Create($destination)
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Host "Icon: $destination"
