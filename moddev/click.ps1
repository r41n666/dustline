param(
    [int]$X,
    [int]$Y,
    [switch]$NoTopMost
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Click33 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); return sb.ToString(); }
}
"@
$log = @()
& "D://Dustline//dustline//moddev//focus_game.ps1" | Out-Null
$proc = Get-Process Dustline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { $log += "no window"; $log | Out-File "D:\Dustline\dustline\moddev\shots\click.txt" -Encoding utf8; exit 1 }
$hwnd = $proc.MainWindowHandle

[Click33]::ShowWindow($hwnd, 9) | Out-Null
if (-not $NoTopMost) {
    [Click33]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null  # TOPMOST|SHOWWINDOW|NOMOVE|NOSIZE
}
[Click33]::BringWindowToTop($hwnd) | Out-Null
[Click33]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400
$fg = [Click33]::GetForegroundWindow()
$log += "game=$([Click33]::Title($hwnd)) foreground=$([Click33]::Title($fg))"

$rect = New-Object Click33+RECT
[Click33]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$sx = $rect.Left + $X
$sy = $rect.Top + $Y
[Click33]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 200
[Click33]::mouse_event([Click33]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[Click33]::mouse_event([Click33]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
$log += "clicked window($X,$Y) -> screen($sx,$sy) winRect=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom))"
$log | Out-File "D:\Dustline\dustline\moddev\shots\click.txt" -Encoding utf8