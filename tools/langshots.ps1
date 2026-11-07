param(
  [string]$Exe = "CDiskWatch/bin/Release/net8.0-windows/CDiskWatch.exe",
  [string]$Lang = "en-US",
  [int]$Pages = 7
)

# 多语言界面回归截图：启动 --lang 指定语言，用 ←/→ 遍历页签逐页截图。
# 注意：不要用固定像素坐标点页签——页签宽度是按文字实测的，各语言不同，坐标会错位。

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class K {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

Get-Process CDiskWatch -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 600
Start-Process -FilePath $Exe -ArgumentList "--preview", "--no-elevate", "--lang", $Lang | Out-Null
Start-Sleep -Seconds 6

$script:h = [IntPtr]::Zero
$cb = [K+EnumProc]{
  param($hw, $l)
  $sb = New-Object System.Text.StringBuilder 128
  [void][K]::GetClassNameW($hw, $sb, 128)
  if ($sb.ToString() -eq "CDiskWatchWnd" -and [K]::IsWindowVisible($hw)) { $script:h = $hw; return $false }
  return $true
}
[void][K]::EnumWindows($cb, [IntPtr]::Zero)
if ($script:h -eq [IntPtr]::Zero) { Write-Output "window not found"; exit 1 }
Write-Output ("hwnd=" + $script:h + " lang=" + $Lang)

function Shot($name) {
  $r = New-Object K+RECT
  [void][K]::GetWindowRect($script:h, [ref]$r)
  $w = $r.R - $r.L; $ht = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [void][K]::PrintWindow($script:h, $hdc, 0)
  $g.ReleaseHdc($hdc)
  $file = Join-Path $env:TEMP $name
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ("shot " + $file)
}

function Key($vk) {
  [void][K]::PostMessageW($script:h, 0x0100, [IntPtr]$vk, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
}

# 回到第一页（Home 无绑定，用左方向键连按），再逐页右移
1..7 | ForEach-Object { Key 0x25 }
Start-Sleep -Milliseconds 300

for ($i = 0; $i -lt $Pages; $i++) {
  Shot ("all-" + $Lang + "-" + $i + ".png")
  Key 0x27
}

Get-Process CDiskWatch -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
