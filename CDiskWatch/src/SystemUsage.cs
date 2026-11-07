using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CDiskWatch;

/// <summary>卷影副本 / 还原点占用信息。</summary>
public sealed class ShadowInfo
{
    public bool Available;
    public long UsedBytes;
    public long AllocatedBytes;
    public int RestorePointCount;
    public string Raw = "";
    public string Error = "";
}

public static class SystemUsage
{
    static bool? _isAdmin;

    /// <summary>当前进程是否以管理员身份运行（结果缓存：进程生命周期内权限不会变化）。</summary>
    public static bool IsAdmin()
    {
        if (_isAdmin.HasValue) return _isAdmin.Value;
        bool v = false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            v = p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { v = false; }
        _isAdmin = v;
        return v;
    }

    /// <summary>
    /// 查询卷影副本 / 系统还原点占用（只读查询，不做任何清理）。
    /// 优先用 CIM（属性名稳定），失败时退回 vssadmin 文本解析。
    /// </summary>
    public static ShadowInfo QueryShadow(string drive = "C")
    {
        var info = new ShadowInfo();
        char driveLetter = string.IsNullOrWhiteSpace(drive) ? 'C' : char.ToUpperInvariant(drive.Trim()[0]);
        if (driveLetter < 'A' || driveLetter > 'Z')
        {
            info.Error = "Invalid drive letter.";
            return info;
        }
        string driveSpec = driveLetter + ":";
        try
        {
            string ps = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                        "$letter = '" + driveSpec + "'; " +
                        "$v = Get-CimInstance Win32_Volume -Filter \"DriveLetter='$letter'\" -ErrorAction Stop; " +
                        "if (-not $v) { throw 'Target volume was not found.' }; " +
                        "$s = Get-CimInstance Win32_ShadowStorage -ErrorAction Stop | Where-Object { $_.Volume -eq $v.DeviceID } | Select-Object -First 1; " +
                        "$c = @(Get-CimInstance Win32_ShadowCopy -ErrorAction Stop | Where-Object { $_.VolumeName -eq $v.DeviceID }).Count; " +
                        "if ($s) { [pscustomobject]@{ Used=[int64]$s.UsedSpace; Allocated=[int64]$s.AllocatedSpace; Count=$c } | ConvertTo-Json -Compress } " +
                        "else { [pscustomobject]@{ Used=0; Allocated=0; Count=$c } | ConvertTo-Json -Compress }";
            string outp = RunProcess("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps.Replace("\"", "\\\"") + "\"",
                25_000, Encoding.UTF8);
            info.Raw = outp;
            if (!string.IsNullOrWhiteSpace(outp))
            {
                using var doc = JsonDocument.Parse(outp.Trim());
                var root = doc.RootElement;
                if (root.TryGetProperty("Used", out var u) && u.TryGetInt64(out long uv)) info.UsedBytes = uv;
                if (root.TryGetProperty("Allocated", out var a) && a.TryGetInt64(out long av)) info.AllocatedBytes = av;
                if (root.TryGetProperty("Count", out var c) && c.TryGetInt32(out int cv)) info.RestorePointCount = cv;
                info.Available = true;
                return info;
            }
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }

        // 兜底：vssadmin 文本（顺序解析：先「已使用」，后「已分配」）
        try
        {
            string outp = RunProcess("vssadmin.exe", "list shadowstorage /for=" + driveSpec, 20_000, null);
            info.Raw = outp;
            var vals = ParseSizes(outp);
            if (vals.Count > 0)
            {
                info.UsedBytes = vals[0];
                info.Available = true;
            }
            if (vals.Count > 1) info.AllocatedBytes = vals[1];
        }
        catch (Exception ex)
        {
            if (info.Error.Length == 0) info.Error = ex.Message;
        }
        return info;
    }

    /// <summary>从任意文本里按顺序抽取 "数字 + 单位"（GB/MB/TB/KB），用于本地化输出的兜底解析。</summary>
    public static List<long> ParseSizes(string text)
    {
        var res = new List<long>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsDigit(text[i]))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',')) i++;
                string num = text[start..i].Replace(",", "");
                int j = i;
                while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
                string unit = "";
                if (j + 1 < text.Length && (text[j] == 'G' || text[j] == 'M' || text[j] == 'T' || text[j] == 'K') &&
                    (text[j + 1] == 'B' || text[j + 1] == 'b'))
                {
                    unit = text.Substring(j, 2).ToUpperInvariant();
                    i = j + 2;
                }
                else if (j < text.Length && text[j] == 'B') { unit = "B"; i = j + 1; }
                if (unit.Length > 0 && double.TryParse(num, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    long mult = unit switch { "TB" => 1L << 40, "GB" => 1L << 30, "MB" => 1L << 20, "KB" => 1L << 10, _ => 1 };
                    res.Add((long)(d * mult));
                }
            }
            else i++;
        }
        return res;
    }

    /// <summary>WinSxS 组件存储分析（只读分析命令，不执行任何清理）。</summary>
    public static string AnalyzeWinSxS()
    {
        try
        {
            return RunProcess("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore /NoRestart", 120_000, null);
        }
        catch (Exception ex)
        {
            return Loc.T("cli.analyzeFailed") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// 运行只读查询进程并取标准输出。
    /// 注意：stdout/stderr 同时重定向时必须并行读取，否则子进程写满其中一个管道缓冲后会阻塞，
    /// 而父进程还在等另一个流结束 —— 典型死锁（旧实现就是这样写的）。
    /// </summary>
    public static string RunProcess(string file, string args, int timeoutMs, Encoding? enc)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (enc != null) psi.StandardOutputEncoding = enc;
        using var p = Process.Start(psi);
        if (p == null) return "";

        var soBuf = new StringBuilder();
        var seBuf = new StringBuilder();
        using var soDone = new ManualResetEventSlim(false);
        using var seDone = new ManualResetEventSlim(false);
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) soDone.Set();
            else lock (soBuf) soBuf.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) seDone.Set();
            else lock (seBuf) seBuf.AppendLine(e.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            try { p.WaitForExit(2000); } catch { }
        }
        soDone.Wait(2000);
        seDone.Wait(500);

        string so, se;
        lock (soBuf) so = soBuf.ToString();
        lock (seBuf) se = seBuf.ToString();
        return so.Length > 0 ? so : se;
    }
}
