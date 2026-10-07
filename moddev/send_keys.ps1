param(
    [string]$Keys = "{ENTER}",
    [int]$PreDelayMs = 300
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Keys {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
$proc = Get-Process Dustline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { "no game window" | Out-File "D:\Dustline\dustline\moddev\shots\keys.txt" -Encoding utf8; exit 1 }
[Win32Keys]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
[Win32Keys]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds $PreDelayMs
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Milliseconds 300
"sent $Keys to PID $($proc.Id)" | Out-File "D:\Dustline\dustline\moddev\shots\keys.txt" -Encoding utf8