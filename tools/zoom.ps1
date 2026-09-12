param(
    [string]$Src,
    [int]$CropX = 0,
    [int]$CropY = 0,
    [int]$CropW = 100,
    [int]$CropH = 100,
    [int]$Zoom = 3,
    [string]$Out = "zoom_out.png"
)
Add-Type -AssemblyName System.Drawing
$srcBmp = [System.Drawing.Bitmap]::FromFile($Src)
$ow = [int]($CropW * $Zoom)
$oh = [int]($CropH * $Zoom)
$dst = New-Object System.Drawing.Bitmap -ArgumentList $ow, $oh
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$srcRect = New-Object System.Drawing.Rectangle -ArgumentList $CropX, $CropY, $CropW, $CropH
$dstRect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $ow, $oh
$g.DrawImage($srcBmp, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$dst.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $srcBmp.Dispose(); $dst.Dispose()
Write-Host "saved: $Out"
