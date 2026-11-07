using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace CDiskWatch;

/// <summary>无头自检：--selftest 运行，结果写 stdout 与 selftest-report.txt。</summary>
public static class SelfTest
{
    static readonly StringBuilder Report = new();
    static int _pass, _fail;

    static void Check(bool cond, string name, string detail = "")
    {
        string line = (cond ? "[PASS] " : "[FAIL] ") + name + (detail.Length > 0 ? "  — " + detail : "");
        if (cond) _pass++; else _fail++;
        Report.AppendLine(line);
        NativeOut.WriteLine(line);
    }

    public static int Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            RunAll();
        }
        catch (Exception ex)
        {
            Check(false, "自检过程未抛异常", ex.ToString());
        }
        sw.Stop();

        string summary = $"\n自检完成：{_pass} 项通过，{_fail} 项失败，用时 {sw.ElapsedMilliseconds}ms";
        Report.AppendLine(summary);
        NativeOut.WriteLine(summary);
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-report.txt"), Report.ToString(), Encoding.UTF8);
        }
        catch { }
        return _fail == 0 ? 0 : 1;
    }

    static void RunAll()
    {
        // ===== 分类器 =====
        Check(Cat.ByExtension(".mp4") == FileCategory.Video, "分类：mp4 → 视频");
        Check(Cat.ByExtension("mkv") == FileCategory.Video, "分类：mkv → 视频");
        Check(Cat.ByExtension(".JPG") == FileCategory.Image, "分类：JPG（大小写）→ 图片");
        Check(Cat.ByExtension(".zip") == FileCategory.Archive, "分类：zip → 压缩包");
        Check(Cat.ByExtension(".exe") == FileCategory.Installer, "分类：exe → 安装包");
        Check(Cat.ByExtension(".log") == FileCategory.Log, "分类：log → 日志");
        Check(Cat.ByExtension(".dmp") == FileCategory.Log, "分类：dmp → 日志/转储");
        Check(Cat.ByExtension(".docx") == FileCategory.Document, "分类：docx → 文档");
        Check(Cat.ByExtension(".vhdx") == FileCategory.DiskImage, "分类：vhdx → 磁盘镜像");
        Check(Cat.ByExtension(".sys") == FileCategory.SysFile, "分类：sys → 系统文件");
        Check(Cat.ByExtension(".wav") == FileCategory.Audio, "分类：wav → 音频（历史上因分桶写错而漏判）");
        Check(Cat.ByExtension(".webm") == FileCategory.Video, "分类：webm → 视频");
        Check(Cat.ByExtension(".svg") == FileCategory.Image, "分类：svg → 图片");
        Check(Cat.ByExtension(".tgz") == FileCategory.Archive, "分类：tgz → 压缩包");
        Check(Cat.ByExtension(".sqlite3") == FileCategory.Database, "分类：sqlite3 → 数据库");
        Check(Cat.ByExtension(".xyz") == FileCategory.Other, "分类：未知扩展 → 其他");
        Check(Cat.ByExtension("") == FileCategory.Other, "分类：空扩展 → 其他");
        Check(Cat.IsCachePath("C:\\Users\\a\\AppData\\Local\\Google\\Chrome\\Cache"), "路径关键词：Cache 命中");
        Check(Cat.IsTempPath("C:\\Users\\a\\AppData\\Local\\Temp"), "路径关键词：Temp 命中");

        // ===== 层级与截断 =====
        Check(Scanner.LevelOf("C:\\") == 0, "层级：根目录 = 0");
        Check(Scanner.LevelOf("C:\\Users") == 1, "层级：一级目录 = 1");
        Check(Scanner.LevelOf("C:\\Users\\Bob\\AppData") == 3, "层级：三级目录 = 3");
        Check(Scanner.TruncateToLevel("C:\\a\\b\\c\\d\\e", 4) == "C:\\a\\b\\c\\d", "截断：保留到第 4 层");
        Check(Scanner.TruncateToLevel("C:\\a\\b", 4) == "C:\\a\\b", "截断：层级不足时保持原样");

        // ===== 格式化 =====
        Check(Fmt.Bytes(1L << 30) == "1.00 GB", "格式化：1GB");
        Check(Fmt.Bytes(-(1L << 20)) == "-1.00 MB", "格式化：负值");

        // ===== 扫描完整性：不完整结果不能进入快照历史 =====
        var scanQuality = new ScanResult();
        Check(scanQuality.IsComplete, "扫描质量：无错误结果视为完整");
        scanQuality.UnreadableDirs = 1;
        Check(!scanQuality.IsComplete, "扫描质量：不可读目录使结果不完整");
        scanQuality.UnreadableDirs = 0;
        scanQuality.PathTooLongCount = 1;
        Check(!scanQuality.IsComplete, "扫描质量：超长路径使结果不完整");
        scanQuality.PathTooLongCount = 0;
        scanQuality.Error = "enumeration failed";
        Check(!scanQuality.IsComplete, "扫描质量：根级错误使结果不完整");
        Check(Fmt.Pct(0.315) == "31.5%", "格式化：百分比");

        // ===== 快照往返 =====
        string tmpDir = Path.Combine(Path.GetTempPath(), "cdw-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        try
        {
            var snap = new Snapshot
            {
                Time = DateTimeOffset.Now,
                TotalBytes = 100L << 30, FreeBytes = 40L << 30, UsedBytes = 60L << 30,
                LogicalBytes = 55L << 30, UnaccountedBytes = 5L << 30,
                FileCount = 1234, Drive = "C",
            };
            snap.CatBytes[(int)FileCategory.Video] = 12L << 30;
            snap.CatCounts[(int)FileCategory.Video] = 7;
            snap.Dirs.Add(new DirStat { Path = "C:\\Users\\Bob\\Videos", Bytes = 12L << 30, FileCount = 7, Level = 3, IsPartition = true });
            snap.TopFiles.Add(new FileStat { Path = "C:\\Users\\Bob\\Videos\\a.mp4", Bytes = 12L << 30, Modified = DateTimeOffset.Now });
            snap.SysFiles.Add(new SysFileStat { Path = "C:\\hiberfil.sys", Bytes = 25L << 30, Flavor = SysFlavor.Hiberfil });
            string sp = Path.Combine(tmpDir, "t.snap");
            snap.Save(sp);
            var back = Snapshot.Load(sp);
            Check(back != null, "快照：可保存并读取");
            if (back != null)
            {
                Check(back.UsedBytes == 60L << 30, "快照：已用字节一致");
                Check(back.CatBytes[(int)FileCategory.Video] == 12L << 30, "快照：类别字节一致");
                Check(back.Dirs.Count == 1 && back.Dirs[0].Path == "C:\\Users\\Bob\\Videos", "快照：目录条目一致");
                Check(back.TopFiles.Count == 1 && back.TopFiles[0].Bytes == 12L << 30, "快照：大文件条目一致");
                Check(back.SysFiles.Count == 1 && back.SysFiles[0].Flavor == SysFlavor.Hiberfil, "快照：系统文件条目一致");
                Check(back.Rollups.Count > 0, "快照：祖先汇总已重算");

                // 轻量头：历史页只读这个（否则每份完整快照要占 ~1.3MB 内存）
                var head = SnapHeader.Read(sp);
                Check(head != null, "快照头：可只解析头部");
                if (head != null)
                {
                    Check(head.UsedBytes == 60L << 30, "快照头：已用字节一致");
                    Check(head.FreeBytes == 40L << 30, "快照头：可用字节一致");
                    Check(head.Time == snap.Time, "快照头：时间一致");
                    Check(head.DirCount == 0 && head.FileCount == 1234, "快照头：计数一致");
                }

                // 祖先汇总必须包含「分区键自身所在层级」，否则目录直属文件会被漏掉
                var rs = new Snapshot();
                rs.Dirs.Add(new DirStat { Path = @"C:\Windows", Bytes = 100, Level = 1, IsPartition = true });
                rs.Dirs.Add(new DirStat { Path = @"C:\Windows\WinSxS", Bytes = 200, Level = 2, IsPartition = true });
                rs.ComputeRollups();
                var win = rs.Rollups.FirstOrDefault(x => x.Path.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase));
                Check(win != null && win.Bytes == 300, "快照：祖先汇总含本级直属文件", win == null ? "未生成" : @"C:\Windows=" + win.Bytes);
            }
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }

        // ===== 扫描器（临时目录） =====
        string scanRoot = Path.Combine(Path.GetTempPath(), "cdw-scan-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(scanRoot, "sub", "deep"));
            File.WriteAllBytes(Path.Combine(scanRoot, "a.mp4"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(scanRoot, "sub", "b.txt"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(scanRoot, "sub", "deep", "c.mp4"), new byte[4096]);
            var r = Scanner.Scan(new ScanOptions { Drive = "C", RootPath = scanRoot }, CancellationToken.None);
            Check(r.FileCount == 3, "扫描：文件数正确", "实际 " + r.FileCount);
            Check(r.LogicalBytes == 1024 + 2048 + 4096, "扫描：逻辑大小正确", "实际 " + r.LogicalBytes);
            Check(r.CatBytes[(int)FileCategory.Video] == 1024 + 4096, "扫描：视频类别字节正确");
            Check(r.Dirs.Count > 0 && r.Dirs[0].Bytes == 1024 + 2048 + 4096, "扫描：目录聚合正确");
            Check(r.TopFiles.Count == 3 && r.TopFiles[0].Bytes == 4096, "扫描：Top 大文件降序正确");

            // 排除路径：历史上因为缓冲区带 "\?\" 前缀，排除列表永远匹配不上
            var r2 = Scanner.Scan(new ScanOptions
            {
                Drive = "C",
                RootPath = scanRoot,
                ExcludePrefixes = new List<string> { Path.Combine(scanRoot, "sub") },
            }, CancellationToken.None);
            Check(r2.FileCount == 1 && r2.LogicalBytes == 1024, "扫描：排除路径生效", $"文件数={r2.FileCount} 字节={r2.LogicalBytes}");
            Check(r2.ExcludedDirs >= 1, "扫描：排除目录被统计");
        }
        finally { try { Directory.Delete(scanRoot, true); } catch { } }

        // ===== Diff =====
        var a = new Snapshot { Time = DateTimeOffset.Now.AddDays(-1), UsedBytes = 100L << 30, FreeBytes = 50L << 30, TotalBytes = 150L << 30 };
        var b = new Snapshot { Time = DateTimeOffset.Now, UsedBytes = 120L << 30, FreeBytes = 30L << 30, TotalBytes = 150L << 30 };
        a.Dirs.Add(new DirStat { Path = "C:\\Users\\Bob\\Videos", Bytes = 10L << 30, Level = 3, IsPartition = true });
        b.Dirs.Add(new DirStat { Path = "C:\\Users\\Bob\\Videos", Bytes = 25L << 30, Level = 3, IsPartition = true });
        b.Dirs.Add(new DirStat { Path = "C:\\Temp", Bytes = 5L << 30, Level = 1, IsPartition = true });
        a.CatBytes[(int)FileCategory.Video] = 10L << 30;
        b.CatBytes[(int)FileCategory.Video] = 25L << 30;
        var d = DiffEngine.Compare(a, b);
        Check(d.UsedDelta == 20L << 30, "对比：已用差值正确");
        Check(d.Growing.Count >= 1 && d.Growing[0].Path == "C:\\Users\\Bob\\Videos" && d.Growing[0].Delta == 15L << 30, "对比：增量文件夹排序正确");
        Check(d.Growing.Any(x => x.IsNew && x.Path == "C:\\Temp"), "对比：新增目录被标记");
        Check(d.Cats.First(x => x.Index == (int)FileCategory.Video).Delta == 15L << 30, "对比：类别差值正确");
        Check(d.Verdict.Length > 0, "对比：结论文本生成", d.Verdict);
        // 文案方向与语言无关：比较「增长」与「减少」两种模板的前缀
        string grewPrefix = LongestFragment(Loc.T("verdict.headGrew"));
        string shrankPrefix = LongestFragment(Loc.T("verdict.headShrank"));
        Check(d.UsedDelta > 0 && d.Verdict.Contains(grewPrefix) && !d.Verdict.Contains(shrankPrefix),
            "对比：结论文案方向正确（已用变多 = 空间变少）", d.Verdict);

        // ===== 建议引擎 =====
        var sn = new Snapshot { Time = DateTimeOffset.Now };
        string localTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        sn.Dirs.Add(new DirStat { Path = localTemp, Bytes = 3L << 30, Level = Scanner.LevelOf(localTemp), IsPartition = true });
        sn.SysFiles.Add(new SysFileStat { Path = "C:\\hiberfil.sys", Bytes = 25L << 30, Flavor = SysFlavor.SystemFile });
        var cards = AdviceEngine.Evaluate(sn);
        Check(AdviceEngine.Rules.Any(r => r.Id == "temp-user"), "建议：规则表包含用户临时目录");
        Check(cards.Any(c => c.Path.Contains("Temp") && c.Risk == RiskLevel.Low), "建议：命中临时目录规则");
        Check(cards.Any(c => c.Path.EndsWith("hiberfil.sys")), "建议：命中休眠文件规则");
        Check(cards.All(c => c.Tip.Length > 0), "建议：每条都有操作说明");
        Check(AdviceEngine.WildcardMatch("Default", "*"), "建议：通配符 * 匹配任意");
        Check(AdviceEngine.WildcardMatch("Cache2", "Cache?"), "建议：通配符 ? 匹配单字符");
        Check(!AdviceEngine.WildcardMatch("Cache", "Cache2"), "建议：通配符不匹配时返回 false");

        // 精确测量：在临时目录里造一个文件，测量结果应包含它
        string mDir = Path.Combine(Path.GetTempPath(), "cdw-measure-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(mDir);
            File.WriteAllBytes(Path.Combine(mDir, "x.bin"), new byte[4096]);
            long measured = AdviceEngine.MeasureDir(mDir);
            Check(measured >= 4096, "建议：目录精确测量可用", "实际 " + measured);
            long native = Scanner.MeasureTree(mDir, out int unreadable);
            Check(native == measured, "测量：原生遍历与旧实现结果一致", $"原生={native} 旧={measured} 不可读={unreadable}");
        }
        finally { try { Directory.Delete(mDir, true); } catch { } }

        // ===== 预警 =====
        var settings = new AlertSettings { FreeWarnBytes = 20L << 30, FreeCriticalBytes = 10L << 30 };
        var low = new Snapshot { Time = DateTimeOffset.Now, FreeBytes = 5L << 30, TotalBytes = 100L << 30, UsedBytes = 95L << 30 };
        var alerts = AlertEngine.Evaluate(low, null, settings, useCooldown: false);
        Check(alerts.Any(x => x.Level == AlertLevel.Critical), "预警：剩余空间过低触发严重级别");

        // ===== 只读守卫 =====
        // 单文件发布时程序集已内嵌到 exe，无法读取 PE 元数据；
        // 此时由发布流程（build/publish.ps1）在打包前对独立 dll 执行同一守卫。
        string dllPath = Path.Combine(AppContext.BaseDirectory, "CDiskWatch.dll");
        if (File.Exists(dllPath))
        {
            Check(!HasForbiddenDeleteApi(dllPath), "只读守卫：除自检/快照仓库外，程序集未调用任何删除类 API", DescribeForbidden());
        }
        else
        {
            Check(true, "只读守卫：单文件发布（程序集已内嵌，守卫由发布流程对独立 dll 执行）");
        }
        Check(Store.HistoryDir.Contains("CDiskWatch"), "只读守卫：删除操作仅限自身数据目录", Store.HistoryDir);

        // ===== 文本度量（面板布局完全依赖它，量错会导致按钮文字被省略号截断）=====
        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        IntPtr testFont = Theme.CreateUiFont(14, 400);
        try
        {
            int w1 = Theme.Measure(screenDc, testFont, "数据目录");
            int w2 = Theme.Measure(screenDc, testFont, "立即扫描");
            Check(testFont != IntPtr.Zero, "渲染：可创建中文字体");
            Check(w1 > 40 && w2 > 40, "渲染：中文文本测量有意义", $"数据目录={w1}px 立即扫描={w2}px");

            // 汉字"墨迹宽度"不能超过它的字宽（advance），否则相邻字会互相压叠
            long ink = InkWidth(screenDc, testFont, "动");
            Win32.GetTextExtentPoint32W(screenDc, "动", 1, out var advSz);
            Check(ink > 0 && ink <= advSz.X + 1, "渲染：汉字字形不溢出字宽（不叠字）", $"墨迹={ink}px 字宽={advSz.X}px");
        }
        finally
        {
            if (testFont != IntPtr.Zero) Win32.DeleteObject(testFont);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // ===== 多语言 =====
        string homeLang = Loc.Code;
        var zhMap = ToMap(Tables.ZhCn);
        var jaMap = ToMap(Tables.JaJp);
        var enMap = ToMap(Tables.EnUs);
        Check(Tables.Codes.Length == 3 && zhMap.Count > 200, "多语言：内置 3 种语言", string.Join(" / ", Tables.Codes) + $" 共 {zhMap.Count} 条");

        var keyDiffJa = zhMap.Keys.Except(jaMap.Keys).Concat(jaMap.Keys.Except(zhMap.Keys)).ToList();
        var keyDiffEn = zhMap.Keys.Except(enMap.Keys).Concat(enMap.Keys.Except(zhMap.Keys)).ToList();
        Check(keyDiffJa.Count == 0, "多语言：日文 key 集合与中文一致", string.Join(",", keyDiffJa.Take(8)));
        Check(keyDiffEn.Count == 0, "多语言：英文 key 集合与中文一致", string.Join(",", keyDiffEn.Take(8)));

        var badPlaceholder = new List<string>();
        var emptyText = new List<string>();
        foreach (var kv in zhMap)
        {
            var p0 = LangPack.Placeholders(kv.Value);
            if (!jaMap.ContainsKey(kv.Key) || !enMap.ContainsKey(kv.Key)) continue;
            if (!p0.SetEquals(LangPack.Placeholders(jaMap[kv.Key]))) badPlaceholder.Add("ja:" + kv.Key);
            if (!p0.SetEquals(LangPack.Placeholders(enMap[kv.Key]))) badPlaceholder.Add("en:" + kv.Key);
            if (string.IsNullOrWhiteSpace(jaMap[kv.Key]) || string.IsNullOrWhiteSpace(enMap[kv.Key])) emptyText.Add(kv.Key);
        }
        Check(badPlaceholder.Count == 0, "多语言：占位符一一对应（不会 FormatException）", string.Join(",", badPlaceholder.Take(8)));
        Check(emptyText.Count == 0, "多语言：无空译文", string.Join(",", emptyText.Take(8)));

        // 每种语言都要能取到全部文案（T 不能把 key 本身吐出来，F 不能抛异常）
        int unresolved = 0;
        string firstUnresolved = "";
        foreach (string code in Tables.Codes)
        {
            Loc.Load(code);
            foreach (var kv in zhMap)
            {
                string v = Loc.T(kv.Key);
                if (v.Length == 0 || v == kv.Key)
                {
                    unresolved++;
                    if (firstUnresolved.Length == 0) firstUnresolved = code + ":" + kv.Key;
                }
                var ph = LangPack.Placeholders(kv.Value);
                int maxPh = ph.Count == 0 ? 0 : ph.Max();
                var args = new object[maxPh + 1];
                for (int i = 0; i < args.Length; i++) args[i] = i + 1;
                string f = Loc.F(kv.Key, args);
                if (f.IndexOf('{') >= 0)
                {
                    unresolved++;
                    if (firstUnresolved.Length == 0) firstUnresolved = code + ":format:" + kv.Key;
                }
            }
        }
        Check(unresolved == 0, "多语言：三种语言全部 key 均可解析且参数格式正确", firstUnresolved);

        // 排版预算：页签 / 顶栏按钮在每种语言下都必须放得下（自绘 UI 没有弹性布局，靠这个兜底）
        var overflow = new List<string>();
        string[] tabKeys = { "tab.overview", "tab.dirs", "tab.files", "tab.delta", "tab.cats", "tab.advice", "tab.history" };
        string[] actKeys = { "action.dataDir", "action.copyReport", "action.rescan" };
        foreach (string code in Tables.Codes)
        {
            Loc.Load(code);
            IntPtr lf = Theme.CreateUiFont(15, 600, Loc.FontFace);
            IntPtr dc = Win32.GetDC(IntPtr.Zero);
            try
            {
                int tabs = 0;
                foreach (var k in tabKeys) tabs += Theme.Measure(dc, lf, Loc.T(k)) + 30 + 6;
                int acts = 0;
                foreach (var k in actKeys) acts += Theme.Measure(dc, lf, Loc.T(k)) + 38 + 8;
                if (tabs > 1140) overflow.Add($"{code}:tabs={tabs}px");
                if (acts > 620) overflow.Add($"{code}:actions={acts}px");
            }
            finally
            {
                if (lf != IntPtr.Zero) Win32.DeleteObject(lf);
                Win32.ReleaseDC(IntPtr.Zero, dc);
            }
        }
        Check(overflow.Count == 0, "多语言：页签/按钮在每种语言下不溢出（排版预算）", string.Join(" ", overflow));

        // 语言切换的基础设施
        Loc.Load("ja-JP");
        Check(Loc.Code == Loc.JaJp && Loc.FontFace != "Microsoft YaHei UI", "多语言：日文使用日文字体", Loc.FontFace);
        Loc.Load("en-US");
        Check(Loc.Code == Loc.EnUs && Loc.FontFace.Contains("Segoe"), "多语言：英文使用 Segoe UI", Loc.FontFace);
        Check(Loc.DateShortPattern.StartsWith("yyyy"), "多语言：英文日期带年份", Loc.DateShortPattern);
        Loc.Load("auto");
        Check(Tables.Codes.Contains(Loc.Code), "多语言：auto 解析到内置语言", Loc.Code + "（系统 " + Loc.DetectSystem() + "）");

        // 旧快照（v3，中文文案）迁移
        Check(AdviceEngine.IdFromLegacyName("用户临时目录") == "temp-user", "多语言：旧快照规则名可反查为稳定 Id");
        Check(AdviceEngine.RiskFromLegacy("高") == RiskLevel.High && AdviceEngine.RiskFromLegacy("中") == RiskLevel.Mid,
            "多语言：旧快照风险等级可映射为枚举");
        Check(SysFileStat.FlavorOf("hiberfil.sys") == SysFlavor.Hiberfil && SysFileStat.FlavorOf("MEMORY.DMP") == SysFlavor.MemoryDump,
            "多语言：系统文件种类按文件名还原");

        Loc.Load(homeLang);

        // ===== 应用图标 =====
        IntPtr iconH = AppIcon.Load();
        Check(iconH != IntPtr.Zero, "应用图标：可加载",
            $"dir={AppIcon.AppDir} module={AppIcon.LoadFromModule()} file={AppIcon.LoadFromFile(Path.Combine(AppIcon.AppDir, "app.ico"))}");

        // ===== Win32 入口点自检（防止把 API 挂错 DLL） =====
        foreach (var (dll, funcs) in Win32EntryPoints)
        {
            var missing = new List<string>();
            foreach (var f in funcs)
                if (!Win32.HasEntryPoint(dll, f)) missing.Add(f);
            Check(missing.Count == 0, $"Win32 入口点：{dll}", missing.Count == 0 ? "" : "缺少 " + string.Join(", ", missing));
        }
    }

    /// <summary>模板里最长的一段固定文字（用于跨语言判断文案方向，避免依赖某种语言的措辞）。</summary>
    static string LongestFragment(string template)
    {
        string best = "";
        var sb = new StringBuilder();
        foreach (char ch in template + "{")
        {
            if (ch == '{' || ch == '}')
            {
                if (sb.Length > best.Length) best = sb.ToString();
                sb.Clear();
            }
            else sb.Append(ch);
        }
        return best.Trim().TrimEnd('（', '(');
    }

    /// <summary>把生成表（key,value 交替的扁平数组）转成字典。</summary>
    static Dictionary<string, string> ToMap(string[] pairs)
    {
        var d = new Dictionary<string, string>(pairs.Length / 2, StringComparer.Ordinal);
        for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }

    /// <summary>把单个字符画到内存 DC 上，返回墨迹（非白像素）宽度，用于检测叠字。</summary>
    static int InkWidth(IntPtr refDc, IntPtr font, string text)
    {
        IntPtr mem = Win32.CreateCompatibleDC(refDc);
        IntPtr bmp = Win32.CreateCompatibleBitmap(refDc, 96, 48);
        if (mem == IntPtr.Zero || bmp == IntPtr.Zero) return 0;
        IntPtr oldBmp = Win32.SelectObject(mem, bmp);
        IntPtr oldFont = Win32.SelectObject(mem, font);
        try
        {
            var rc = new Win32.RECT { L = 0, T = 0, R = 96, B = 48 };
            IntPtr white = Win32.CreateSolidBrush(0x00FFFFFF);
            Win32.FillRect(mem, ref rc, white);
            Win32.DeleteObject(white);
            Win32.SetBkMode(mem, Win32.TRANSPARENT);
            Win32.SetTextColor(mem, 0);
            Win32.DrawTextW(mem, text, -1, ref rc, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_VCENTER | Win32.DT_NOPREFIX);
            int minX = int.MaxValue, maxX = -1;
            for (int x = 0; x < 96; x++)
                for (int y = 0; y < 48; y++)
                {
                    uint px = Win32.GetPixel(mem, x, y);
                    if (px != 0x00FFFFFF && px != 0xFFFFFFFF) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
                }
            return maxX >= minX ? maxX - minX + 1 : 0;
        }
        finally
        {
            Win32.SelectObject(mem, oldFont);
            Win32.SelectObject(mem, oldBmp);
            Win32.DeleteObject(bmp);
            Win32.DeleteDC(mem);
        }
    }

    /// <summary>需要校验的 Win32 入口点（dll → 函数名列表）。</summary>
    static readonly (string dll, string[] funcs)[] Win32EntryPoints =
    {
        ("user32.dll", new[] { "DrawTextW", "FillRect", "CreateWindowExW", "RegisterClassW", "DefWindowProcW",
            "GetMessageW", "TranslateMessage", "DispatchMessageW", "BeginPaint", "EndPaint", "InvalidateRect",
            "GetClientRect", "SetWindowPos", "ShowWindow", "SetForegroundWindow",
            "CreatePopupMenu", "AppendMenuW", "TrackPopupMenu", "DestroyMenu", "LoadImageW", "CreateIconIndirect",
            "MonitorFromPoint", "GetMonitorInfoW", "LoadCursorW",
            "TrackMouseEvent", "GetCursorPos", "SetCursor", "GetAsyncKeyState", "PostMessageW", "SetTimer",
            "KillTimer", "PostQuitMessage", "DestroyWindow", "IsWindowVisible", "GetWindowThreadProcessId",
            "AttachThreadInput", "GetForegroundWindow", "OpenClipboard",
            "EmptyClipboard", "SetClipboardData", "CloseClipboard", "SetWindowTextW" }),
        ("gdi32.dll", new[] { "CreateCompatibleDC", "DeleteDC", "CreateCompatibleBitmap", "SelectObject", "DeleteObject",
            "BitBlt", "CreateSolidBrush", "CreatePen", "GetStockObject", "SetBkMode", "SetTextColor", "SetTextAlign",
            "Ellipse", "Pie", "MoveToEx", "LineTo", "Polygon", "CreateFontW", "CreateRoundRectRgn", "FillRgn",
            "SelectClipRgn", "SaveDC", "RestoreDC", "GetDeviceCaps", "Rectangle" }),
        ("kernel32.dll", new[] { "GetModuleHandleW", "GetModuleFileNameW", "GetDiskFreeSpaceExW", "GetStdHandle",
            "WriteFile", "GlobalAlloc", "GlobalLock", "GlobalUnlock", "GlobalFree", "WaitForSingleObject",
            "CloseHandle", "GetCurrentThreadId" }),
        ("shell32.dll", new[] { "Shell_NotifyIconW", "SHGetFileInfoW", "ShellExecuteExW" }),
        ("msimg32.dll", new[] { "GradientFill" }),
        ("dwmapi.dll", new[] { "DwmSetWindowAttribute" }),
    };

    /// <summary>扫描程序集元数据，确认没有调用任何"删除/覆盖"类 API。</summary>
    static bool HasForbiddenDeleteApi(string assemblyPath)
    {
        var found = ScanForbiddenCallSites(assemblyPath);
        return found.Count > 0;
    }

    static readonly List<string> _forbiddenFound = new();
    static string DescribeForbidden() => _forbiddenFound.Count == 0 ? "" : string.Join(" | ", _forbiddenFound);

    static readonly string[] ForbiddenTypes = { "System.IO.File", "System.IO.Directory" };
    static readonly string[] ForbiddenNames = { "Delete", "Copy", "Create", "CreateText", "CreateTextWriter" };

    /// <summary>允许调用删除 API 的类型（只能操作程序自己的数据目录，不得触碰用户文件）。</summary>
    static readonly string[] AllowedDeleteTypes = { "SelfTest", "Store" };

    /// <summary>
    /// 精确到调用点：逐个方法体查找对禁止 API 的调用（call / callvirt / newobj 操作码）。
    /// 自检模块允许调用 Directory.Delete（仅用于清理自己创建的临时目录）。
    /// </summary>
    static List<string> ScanForbiddenCallSites(string assemblyPath)
    {
        _forbiddenFound.Clear();
        try
        {
            string path = assemblyPath;
            if (!File.Exists(path)) return _forbiddenFound;
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return _forbiddenFound;
            var md = pe.GetMetadataReader();

            var forbidden = new Dictionary<int, string>();
            foreach (var handle in md.MemberReferences)
            {
                var mr = md.GetMemberReference(handle);
                string name = md.GetString(mr.Name);
                if (mr.Parent.Kind != HandleKind.TypeReference) continue;
                var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                string full = md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
                if (!ForbiddenTypes.Contains(full)) continue;
                if (!ForbiddenNames.Contains(name)) continue;
                forbidden[System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle)] = full + "::" + name;
            }
            if (forbidden.Count == 0) return _forbiddenFound;

            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                string typeName = md.GetString(td.Name);
                foreach (var mh in td.GetMethods())
                {
                    var mdef = md.GetMethodDefinition(mh);
                    if (mdef.RelativeVirtualAddress == 0) continue;
                    var body = pe.GetMethodBody(mdef.RelativeVirtualAddress);
                    var il = body.GetILBytes();
                    if (il == null || il.Length < 5) continue;
                    for (int i = 1; i + 4 <= il.Length; i++)
                    {
                        byte op = il[i - 1];
                        if (op != 0x28 && op != 0x6F && op != 0x73) continue; // call / callvirt / newobj
                        int tok = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
                        if (!forbidden.TryGetValue(tok, out var what)) continue;
                        string site = typeName + "::" + md.GetString(mdef.Name) + " → " + what;
                        // 白名单：SelfTest 清理自己的临时目录；Store 删除自己数据目录下的历史快照
                        if (AllowedDeleteTypes.Contains(typeName)) continue;
                        _forbiddenFound.Add(site);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _forbiddenFound.Add("扫描异常: " + ex.Message);
        }
        return _forbiddenFound;
    }
}
