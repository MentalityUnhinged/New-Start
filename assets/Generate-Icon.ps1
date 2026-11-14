$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
function Render-Logo([int]$size) {
    $visual=[System.Windows.Media.DrawingVisual]::new()
    $drawing=$visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    $gradient=[System.Windows.Media.LinearGradientBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#30343c'),[System.Windows.Media.ColorConverter]::ConvertFromString('#15181f'),90)
    $outline=[System.Windows.Media.Pen]::new([System.Windows.Media.BrushConverter]::new().ConvertFromString('#59616d'),1.5)
    $drawing.DrawRoundedRectangle($gradient,$outline,[System.Windows.Rect]::new(8,8,240,240),54,54)
    $ring=[System.Windows.Media.Pen]::new([System.Windows.Media.BrushConverter]::new().ConvertFromString('#fafcff'),12)
    $ring.StartLineCap='Round';$ring.EndLineCap='Round'
    $drawing.DrawGeometry($null,$ring,[System.Windows.Media.Geometry]::Parse('M 177 77 A 74 74 0 1 0 202 132'))
    $drawing.DrawGeometry([System.Windows.Media.BrushConverter]::new().ConvertFromString('#bdefff'),$null,[System.Windows.Media.Geometry]::Parse('M 204 37 L 210 54 L 227 60 L 210 66 L 204 83 L 198 66 L 181 60 L 198 54 Z'))
    $drawing.Pop();$drawing.Close()
    $bitmap=[System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder=[System.Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[System.IO.MemoryStream]::new();$encoder.Save($stream);$bytes=$stream.ToArray();$stream.Dispose();return ,$bytes
}
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@($sizes | ForEach-Object { ,(Render-Logo $_) })
$stream=[System.IO.File]::Create((Join-Path $PSScriptRoot 'NewStart.ico'));$writer=[System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($index=0;$index -lt $sizes.Count;$index++) {
        $dimension=if($sizes[$index] -eq 256){0}else{$sizes[$index]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$index].Length);$writer.Write([uint32]$offset);$offset+=$frames[$index].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose();$stream.Dispose()}
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'logo.png'),(Render-Logo 256))
