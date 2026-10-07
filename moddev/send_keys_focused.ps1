param(
    [string]$Keys = "%{ENTER}",
    [int]$PreDelayMs = 300,
    [int]$PostDelayMs = 1500
)
Add-Type -AssemblyName System.Windows.Forms
$proc = Get-Process Dustline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) {
    "no game window" | Out-File "D:\Dustline\dustline\moddev\shots\combo.txt" -Encoding utf8
    exit 1
}
& "D:\Dustline\dustline\moddev\focus_game.ps1" | Out-Null
Start-Sleep -Milliseconds $PreDelayMs
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Milliseconds $PostDelayMs
"sent $Keys" | Out-File "D:\Dustline\dustline\moddev\shots\combo.txt" -Encoding utf8