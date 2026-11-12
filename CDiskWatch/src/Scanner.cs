using System.Runtime.InteropServices;

namespace CDiskWatch;

public sealed class ScanOptions
{
    public string Drive = "C";
    /// <summary>扫描根路径（默认 = 盘符根目录），自检时指向临时目录。</summary>
    public string RootPath = "";
    /// <summary>目录聚合深度：文件的 min(父目录层级, MaxDirLevel) 级祖先作为分区键。</summary>
    public int MaxDirLevel = 4;
    public int TopFileCount = 100;
    /// <summary>排除的路径前缀（自身数据目录等）。</summary>
    public List<string> ExcludePrefixes = new();
}

public sealed class DirStat
{
    public string Path = "";
    public long Bytes;
    public long FileCount;
    public int Level;
    /// <summary>true = 分区键（互不重叠）；false = 祖先汇总（相互包含）。</summary>
    public bool IsPartition;
}

public sealed class FileStat
{
    public string Path = "";
    public long Bytes;
    public DateTimeOffset Modified;
}

public enum SysFlavor : byte
{
    None = 0,
    SystemFile = 1,
    CrashDump = 2,
    Hiberfil = 3,
    Pagefile = 4,
    Swapfile = 5,
    MemoryDump = 6,
}

public sealed class SysFileStat
{
    public string Path = "";
    public long Bytes;
    /// <summary>文件种类（稳定枚举，随快照落盘；显示文案在读取时翻译）。</summary>
    public SysFlavor Flavor;

    public string KindCode => Flavor is SysFlavor.CrashDump or SysFlavor.MemoryDump ? "dump" : "system";
    public string Kind => Loc.T(Flavor is SysFlavor.CrashDump or SysFlavor.MemoryDump ? "sys.kind.dump" : "sys.kind.system");
    public string Note => Flavor switch
    {
        SysFlavor.Hiberfil => Loc.T("sys.note.hiberfil"),
        SysFlavor.Pagefile => Loc.T("sys.note.pagefile"),
        SysFlavor.Swapfile => Loc.T("sys.note.swapfile"),
        SysFlavor.MemoryDump => Loc.T("sys.note.memdump"),
        _ => "",
    };

    /// <summary>由文件名判定种类（扫描与旧快照兼容共用）。</summary>
    public static SysFlavor FlavorOf(string fileName)
    {
        if (fileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)) return SysFlavor.Hiberfil;
        if (fileName.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase)) return SysFlavor.Pagefile;
        if (fileName.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase)) return SysFlavor.Swapfile;
        if (fileName.Equals("MEMORY.DMP", StringComparison.OrdinalIgnoreCase)) return SysFlavor.MemoryDump;
        if (fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)) return SysFlavor.CrashDump;
        return SysFlavor.SystemFile;
    }
}

public sealed class ScanResult
{
    public DateTimeOffset Time;
    public string Drive = "C";
    public long TotalBytes;
    public long FreeBytes;
    public long UsedBytes;
    public long LogicalBytes;
    public long UnaccountedBytes;
    public long FileCount;
    public long DirCount;
    public long CacheBytes;
    public long TempBytes;
    public long HardlinkBytes;
    public long[] CatBytes = new long[Cat.Count];
    public long[] CatCounts = new long[Cat.Count];
    public List<DirStat> Dirs = new();
    public List<FileStat> TopFiles = new();
    public List<SysFileStat> SysFiles = new();
    public double DurationMs;
    public string Error = "";
    /// <summary>因权限/IO 失败无法枚举的目录数。</summary>
    public int UnreadableDirs;
    /// <summary>被排除规则跳过的目录数。</summary>
    public int ExcludedDirs;
    /// <summary>因超过扩展路径缓冲区上限而跳过的目录数。</summary>
    public int PathTooLongCount;
    /// <summary>完全没有跳过任何目录/路径的理想结果（大多数机器上做不到，仅用于自检与统计）。</summary>
    public bool IsComplete => Error.Length == 0 && UnreadableDirs == 0 && PathTooLongCount == 0;

    /// <summary>致命失败：连卷信息都读不到，结果毫无意义，绝不能落盘。</summary>
    public bool IsFatal => TotalBytes <= 0;

    /// <summary>
    /// 有跳过（无权限目录 / 超长路径 / 子目录枚举失败）：文件明细会偏小，
    /// 但卷级「总容量 / 可用空间」来自 GetDiskFreeSpaceEx，仍然准确。
    /// 这类结果**仍然要落盘并刷新界面**，只是打上 Partial 标记——
    /// 否则只要有一个 System Volume Information 之类的目录读不到，界面就会永远停在旧数据上。
    /// </summary>
    public bool IsPartial => !IsFatal && (UnreadableDirs > 0 || PathTooLongCount > 0 || Error.Length > 0);

    /// <summary>占用率（0..1）。</summary>
    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
}

/// <summary>
/// 全盘流式扫描器。
/// 关键点：
///  1) 直接调用 Win32 FindFirstFile/FindNextFile（不走 FileSystemEnumerable），
///     每个文件零托管分配（框架枚举器每文件约 80 字节，70 万文件就是 70MB 垃圾）；
///  2) 目录聚合用「非重叠分区 + 祖先汇总」，内存 ~2MB；
///  3) Top 大文件用定长数组插入排序，只有能进榜的文件才分配路径字符串；
///  4) 路径缓冲与查找缓冲全部复用，扫描期几乎不产生垃圾。
/// </summary>
public static class Scanner
{
    // ===== WIN32_FIND_DATAW 字段偏移（64 位） =====
    // WIN32_FIND_DATAW 布局（FILETIME 仅 4 字节对齐）：
    //   0 dwFileAttributes | 4 ftCreationTime | 12 ftLastAccessTime | 20 ftLastWriteTime
    //   28 nFileSizeHigh | 32 nFileSizeLow | 36 dwReserved0 | 40 dwReserved1 | 44 cFileName[260]
    const int FindDataSize = 592;
    const int OffAttributes = 0;
    const int OffLastWrite = 20;       // FILETIME（8 字节）
    const int OffSizeHigh = 28;
    const int OffSizeLow = 32;
    const int OffFileName = 44;        // WCHAR[260]

    /// <summary>原生路径前缀 "\\?\" 的长度（显示路径时要去掉）。</summary>
    const int PathPrefixLen = 4;

    const uint FileAttributeDirectory = 0x10;
    const uint FileAttributeReparsePoint = 0x400;
    static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>把路径截断到指定层级（level = 分隔符个数，根目录为 0）。</summary>
    public static string TruncateToLevel(string path, int maxLevel)
    {
        int seps = 0;
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '\\')
            {
                seps++;
                if (seps > maxLevel) return path.Substring(0, i);
            }
        }
        return path;
    }

    /// <summary>路径层级（根目录 0）。</summary>
    public static int LevelOf(string path)
    {
        int n = 0;
        for (int i = 0; i < path.Length; i++) if (path[i] == '\\') n++;
        if (n > 0 && path[path.Length - 1] == '\\') n--; // 去掉结尾斜杠
        return n;
    }

    public static ScanResult Scan(ScanOptions opt, CancellationToken ct, Action<string>? progress = null)
    {
        var res = new ScanResult { Drive = opt.Drive, Time = DateTimeOffset.Now };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var di = new DriveInfo(opt.Drive);
            res.TotalBytes = di.TotalSize;
            res.FreeBytes = di.AvailableFreeSpace;
            res.UsedBytes = di.TotalSize - di.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            res.Error = Loc.F("cli.readDiskFailed", ex.Message);
        }

        string root = string.IsNullOrEmpty(opt.RootPath) ? opt.Drive + ":\\" : opt.RootPath;
        var ctx = new ScanContext(opt, res);
        try
        {
            ctx.Run(root, ct);
        }
        catch (Exception ex)
        {
            res.Error = ex.Message;
        }

        ctx.Finish();
        sw.Stop();
        res.DurationMs = sw.ElapsedMilliseconds;
        // 未归因 = 实际已用 - 文件逻辑合计；为负说明存在硬链接重复计数（WinSxS 等）
        res.UnaccountedBytes = res.UsedBytes - res.LogicalBytes;
        if (res.UnaccountedBytes < 0)
        {
            res.HardlinkBytes = -res.UnaccountedBytes;
            res.UnaccountedBytes = 0;
        }
        progress?.Invoke(Loc.F("cli.scanProgress", res.FileCount.ToString("N0"), (res.DurationMs / 1000.0).ToString("F1")));
        return res;
    }

    // ===== 目录精确测量（供建议引擎复用同一套原生遍历） =====

    /// <summary>
    /// 精确测量目录占用（原生递归，跳过联接/符号链接）。
    /// 与原来的 FileSystemEnumerable 相比：结果与耗时一致（都是磁盘瓶颈），但**句柄峰值从 6 万降到几百**，
    /// 且几乎不产生托管垃圾（实测 C:\Windows\WinSxS：59,6xx → 211 个句柄）。
    /// </summary>
    public static long MeasureTree(string path, out int unreadableDirs)
    {
        long total = 0;
        int unreadable = 0;
        unreadableDirs = 0;
        if (string.IsNullOrWhiteSpace(path)) return 0;

        // Windows 扩展路径上限约 32K；为路径和末尾通配符预留空间。
        var pathBuf = new char[32768];
        var patChars = new char[32770];
        IntPtr findBuf = Marshal.AllocHGlobal(FindDataSize);
        IntPtr patBuf = Marshal.AllocHGlobal(patChars.Length * sizeof(char));
        try
        {
            pathBuf[0] = (char)92; pathBuf[1] = (char)92; pathBuf[2] = '?'; pathBuf[3] = (char)92;
            if (path.Length + PathPrefixLen + 2 >= pathBuf.Length)
            {
                unreadable++;
                unreadableDirs = unreadable;
                return 0;
            }
            int n = path.Length;
            for (int i = 0; i < n; i++) pathBuf[PathPrefixLen + i] = path[i];
            int len = PathPrefixLen + n;
            if (len == PathPrefixLen || pathBuf[len - 1] != (char)92) pathBuf[len++] = (char)92;
            MeasureRec(pathBuf, len, findBuf, patBuf, patChars, ref total, ref unreadable);
        }
        finally
        {
            Marshal.FreeHGlobal(findBuf);
            Marshal.FreeHGlobal(patBuf);
        }
        unreadableDirs = unreadable;
        return total;
    }

    static void MeasureRec(char[] pathBuf, int pathLen, IntPtr findBuf, IntPtr patBuf, char[] patChars,
                           ref long total, ref int unreadable)
    {
        BuildSearchPattern(pathBuf, pathLen, patChars, patBuf);

        IntPtr h = Win32.FindFirstFileW(patBuf, findBuf);
        if (h == InvalidHandleValue) { unreadable++; return; }
        try
        {
            do
            {
                uint attr = (uint)Marshal.ReadInt32(findBuf, OffAttributes);
                int nameLen = FileNameLength(findBuf);
                if (nameLen == 0) continue;
                if (nameLen <= 2 && Marshal.ReadInt16(findBuf, OffFileName) == '.' ) continue;   // "." / ".."

                if ((attr & FileAttributeDirectory) != 0)
                {
                    if ((attr & FileAttributeReparsePoint) != 0) continue;
                    if (pathLen + nameLen + 1 >= pathBuf.Length) { unreadable++; continue; }
                    CopyFileName(findBuf, nameLen, new Span<char>(pathBuf, pathLen, nameLen));
                    pathBuf[pathLen + nameLen] = (char)92;
                    MeasureRec(pathBuf, pathLen + nameLen + 1, findBuf, patBuf, patChars, ref total, ref unreadable);
                    continue;
                }

                long size = ((long)(uint)Marshal.ReadInt32(findBuf, OffSizeHigh) << 32)
                          | (uint)Marshal.ReadInt32(findBuf, OffSizeLow);
                if (size > 0) total += size;
            }
            while (Win32.FindNextFileW(h, findBuf));
            // ERROR_NO_MORE_FILES (18) is the normal end of enumeration.
            if (Marshal.GetLastWin32Error() != 18) unreadable++;
        }
        finally { Win32.FindClose(h); }
    }

    static unsafe int FileNameLength(IntPtr buf)
    {
        var name = new ReadOnlySpan<char>((char*)(buf + OffFileName), 260);
        int length = name.IndexOf('\0');
        return length < 0 ? 260 : length;
    }

    static unsafe void CopyFileName(IntPtr buf, int nameLen, Span<char> destination)
    {
        new ReadOnlySpan<char>((char*)(buf + OffFileName), nameLen).CopyTo(destination);
    }

    /// <summary>构造搜索模式 "&lt;path&gt;*"，返回写入的路径字符数（不含 '*'，供 cache/temp 判定复用）。</summary>
    static int BuildSearchPattern(char[] path, int pathLen, char[] pattern, IntPtr nativePattern)
    {
        // 末尾要留 '*' + 结束符，因此路径最多 pattern.Length - 3 个字符
        int n = Math.Min(pathLen, pattern.Length - 3);
        Array.Copy(path, pattern, n);
        pattern[n] = '*';
        pattern[n + 1] = '\0';
        Marshal.Copy(pattern, 0, nativePattern, n + 2);
        return n;
    }

    sealed class ScanContext
    {
        readonly ScanOptions _opt;
        readonly ScanResult _res;
        readonly Dictionary<string, DirStat> _parts = new(StringComparer.OrdinalIgnoreCase);
        readonly string[] _exclude;

        // 复用的路径缓冲区（当前目录）与原生查找缓冲区
        readonly char[] _path = new char[32768];
        int _pathLen;
        IntPtr _findBuf = IntPtr.Zero;      // WIN32_FIND_DATAW
        IntPtr _patBuf = IntPtr.Zero;       // 搜索模式 "path\*"
        readonly char[] _patChars = new char[32770];

        readonly FileStat[] _top;
        int _topCount;
        readonly char[] _extBuf = new char[16];   // 扩展名缓冲（复用，避免每文件分配）
        readonly char[] _nameBuf = new char[260]; // 文件名缓冲（复用）

        // 分区键前缀缓存（避免频繁截断字符串）
        string _keyPrefix = "";
        int _keyPrefixLevel = -1;

        public ScanContext(ScanOptions opt, ScanResult res)
        {
            _opt = opt;
            _res = res;
            _top = new FileStat[Math.Max(1, opt.TopFileCount)];
            _exclude = opt.ExcludePrefixes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\')).ToArray();
        }

        public void Run(string root, CancellationToken ct)
        {
            _findBuf = Marshal.AllocHGlobal(FindDataSize);
            _patBuf = Marshal.AllocHGlobal(_patChars.Length * sizeof(char));
            try
            {
                // 使用 "\\?\" 扩展长度前缀（4 个字符：\ \ ? \），支持超过 MAX_PATH 的路径
                _path[0] = '\\'; _path[1] = '\\'; _path[2] = '?'; _path[3] = '\\';
                if (root.Length + PathPrefixLen + 2 >= _path.Length)
                {
                    _res.Error = "Scan root path exceeds the Windows extended-path limit.";
                    return;
                }
                int n = root.Length;
                for (int i = 0; i < n; i++) _path[PathPrefixLen + i] = root[i];
                _pathLen = PathPrefixLen + n;
                if (_pathLen == 0 || _path[_pathLen - 1] != '\\') _path[_pathLen++] = '\\';
                ScanDir(ct);
            }
            finally
            {
                if (_findBuf != IntPtr.Zero) { Marshal.FreeHGlobal(_findBuf); _findBuf = IntPtr.Zero; }
                if (_patBuf != IntPtr.Zero) { Marshal.FreeHGlobal(_patBuf); _patBuf = IntPtr.Zero; }
            }
        }

        void ScanDir(CancellationToken ct)
        {
            int savedLen = _pathLen;

            // 排除路径
            foreach (var ex in _exclude)
            {
                if (PathStartsWith(savedLen, ex))
                {
                    _res.ExcludedDirs++;
                    return;
                }
            }
            if (ct.IsCancellationRequested) { _res.Error = Loc.T("cli.scanCancelled"); return; }

            // 构造搜索模式 "<当前路径>*"（plen = 路径部分长度，不含 '*'）
            int plen = BuildSearchPattern(_path, savedLen, _patChars, _patBuf);

            IntPtr h = Win32.FindFirstFileW(_patBuf, _findBuf);
            if (h == InvalidHandleValue)
            {
                // 无权限 / 目录已消失：计入不可读统计（非管理员运行时会有很多）
                _res.UnreadableDirs++;
                return;
            }

            bool isCache = Cat.IsCachePath(_patChars.AsSpan(0, plen));
            bool isTemp = Cat.IsTempPath(_patChars.AsSpan(0, plen));
            bool isRoot = savedLen <= PathPrefixLen + 3;   // "\?C:\"

            long dirBytes = 0, dirFiles = 0;

            try
            {
                do
                {
                    uint attr = (uint)Marshal.ReadInt32(_findBuf, OffAttributes);
                    int nameLen = FileNameLength(_findBuf);
                    if (nameLen == 0) continue;
                    CopyFileName(_findBuf, nameLen, _nameBuf);
                    // 只跳过 "." 与 ".."，不能漏掉 .gitignore 之类的点开头文件
                    if (nameLen <= 2 && _nameBuf[0] == '.') continue;

                    bool isDir = (attr & FileAttributeDirectory) != 0;

                    if (isDir)
                    {
                        if ((attr & FileAttributeReparsePoint) != 0) continue;   // 跳过联接/符号链接，防环与重复统计
                        if (savedLen + nameLen + 1 >= _path.Length)
                        {
                            _res.PathTooLongCount++;
                            _res.UnreadableDirs++;
                            continue;
                        }
                        for (int i = 0; i < nameLen; i++) _path[savedLen + i] = _nameBuf[i];
                        _path[savedLen + nameLen] = '\\';
                        _pathLen = savedLen + nameLen + 1;
                        _res.DirCount++;
                        ScanDir(ct);
                        _pathLen = savedLen;
                        continue;
                    }

                    long size = ((long)(uint)Marshal.ReadInt32(_findBuf, OffSizeHigh) << 32)
                              | (uint)Marshal.ReadInt32(_findBuf, OffSizeLow);
                    if (size < 0) size = 0;

                    _res.FileCount++;
                    _res.LogicalBytes += size;

                    var cat = Cat.ByExtension(ExtensionOf(_findBuf, OffFileName, nameLen, _extBuf));
                    _res.CatBytes[(int)cat] += size;
                    _res.CatCounts[(int)cat]++;
                    if (isCache) _res.CacheBytes += size;
                    if (isTemp) _res.TempBytes += size;

                    dirBytes += size;
                    dirFiles++;

                    // 根目录下的系统文件（hiberfil/pagefile/swapfile/MEMORY.DMP）
                    if (isRoot && IsSystemFileName(_findBuf, nameLen))
                    {
                        string nm = FileNameOf(_findBuf, nameLen);
                        _res.SysFiles.Add(new SysFileStat
                        {
                            Path = new string(_path, PathPrefixLen, savedLen - PathPrefixLen) + nm,
                            Bytes = size,
                            Flavor = SysFileStat.FlavorOf(nm),
                        });
                    }

                    // Top 大文件：只有能进榜的文件才分配路径字符串
                    if (CanEnterTop(size))
                    {
                        string full = new string(_path, PathPrefixLen, savedLen - PathPrefixLen) + FileNameOf(_findBuf, nameLen);
                        AddTop(full, size, LastWriteOf(_findBuf));
                    }
                }
                while (Win32.FindNextFileW(h, _findBuf));

                // ERROR_NO_MORE_FILES (18) is the normal end. Other errors mean
                // that this directory was only partially enumerated.
                int enumerationError = Marshal.GetLastWin32Error();
                if (enumerationError != 18)
                {
                    _res.UnreadableDirs++;
                    if (_res.Error.Length == 0)
                        _res.Error = "Directory enumeration failed (Win32 error " + enumerationError + ").";
                }

                // 当前目录的聚合落账
                if (dirBytes > 0)
                {
                    string key = KeyForCurrentDir(savedLen);
                    if (key.Length > 0)
                    {
                        if (_parts.TryGetValue(key, out var acc))
                        {
                            acc.Bytes += dirBytes;
                            acc.FileCount += dirFiles;
                        }
                        else
                        {
                            acc = new DirStat { Path = key, Level = Scanner.LevelOf(key), IsPartition = true };
                            acc.Bytes = dirBytes;
                            acc.FileCount = dirFiles;
                            _parts[key] = acc;
                        }
                    }
                }
            }
            finally
            {
                Win32.FindClose(h);
                _pathLen = savedLen;
            }
        }

        /// <summary>
        /// 当前路径是否以指定前缀开头（大小写不敏感）。
        /// 注意：_path 里带 4 字符的 "\\?\" 扩展长度前缀，比较时必须跳过它，
        /// 否则排除列表（Plain 路径）永远匹配不上。
        /// </summary>
        bool PathStartsWith(int len, string prefix)
        {
            int n = len - PathPrefixLen;              // 去掉 "\\?\" 后的显示路径长度
            if (n < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                char a = _path[PathPrefixLen + i], b = prefix[i];
                if (char.IsLetter(b)) { if (char.ToLowerInvariant(a) != char.ToLowerInvariant(b)) return false; }
                else if (a != b) return false;
            }
            // 目录边界检查：C:\Temp 不应该匹配 C:\Temp2
            if (prefix.Length > 0 && prefix[prefix.Length - 1] != '\\' && n > prefix.Length)
            {
                char next = _path[PathPrefixLen + prefix.Length];
                if (next != '\\' && next != ':') return false;
            }
            return true;
        }

        static unsafe bool IsSystemFileName(IntPtr buf, int nameLen)
        {
            var span = new ReadOnlySpan<char>((char*)(buf + OffFileName), nameLen);
            int dot = span.LastIndexOf('.');
            if (dot < 0) return false;
            int extLen = nameLen - dot - 1;
            if (extLen != 3 && extLen != 4) return false;
            var ext = span[(dot + 1)..];
            return Eq(ext, "sys") || Eq(ext, "dmp") || Eq(ext, "etl");
        }

        /// <summary>span 与字符串的大小写不敏感比较。</summary>
        static bool Eq(ReadOnlySpan<char> s, string t)
        {
            if (s.Length != t.Length) return false;
            for (int i = 0; i < s.Length; i++)
                if (char.ToLowerInvariant(s[i]) != char.ToLowerInvariant(t[i])) return false;
            return true;
        }

        /// <summary>当前目录的分区键（level &lt;= MaxDirLevel 的祖先），带前缀复用优化。</summary>
        string KeyForCurrentDir(int len)
        {
            string prefix = _keyPrefix;
            // 只有当缓存的键恰好处于聚合深度时才可复用（否则会把深层目录归到浅层祖先进去）
            if (_keyPrefixLevel == _opt.MaxDirLevel && len - PathPrefixLen > prefix.Length)
            {
                // 比较显示路径部分（跳过 "\\?\" 前缀）
                bool match = true;
                for (int i = 0; i < prefix.Length; i++)
                {
                    if (char.ToLowerInvariant(_path[PathPrefixLen + i]) != char.ToLowerInvariant(prefix[i])) { match = false; break; }
                }
                if (match && _path[PathPrefixLen + prefix.Length] == '\\') return prefix;
            }
            string dir = new string(_path, PathPrefixLen, len - PathPrefixLen).TrimEnd('\\');
            if (dir.EndsWith(":", StringComparison.Ordinal)) dir += "\\";
            string key = Scanner.TruncateToLevel(dir, _opt.MaxDirLevel);
            if (key.Length == 0) key = dir;
            _keyPrefix = key;
            _keyPrefixLevel = Scanner.LevelOf(key);
            return key;
        }

        static unsafe ReadOnlySpan<char> ExtensionOf(IntPtr buf, int off, int nameLen, Span<char> dest)
        {
            var span = new ReadOnlySpan<char>((char*)(buf + off), nameLen);
            int dot = span.LastIndexOf('.');
            if (dot <= 0 || dot >= nameLen - 1) return ReadOnlySpan<char>.Empty;
            int extLen = nameLen - dot - 1;
            if (extLen > 12) return ReadOnlySpan<char>.Empty;
            span[(dot + 1)..].CopyTo(dest);
            return dest[..extLen];
        }

        static unsafe string FileNameOf(IntPtr buf, int nameLen)
        {
            var span = new ReadOnlySpan<char>((char*)(buf + OffFileName), nameLen);
            return new string(span);
        }

        static DateTimeOffset LastWriteOf(IntPtr buf)
        {
            // FILETIME：低 32 位在前（偏移 20），高 32 位在后（偏移 24）
            long ft = ((long)(uint)Marshal.ReadInt32(buf, OffLastWrite + 4) << 32)
                    | (uint)Marshal.ReadInt32(buf, OffLastWrite);
            try { return DateTimeOffset.FromFileTime(ft); }
            catch { return DateTimeOffset.MinValue; }
        }

        /// <summary>能否进入 Top 榜单（避免为注定进不了的文件分配路径字符串）。</summary>
        bool CanEnterTop(long len) => _topCount < _top.Length || len > _top[_topCount - 1].Bytes;

        void AddTop(string path, long len, DateTimeOffset mod)
        {
            var item = new FileStat { Path = path, Bytes = len, Modified = mod };
            if (_topCount < _top.Length)
            {
                int i = _topCount++;
                while (i > 0 && _top[i - 1].Bytes < len) { _top[i] = _top[i - 1]; i--; }
                _top[i] = item;
            }
            else
            {
                int i = _top.Length - 1;
                while (i > 0 && _top[i - 1].Bytes < len) { _top[i] = _top[i - 1]; i--; }
                _top[i] = item;
            }
        }

        public void Finish()
        {
            // 祖先汇总：把分区键字节数向上累加（层级视图，基于全量分区键，保证准确）
            var anc = new Dictionary<string, DirStat>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _parts.Values)
            {
                // lv <= p.Level：包含分区键自身层级，否则目录直属文件不会被汇总进去
                for (int lv = 1; lv <= p.Level; lv++)
                {
                    string a = Scanner.TruncateToLevel(p.Path, lv);
                    if (a.Length == 0) continue;
                    if (!anc.TryGetValue(a, out var acc))
                    {
                        acc = new DirStat { Path = a, Level = lv, IsPartition = false };
                        anc[a] = acc;
                    }
                    acc.Bytes += p.Bytes;
                    acc.FileCount += p.FileCount;
                }
            }

            // 分区键只保留 ≥1MB 的，降低常驻内存与快照体积（小目录的影响可忽略）
            var all = new List<DirStat>(anc.Count + 1024);
            foreach (var kv in _parts)
                if (kv.Value.Bytes >= Snapshot.MinStoredDirBytes) all.Add(kv.Value);
            all.AddRange(anc.Values);
            all.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
            _res.Dirs = all;
            _parts.Clear();

            for (int i = 0; i < _topCount; i++) _res.TopFiles.Add(_top[i]);
        }
    }
}
