param(
    [switch]$Quiet
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Focus {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
    public const uint WM_CLOSE = 0x0010;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); return sb.ToString(); }
}
"@
$log = @()
$proc = Get-Process Dustline -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { "no game window" | Out-File "D:\Dustline\dustline\moddev\shots\focus.txt" -Encoding utf8; exit 1 }
$hwnd = $proc.MainWindowHandle

# 1) 关掉抢占焦点的窗口（比如 Windows 搜索）
for ($i = 0; $i -lt 4; $i++) {
    $fg = [Focus]::GetForegroundWindow()
    if ($fg -eq $hwnd) { break }
    $title = [Focus]::Title($fg)
    $log += "closing foreground: '$title'"
    $r = [IntPtr]::Zero
    [Focus]::SendMessageTimeout($fg, [Focus]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero, [Focus]::SMTO_ABORTIFHUNG, 200, [ref]$r) | Out-Null
    Start-Sleep -Milliseconds 300
}

# 2) 用 AttachThreadInput 强制夺回焦点
[Focus]::ShowWindow($hwnd, 9) | Out-Null
$fg = [Focus]::GetForegroundWindow()
$targetThread = [Focus]::GetWindowThreadProcessId($hwnd, [ref]([uint32]0))
$fgThread = [Focus]::GetWindowThreadProcessId($fg, [ref]([uint32]0))
$myThread = [Focus]::GetCurrentThreadId()
[Focus]::AttachThreadInput($myThread, $targetThread, $true) | Out-Null
[Focus]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
[Focus]::BringWindowToTop($hwnd) | Out-Null
[Focus]::SetForegroundWindow($hwnd) | Out-Null
[Focus]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
[Focus]::AttachThreadInput($myThread, $targetThread, $false) | Out-Null
Start-Sleep -Milliseconds 300

$now = [Focus]::GetForegroundWindow()
$log += "foreground now: '$([Focus]::Title($now))'"
$log += $(if ($now -eq $hwnd) { "FOCUS OK" } else { "FOCUS FAILED" })
$log | Out-File "D:\Dustline\dustline\moddev\shots\focus.txt" -Encoding utf8