$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/CodexUsageWidget/Assets'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$frames = @()
foreach ($size in 16,24,32,48,64,128,256) {
 $visual = [Windows.Media.DrawingVisual]::new()
 $context = $visual.RenderOpen()
 $context.PushTransform([Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
 $surface = [Windows.Media.BrushConverter]::new().ConvertFromString('#141619')
 $context.DrawRoundedRectangle($surface,$null,[Windows.Rect]::new(0,0,256,256),58,58)
 foreach ($ring in @(@{Radius=88;Width=22;Color='#FF397E';End=310},@{Radius=53;Width=20;Color='#9BEF29';End=245})) {
  $brush = [Windows.Media.BrushConverter]::new().ConvertFromString($ring.Color)
  $pen = [Windows.Media.Pen]::new($brush,$ring.Width)
  $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'
  $angle = ($ring.End - 90) * [Math]::PI / 180
  $geometry = [Windows.Media.StreamGeometry]::new()
  $drawing = $geometry.Open()
  $drawing.BeginFigure([Windows.Point]::new(128,128-$ring.Radius),$false,$false)
  $drawing.ArcTo([Windows.Point]::new(128+$ring.Radius*[Math]::Cos($angle),128+$ring.Radius*[Math]::Sin($angle)),[Windows.Size]::new($ring.Radius,$ring.Radius),0,$true,'Clockwise',$true,$false)
  $drawing.Close()
  $context.DrawGeometry($null,$pen,$geometry)
 }
 $context.Pop(); $context.Close()
 $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual)
 $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream = [IO.MemoryStream]::new(); $encoder.Save($stream)
 $frames += @{Size=$size;Data=$stream.ToArray()}; $stream.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $destination 'app.png'), $frames[-1].Data)
$stream = [IO.File]::Create((Join-Path $destination 'app.ico'))
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
foreach ($frame in $frames) {
 $dimension = if($frame.Size -eq 256){0}else{$frame.Size}
 $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
 $writer.Write([uint16]1); $writer.Write([uint16]32)
 $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
 $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Dispose()
Write-Output $destination
