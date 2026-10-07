param(
    [Parameter(Mandatory = $true)][string]$Input,
    [Parameter(Mandatory = $true)][string]$Output,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 420,
    [int]$H = 420,
    [double]$Zoom = 2.0
)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($Input)
$dst = New-Object System.Drawing.Bitmap ([int]($W * $Zoom)), ([int]($H * $Zoom))
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$dstRect = New-Object System.Drawing.Rectangle 0, 0, ([int]($W * $Zoom)), ([int]($H * $Zoom))
$srcRect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$g.DrawImage($src, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
if ($Output.ToLower().EndsWith(".jpg") -or $Output.ToLower().EndsWith(".jpeg")) {
    $dst.Save($Output, [System.Drawing.Imaging.ImageFormat]::Jpeg)
} else {
    $dst.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
}
$dst.Dispose()
$src.Dispose()
"cropped ${W}x${H} @($X,$Y) zoom=$Zoom -> $Output" | Out-File "D:\Dustline\dustline\moddev\shots\crop.txt" -Encoding utf8