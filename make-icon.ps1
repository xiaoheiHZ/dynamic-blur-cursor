Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 32, 48, 256)) {
 $bmp = [System.Drawing.Bitmap]::new($size,$size)
 $g = [System.Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.ScaleTransform($size/256.0,$size/256.0)
 $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
 $path.AddArc(10,10,236,236,180,90); $path.AddArc(10,10,236,236,270,90); $path.AddArc(10,10,236,236,0,90); $path.AddArc(10,10,236,236,90,90); $path.CloseFigure()
 $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Point]::new(0,0),[System.Drawing.Point]::new(256,256),[System.Drawing.Color]::FromArgb(35,181,232),[System.Drawing.Color]::FromArgb(105,66,222))
 $g.FillPath($grad,$path)
 $trailpen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(120,210,245,255),13); $trailpen.StartCap='Round'; $trailpen.EndCap='Round'
 $g.DrawLine($trailpen,54,162,104,112); $g.DrawLine($trailpen,67,193,120,140)
 $shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(75,13,20,72))
 $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
 $p = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(92,46),[System.Drawing.PointF]::new(95,170),[System.Drawing.PointF]::new(124,142),[System.Drawing.PointF]::new(146,188),[System.Drawing.PointF]::new(166,178),[System.Drawing.PointF]::new(143,132),[System.Drawing.PointF]::new(183,129))
 $g.TranslateTransform(5,7); $g.FillPolygon($shadow,$p); $g.TranslateTransform(-5,-7); $g.FillPolygon($white,$p)
 $stream=[System.IO.MemoryStream]::new(); $bmp.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png); $frames += ,$stream.ToArray()
 $stream.Dispose(); $white.Dispose(); $shadow.Dispose(); $trailpen.Dispose(); $grad.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
}
$target = Join-Path $PSScriptRoot 'app.ico'
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($target))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
for($i=0;$i -lt $frames.Count;$i++) { $s=@(16,32,48,256)[$i]; $byteSize=if($s -eq 256){0}else{$s}; $writer.Write([byte]$byteSize); $writer.Write([byte]$byteSize); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([int]$frames[$i].Length); $writer.Write([int]$offset); $offset += $frames[$i].Length }
foreach($frame in $frames){$writer.Write([byte[]]$frame)}
$writer.Dispose()
