param(
    [Parameter(Mandatory = $true)][string]$Input
)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($Input)
$lines = @("size: $($img.Width) x $($img.Height)")
$cols = 6; $rows = 4
for ($r = 0; $r -lt $rows; $r++) {
    $row = @()
    for ($c = 0; $c -lt $cols; $c++) {
        $x = [int](($c + 0.5) * $img.Width / $cols)
        $y = [int](($r + 0.5) * $img.Height / $rows)
        $p = $img.GetPixel($x, $y)
        $b = [int](0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B)
        $row += ("{0,3}" -f $b)
    }
    $lines += ($row -join " ")
}
$img.Dispose()
$lines | Out-File "D:\Dustline\dustline\moddev\shots\probe.txt" -Encoding utf8