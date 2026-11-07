# C 盘容量监控（CDiskWatch）

> 只读监控 C 盘空间，定位「谁在涨、涨的是什么、能不能删」。
> 纯 Win32 P/Invoke 自绘（无 WinForms / WPF / WebView / NuGet），**不删除、不修改任何文件**。
> 界面为**浅色主题**：顶栏操作 + 空间仪表卡片 + 7 个页签 + 斑马纹表格 + 趋势图。

> 本轮做了一次完整的界面重构与代码审计，问题清单与修复记录见 [`docs/audit.md`](docs/audit.md)
> （41 项缺陷，其中 5 项是「编译不过 / 界面完全不可用」级别的硬伤）；
> 多语言按 [`docs/i18n-plan.md`](docs/i18n-plan.md) 落地：中/日/英三语 + 外部语言包 + `--report --format json`。
> 内存做过一轮实测与瘦身，实测数据见 [`docs/memory-plan.md`](docs/memory-plan.md)。

---

## 一、它解决什么问题

昨天的 C 盘空间突然暴涨，想知道**哪个文件夹涨的、属于什么类型的文件、能不能删**。

本工具把这件事拆成四层：

| 层级 | 回答的问题 | 实现 |
|---|---|---|
| ① 空间 | 现在还剩多少 | 托盘实时显示 `C: 可用 93.50 GB · 31%`（30 秒轮询） |
| ② 变化 | 相比昨天多了/少了什么 | 每日全盘快照 + 日对比（目录/类别/文件三级） |
| ③ 归因 | 属于什么类型的文件 | 15 类扩展名分类 + 类别×目录交叉 |
| ④ 建议 | 能不能删、怎么删 | 30 条规则给出**建议**（只建议，不代执行） |

**特别关注"不在文件夹里"的占用**：`hiberfil.sys`、`pagefile.sys`、卷影副本/系统还原点、MFT 与簇对齐损耗——
这些是传统磁盘监控工具看不到、却常常是暴涨真凶的部分。

---

## 二、实测数据（本机 C 盘）

| 指标 | 值 |
|---|---|
| 总容量 / 已用 / 可用 | 299.81 GB / 206.31 GB (68.8%) / **93.50 GB (31.2%)** |
| 文件总数 | 778,765 |
| 文件逻辑合计 | 209.86 GB（比实际占用多 3.55 GB → **硬链接重复计数**，WinSxS 典型） |
| 卷影副本/还原点占用 | 4.65 GB（不在任何文件夹里） |
| `C:\hiberfil.sys` | **25.48 GB**（休眠文件） |
| 全盘扫描耗时 | **12~40 秒**（取决于磁盘缓存冷热） |
| 常驻内存（托盘空闲） | **工作集 33 MB / 私有内存 12 MB** |
| 扫描后内存（自动回收后） | **工作集 14 MB / 私有内存 25 MB** |

---

## 三、界面（浅色主题，7 个页签）

界面结构（自上而下）：

```
┌ 顶栏 ── Logo C │ C 盘容量监控 / 只读监控 · 绝不删除修改 ── [数据目录][复制报告][立即扫描] ┐
├ 仪表卡 ── 可用空间 93.50 GB（按危险度变色）                 [较上次 +2.1 GB] / 时间信息  ─┤
│            总容量 299.81 GB · 剩余占比 31.2%                                            │
│            ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░  已用 206.31 GB · 68.8%    可用 93.50 GB · 31.2%      │
├ 页签 ── (概览) 目录  大文件  增量  类型  建议  历史 ─────────────────────────────────────┤
├ 内容卡 ── 表头（浅灰底）│ 斑马纹数据行 │ 行分隔线 │ 悬停高亮 + 左侧蓝条 │ 可拖动滚动条 ──┤
└ 状态栏 ── ● 状态 / 悬停行的完整路径 ─────────────────────── 快捷键提示 ────────────────┘
```

| 页签 | 内容 |
|---|---|
| **概览** | 空间仪表、本次变化、自然语言结论、未归因 / 硬链接 / 卷影副本 / 缓存、系统级占用、预警列表 |
| **目录** | 大目录按占用排序（缩进表示层级 L1~L4），进度条按**真实占比** |
| **大文件** | **Top 100 大文件**（路径/大小/修改时间），与上次快照对比标记「★ 新增」 |
| **增量** | **Top 20 增量文件夹**（Δ 排序，蓝条 = 新增目录）+ 文件类型增量 + 收缩 Top 10 + 新增大文件 |
| **类型** | 15 类文件分布（类别/大小/数量/占比/配色条） |
| **建议** | 删除建议（路径/占用/风险胶囊/命中规则 + 整行操作说明 + 合计） |
| **历史** | 近 N 次快照的剩余空间趋势图（折线 + 面积 + y 轴刻度 + 网格）+ 每日变化列表 |

交互：

| 操作 | 效果 |
|---|---|
| 单击行 | 选中（高亮 + 左侧蓝条） |
| 双击行 / `Enter` | 在资源管理器中定位该路径 |
| `↑` `↓` / `PgUp` `PgDn` / `Home` `End` | 逐行 / 翻页 / 首尾移动选中行 |
| `←` `→` | 切换页签（也可直接点页签） |
| `F5` | 立即全盘扫描 |
| 滚轮 / 拖动滚动条 / 点击空槽 | 滚动 |
| 悬停任意行 | 状态栏显示**完整路径**（长路径不再只能看到省略号） |
| 关闭窗口 | 最小化到托盘（气泡提示），单击托盘图标恢复 |

托盘：图标按占用率变色（绿/黄/橙/红），提示文本含百分比，告警走气泡通知。

---

## 四、快速使用

```bash
# 托盘常驻（首次启动会自动提权，需要管理员读取卷影副本/系统文件）
CDiskWatch.exe

# 立即全盘扫描并存快照
CDiskWatch.exe --scan

# 直接显示面板（跳过自提权，用于界面预览 / 调试）
CDiskWatch.exe --preview --no-elevate

# 打印今日报告（含目录/类别/文件类型分布 + 删除建议）
CDiskWatch.exe --report

# 与基线对比（自第一次扫描起的累计变化）
CDiskWatch.exe --report --baseline

# 结构化报告（给脚本用：JSON，含语言无关的稳定代码 id/rule/risk/level）
CDiskWatch.exe --report --format json

# 指定语言（优先级：--lang > 环境变量 CDISKWATCH_LANG > config.json 的 Lang > 跟随系统）
CDiskWatch.exe --report --lang en-US        # zh-CN / ja-JP / en-US
CDiskWatch.exe --preview --no-elevate --lang ja-JP

# 无头自检（89 项断言，含只读 IL 守卫、字体测量、排除规则、快照头、原生测量、三语一致性）
CDiskWatch.exe --selftest
```

数据目录：`%AppData%\CDiskWatch\`
- `config.json`：阈值、扫描时间、排除路径、界面语言（`Lang`: `auto` / `zh-CN` / `ja-JP` / `en-US`）
- `lang\<code>.json`：可选的外部语言包（覆盖内置文案，格式见 `docs/i18n-plan.md`）
- `history\snap-*.snap`：每日快照（GZip+二进制，约 117KB/份）
- `history\baseline.snap`：首次扫描的基线，永久保留

---

## 五、多语言（中 / 日 / 英）

界面、托盘、CLI 报告全部可切换语言，**文案不写在代码里**：

| 组成 | 说明 |
|---|---|
| `tools/lang/lang.tsv` | 唯一事实源：`key` + 中文 + 日文 + 英文，294 条 |
| `tools/gen_lang.py` | 生成 `CDiskWatch/src/i18n/Tables.g.cs`（校验占位符一致） |
| `CDiskWatch/src/i18n/Loc.cs` | `Loc.T(key)` / `Loc.F(key, args)`，缺 key 回退中文，绝不显示 key 本身 |
| `tools/check_lang.py` | 开发期守卫：key 拼错、文案表之外写死中文、占位符不一致 |

几条硬约定：

1. **句子一律用模板**（`{0}` / `{1}`），不做字符串拼接——否则英文/日文语序没法翻译；
2. **会落盘的数据不放文案**：建议的风险等级、系统文件种类改为稳定枚举，旧快照（v3）读取时按中文反查还原；
3. **排版有预算**：页签/按钮宽度按文字实测，自检会断言三种语言下都不溢出（自绘 UI 没有弹性布局兜底）；
4. 字体随语言切换：中文 `Microsoft YaHei UI`、日文 `Yu Gothic UI`、英文 `Segoe UI`；
5. 外部语言包只做覆盖，任何一条占位符不匹配会被丢弃，不会因劣质翻译崩界面。

---

## 六、只读红线如何落地

1. **源码层**：核心代码不使用 `File.Delete` / `Directory.Delete` / `File.Move` / `Directory.Move` / `File.Copy` / `File.Create`；
2. **构建期守卫**：自检用 `System.Reflection.Metadata` 扫描程序集 IL 的 `call/callvirt/newobj` 指令，
   断言**除自检模块与快照仓库外没有任何删除类调用**（白名单：`SelfTest`、`Store`，且只操作 `%AppData%\CDiskWatch`）；
   发布脚本 `build/publish.ps1` 在打包前对独立 dll 执行该守卫，不通过即终止发布；
3. **运行时**：不申请 SeBackupPrivilege / SeTakeOwnershipPrivilege；子进程只允许只读查询
   （`vssadmin list shadowstorage`、`DISM /AnalyzeComponentStore`）；
4. **UI 层**：所有"清理"文案均标注「本软件不会自动删除任何文件」，命令一律交由用户自行执行。

---

## 七、工程要点

- **零 WinForms / WPF / WebView / System.Drawing / NuGet**：全部 Win32 P/Invoke + GDI 自绘；
- **扫描器用原生 `FindFirstFile/FindNextFile`**（不走 `FileSystemEnumerable`）：框架枚举器每文件约 80 字节托管分配，
  70 万文件就是 70 MB 垃圾；原生实现**每文件零分配**，路径/查找缓冲全部复用；
- **目录聚合 = 非重叠分区 + 祖先汇总**：分区键取文件的第 4 级祖先（每个文件只计一次，键之间不重复），
  扫描后再向上累加出层级视图；内存 ~2 MB；
- **快照只存 ≥1MB 的目录条目**，单份 117 KB，历史仓库上限 300 MB（自动淘汰最旧）；
- **内存治理**：`System.GC.ConserveMemory=9` + 扫描后压缩回收 + 扫描期定期 gen0 回收；
- **Win32 入口点自检**：自检里用 `GetProcAddress` 校验 6 个 DLL 的 80+ 个导出函数存在，
  防止 P/Invoke 挂错 DLL（开发期确实踩过 `DrawTextW`/`FillRect` 挂到 gdi32 的坑）。

---

## 八、目录结构

```
C盘容量监控/
├─ CDiskWatch/                 单 exe 工程
│  ├─ CDiskWatch.csproj
│  ├─ app.manifest             清单（注意 SxS 命名空间必须是 urn:schemas-microsoft-com:asm.v1）
│  ├─ app.ico + icon-*.ico     应用图标与托盘状态图标（绿/黄/橙/红）
│  └─ src/
│     ├─ Program.cs            入口：单实例 / 自提权 / 消息循环 / CLI 子命令
│     ├─ Win32.cs              全部 P/Invoke 与常量
│     ├─ Theme.cs              浅色配色 + GDI 绘制工具（字体/画刷缓存）
│     ├─ MainWindow.cs         7 页签自绘面板（布局与命中测试共用同一套矩形）
│     ├─ Tray.cs               托盘图标 / 气泡 / 右键菜单
│     ├─ Monitor.cs            调度：空间探针 + 每日扫描 + 派生数据
│     ├─ Scanner.cs            原生全盘扫描器（零分配）
│     ├─ Categories.cs         15 类文件分类器
│     ├─ SystemUsage.cs        系统级占用（卷影副本 / WinSxS 分析）
│     ├─ Snapshot.cs / Store.cs 快照模型与仓库
│     ├─ Diff.cs               日对比 + 结论生成
│     ├─ Alerts.cs             预警引擎
│     ├─ Advice.cs             删除建议规则引擎
│     ├─ Report.cs             文本报告 / JSON 报告（--format json）
│     ├─ ReportJson.cs         结构化报告 DTO（稳定代码 + 本地化文本）
│     ├─ i18n/Loc.cs           多语言门面（T/F + 外部语言包 + 字体与日期格式）
│     ├─ i18n/Tables.g.cs      生成的文案表（zh-CN / ja-JP / en-US，勿手改）
│     └─ SelfTest.cs           89 项无头自检（含只读 IL 守卫 + 渲染断言 + 三语一致性）
├─ build/publish.ps1           发布脚本（构建→dll 自检→单文件发布→产物冒烟）
├─ tools/make_icon.py          图标生成（Pillow，离线）
├─ tools/shot.ps1              窗口截图（PrintWindow）
├─ tools/memprobe.ps1          内存/句柄分阶段采样
├─ tools/wsprobe.ps1           工作集归还效果验证
├─ tools/lang/lang.tsv         多语言文案唯一事实源（key + 中/日/英）
├─ tools/gen_lang.py           由 tsv 生成 Tables.g.cs
├─ tools/check_lang.py         多语言守卫（key 拼错 / 写死中文 / 占位符）
├─ tools/langshots.ps1         多语言界面截图（←/→ 遍历 7 页）
├─ publish/win-x64/            发布产物（CDiskWatch.exe 12 MB）
├─ docs/plan.md                完整技术方案
├─ docs/audit.md               代码审计 + UI 重构问题清单（31 项）
└─ docs/memory-plan.md         内存审计与瘦身方案（含实测数据与收益估算）
```

---

## 九、已知限制

- **目录归属到第 4 级**：`C:\Users\X\AppData\Local\Temp` 的内容记在 `C:\Users\X\AppData\Local`，
  深度更细的目录需要在「建议」页看（建议引擎对规则目标目录做**精确测量**，不受此限制）；
- **硬链接重复计数**：WinSxS 与 System32 存在硬链接，逻辑合计可能大于实际占用（报告里会明确标注多出多少）；
- **联接/符号链接被跳过**（防止环路与重复统计），OneDrive 占位文件因此不计入；
- **NTFS 扩展长度前缀**：路径超过 260 字符的目录用 `\\?\` 访问，正常可用；
- **非管理员运行**：`System Volume Information` 等受保护目录读不到，会体现为「未归因占用」偏高，
  快照里暂未记录本次是否提权，跳权限对比会有噪声；
- **自绘 UI 未实现 UIA**（`WM_GETOBJECT`）：屏幕阅读器等无障碍工具读不到表格内容。
