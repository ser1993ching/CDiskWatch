# 内存探针：对运行中的 CDiskWatch 进程分阶段采样（工作集 / 私有内存 / 句柄 / GDI+USER 对象）
param(
  [string]$Exe = "E:\code\C盘容量监控\publish\win-x64\CDiskWatch.exe",
  [string]$Csv = "C:	emp\cdw-mem.csv"
)
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class M {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hp, int flags);
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
"@
function Find-Win {
  $script:h=[IntPtr]::Zero
  $cb=[M+EnumProc]{ param($hw,$l)
    $sb=New-Object System.Text.StringBuilder 128
    [void][M]::GetClassNameW($hw,$sb,128)
    if ($sb.ToString() -eq "CDiskWatchWnd" -and [M]::IsWindowVisible($hw)) { $script:h=$hw; return $false }
    return $true }
  [void][M]::EnumWindows($cb,[IntPtr]::Zero)
  return $script:h
}
function Sample($phase) {
  $script:rows += [pscustomobject]@{
    t = (Get-Date).ToString("HH:mm:ss"); phase = $phase
    wsMB = [math]::Round($script:p.WorkingSet64/1MB,1)
    privMB = [math]::Round($script:p.PrivateMemorySize64/1MB,1)
    pagedMB = [math]::Round($script:p.PagedMemorySize64/1MB,1)
    handles = $script:p.HandleCount
    threads = $script:p.Threads.Count
    gdi = [M]::GetGuiResources($script:hp,0)
    user = [M]::GetGuiResources($script:hp,1)
  }
}
Get-Process CDiskWatch -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 500
Start-Process -FilePath $Exe -ArgumentList '--preview','--no-elevate' | Out-Null
Start-Sleep -Seconds 6
$script:p = Get-Process CDiskWatch | Select-Object -First 1
$script:hp = [M]::OpenProcess(0x0400, $false, $script:p.Id)
$script:rows = @()

1..6 | ForEach-Object { $script:p.Refresh(); Sample "1-idle-panel"; Start-Sleep -Milliseconds 800 }

$h = Find-Win
if ($h -ne [IntPtr]::Zero) {
  $lp = [IntPtr]((254 -shl 16) -bor 447)
  [void][M]::PostMessageW($h,0x0200,[IntPtr]::Zero,$lp)
  [void][M]::PostMessageW($h,0x0201,[IntPtr]1,$lp)
  [void][M]::PostMessageW($h,0x0202,[IntPtr]::Zero,$lp)
  Start-Sleep -Seconds 2
  1..4 | ForEach-Object { $script:p.Refresh(); Sample "2-history-opened"; Start-Sleep -Milliseconds 800 }
}

if ($h -ne [IntPtr]::Zero) {
  # 触发全盘扫描（F5）后连续采样 60 秒，把整个时间序列记下来（扫描耗时不定，靠时间戳对齐）
  [void][M]::PostMessageW($h, 0x0100, [IntPtr]0x74, [IntPtr]::Zero)
  1..170 | ForEach-Object { $script:p.Refresh(); Sample "3-timeline"; Start-Sleep -Milliseconds 350 }
}

$tl = $script:rows | Where-Object { $_.phase -eq "3-timeline" }
"timeline ws:    " + (($tl | ForEach-Object { $_.wsMB }) -join " ")
"timeline priv:  " + (($tl | ForEach-Object { $_.privMB }) -join " ")
"timeline hndl:  " + (($tl | ForEach-Object { $_.handles }) -join " ")
"== summary (MB) =="
$script:rows | Group-Object phase | ForEach-Object {
  $g=$_.Group
  [pscustomobject]@{
    phase = $_.Name
    WS_peak = ($g | Measure-Object wsMB -Maximum).Maximum
    WS_avg = [math]::Round(($g | Measure-Object wsMB -Average).Average,1)
    PRIV_peak = ($g | Measure-Object privMB -Maximum).Maximum
    PRIV_avg = [math]::Round(($g | Measure-Object privMB -Average).Average,1)
    handles = ($g | Measure-Object handles -Maximum).Maximum
    threads = ($g | Measure-Object threads -Maximum).Maximum
    gdi = ($g | Measure-Object gdi -Maximum).Maximum
    user = ($g | Measure-Object user -Maximum).Maximum
  }
} | Format-Table -AutoSize
$tail = $script:rows | Select-Object -Last 6
"== settled (last 6 samples) =="
"WS   : " + (($tail | ForEach-Object { $_.wsMB }) -join " ")
"PRIV : " + (($tail | ForEach-Object { $_.privMB }) -join " ")
"HNDL : " + (($tail | ForEach-Object { $_.handles }) -join " ")
if ($script:hp -ne [IntPtr]::Zero) { [void][M]::CloseHandle($script:hp) }
