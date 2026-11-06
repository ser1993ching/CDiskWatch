param(  [string]$Exe = "E:\code\C盘容量监控\publish\win-x64\CDiskWatch.exe", [switch]$Minimized)
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W {
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int pid);
  [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr hp, IntPtr min, IntPtr max);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
"@
Get-Process CDiskWatch -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 400
if ($Minimized) { Start-Process -FilePath $Exe | Out-Null } else { Start-Process -FilePath $Exe -ArgumentList "--preview","--no-elevate" | Out-Null }
Start-Sleep -Seconds 8
$p = Get-Process CDiskWatch | Select-Object -First 1
$p.Refresh()
"before: ws=$([math]::Round($p.WorkingSet64/1MB,1)) priv=$([math]::Round($p.PrivateMemorySize64/1MB,1)) handles=$($p.HandleCount)"
$hp = [W]::OpenProcess(0x1F0FFF, $false, $p.Id)
[void][W]::SetProcessWorkingSetSize($hp, [IntPtr]-1, [IntPtr]-1)
Start-Sleep -Seconds 2
$p.Refresh()
"after EmptyWorkingSet: ws=$([math]::Round($p.WorkingSet64/1MB,1)) priv=$([math]::Round($p.PrivateMemorySize64/1MB,1))"
Start-Sleep -Seconds 6
$p.Refresh()
"6s later: ws=$([math]::Round($p.WorkingSet64/1MB,1)) priv=$([math]::Round($p.PrivateMemorySize64/1MB,1))"
[void][W]::CloseHandle($hp)
