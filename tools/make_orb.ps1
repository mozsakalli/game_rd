Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap -ArgumentList 64, 64
$g = [System.Drawing.Graphics]::FromImage($b)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddEllipse(2, 2, 60, 60)
$br = New-Object System.Drawing.Drawing2D.PathGradientBrush -ArgumentList $path
$br.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$br.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
$br.CenterPoint = New-Object System.Drawing.PointF -ArgumentList 26, 26
$g.FillEllipse($br, 2, 2, 60, 60)
$g.Dispose()
$b.Save('C:\Work\digitoygames\game_rd\Projects\Sandbox\Assets\Textures\orb.png', [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Host 'orb yazildi'
