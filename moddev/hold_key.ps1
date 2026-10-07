param(
    [string]$Key = "w",
    [int]$HoldMs = 1200,
    [switch]$MouseDx,
    [int]$Dx = 0,
    [int]$Dy = 0
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Input32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    public const uint KEYUP = 0x0002;
    public const uint MOVE = 0x0001;
    public const uint LEFTDOWN = 0x0002;
}
"@
$proc = Get-Process Dustline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { "no window" | Out-File "D:\Dustline\dustline\moddev\shots\input.txt" -Encoding utf8; exit 1 }
[Input32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
[Input32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 250

if ($MouseDx) {
    [Input32]::mouse_event([Input32]::MOVE, $Dx, $Dy, 0, [UIntPtr]::Zero)
    "mouse move $Dx,$Dy" | Out-File "D:\Dustline\dustline\moddev\shots\input.txt" -Encoding utf8
    exit 0
}

$vk = [byte][System.Windows.Forms.SendKeys]::ToSpecialChar($Key) 2>$null
$map = @{ 'w' = 0x57; 'a' = 0x41; 's' = 0x53; 'd' = 0x44; 'shift' = 0x10; 'ctrl' = 0x11; 'space' = 0x20; 'v' = 0x56; 'r' = 0x52; 'e' = 0x45; '1' = 0x31; '2' = 0x32; '3' = 0x33 }
$code = [byte]$map[$Key.ToLower()]
if ($code -eq 0) { "unknown key $Key" | Out-File "D:\Dustline\dustline\moddev\shots\input.txt" -Encoding utf8; exit 1 }
[Input32]::keybd_event($code, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[Input32]::keybd_event($code, 0, [Input32]::KEYUP, [UIntPtr]::Zero)
"held $Key for $HoldMs ms" | Out-File "D:\Dustline\dustline\moddev\shots\input.txt" -Encoding utf8