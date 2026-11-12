# AGENTS.md — C 盘容量监控（CDiskWatch）

只读监控 C 盘的 Windows 桌面工具：`CDiskWatch.exe`（托盘常驻 + 8 页签自绘面板 + CLI 报告）。
.NET 8 / `net8.0-windows` / WinExe / 单文件发布。**所有中文文档与注释都保持中文。**

## 红线（改代码前必读）

1. **绝不删除 / 移动 / 修改 C 盘上任何用户文件**。核心代码不得出现 `File.Delete` / `Directory.Delete` /
   `File.Move` / `Directory.Move` / `File.Copy` / `File.Create`；
   唯一允许删除的是 `%AppData%\CDiskWatch` 下自己的快照（白名单：`SelfTest`、`Store`）。
   `SelfTest` 里有一条 IL 守卫断言，破坏它 `--selftest` 会红。
2. **不引入 NuGet / WinForms / WPF / WebView / System.Drawing 依赖**。UI 全部是 Win32 P/Invoke + GDI 自绘。
3. 不申请 `SeBackupPrivilege` / `SeTakeOwnershipPrivilege`；子进程只允许只读查询
   （`vssadmin list shadowstorage`、`DISM /AnalyzeComponentStore`）。
4. 构建必须 **0 warning / 0 error**，`--selftest` 必须全绿；不绿不要交付。

## 常用命令

```bash
dotnet build CDiskWatch/CDiskWatch.csproj -c Release        # 构建（日常）
CDiskWatch/bin/Release/net8.0-windows/CDiskWatch.exe --selftest        # 68 项无头自检
CDiskWatch/bin/Release/net8.0-windows/CDiskWatch.exe --report          # 打印报告
CDiskWatch/bin/Release/net8.0-windows/CDiskWatch.exe --preview --no-elevate   # 免提权打开面板
powershell -File build/publish.ps1                          # 构建→dll 自检→单文件发布→产物冒烟
powershell -File tools/shot.ps1 -Out C:\temp\shot.png        # PrintWindow 截图（CDW_SCREEN=1 走屏幕拷贝）
powershell -File tools/memprobe.ps1                         # 分阶段内存/句柄采样（含扫描期峰值）
powershell -File tools/wsprobe.ps1 -Minimized               # 验证 EmptyWorkingSet / 托盘空闲占用
```

数据目录 `%AppData%\CDiskWatch\`：`config.json`、`history\snap-*.snap`、`history\baseline.snap`。
`--scan` / `--preview` 会真的扫描 C 盘并落快照（12~40 秒），调试时注意副作用。

## 代码地图

| 文件 | 职责 |
|---|---|
| `Program.cs` | 入口、单实例（`Local\CDiskWatch_SingleInstance`）、自提权、消息循环、CLI 子命令 |
| `Win32.cs` | 全部 P/Invoke、常量、结构体。**常量写错会静默毁掉整个 UI**，见下节 |
| `Theme.cs` | 浅色配色、`CreateUiFont`、GDI 绘制原语、画刷/画笔缓存、`Release()` |
| `MainWindow.cs` | 面板：`LayoutUi`（布局唯一事实源）、`DrawXxx`、命中测试、交互、WndProc |
| `Tray.cs` | 托盘图标（按占用率变色）/ 气泡 / 右键菜单 |
| `Monitor.cs` | 调度与共享状态：1s 探针、每日扫描、派生数据、事件、历史缓存 |
| `Scanner.cs` | 原生 `FindFirstFile/FindNextFile` 全盘扫描（每文件零分配） |
| `Categories.cs` | 15 类扩展名分类（按长度分桶 + span 比较，零分配） |
| `Snapshot.cs` / `Store.cs` | 快照模型（GZip+二进制，带 `Version`）/ 仓库与保留策略 |
| `Diff.cs` | 快照对比 + 自然语言结论 + `Fmt` 格式化 |
| `Alerts.cs` / `Advice.cs` / `Report.cs` | 预警引擎 / 删除建议规则引擎 / 文本报告 |
| `SystemUsage.cs` | 卷影副本查询（CIM→vssadmin 兜底）、WinSxS 分析 |
| `SelfTest.cs` | 无头自检（含只读 IL 守卫、字体测量、排除规则、分类、结论文案方向） |
| `docs/plan.md` / `docs/audit.md` / `docs/memory-plan.md` | 技术方案 / 审计与修复记录 / 内存审计与瘦身方案 |

## 已经踩过的坑（改代码前务必看）

**UI 层**

- `DT_CALCRECT` 是 `0x400`，`0x100` 是 `DT_NOCLIP`。写错 → `Theme.Measure` 恒返回 0 →
  页签重叠、按钮文字被省略号截断。自检里有 `渲染：中文文本测量有意义` 守着。
- 字体必须用 **负高度（em）** + `DEFAULT_CHARSET` + ClearType，见 `Theme.CreateUiFont`。
  正高度 + `ANSI_CHARSET` 会让 CJK 字形墨迹溢出字宽 → 叠字。自检有 `渲染：汉字字形不溢出字宽`。
- 所有 `DrawText` 前必须 `SetBkMode(TRANSPARENT)`（`Theme.Text/TextWrap/Measure` 已处理），
  否则文字背后会被刷白色矩形。
- 窗口类必须带 `CS_DBLCLKS` 才收得到 `WM_LBUTTONDBLCLK`；处理 `WM_LBUTTONDOWN` 后要 `SetFocus(hwnd)`，
  否则键盘快捷键和滚轮都不工作。
- **绘制与命中测试必须共用 `LayoutUi` 算出的矩形**，不要在 `RowIndexAt` 里另算偏移
  （历史上差 24px 导致点错行）。
- 行绘制用 `SaveDC + CreateRectRgn + SelectClipRgn` 裁剪到列表区，否则会盖住表头。
- 每帧不要创建 GDI 对象，走 `Theme` 的缓存（`Brush`/`Pen`）；`WM_DESTROY` 里 `Theme.Release()` + `DeleteFonts()`。
- 尺寸一律 `S(px)`（96 DPI 基准 × 缩放）；`WM_DPICHANGED` 要重建字体并重新布局。
- 后台线程**不得直接动 UI / 托盘**：`Monitor` 的事件只做 `PostMessage`，
  真正刷新在 `WM_APP_TICK` / `WM_APP_SCAN_DONE` 里做。

**多语言（改文案的唯一正确姿势）**

- 文案**不在代码里**：全部在 `tools/lang/lang.tsv`（`key` + 中文 + 日文 + 英文）。
  改文案流程：编辑 tsv → `python tools/gen_lang.py` → `dotnet build`。
  **不要**直接改 `src/i18n/Tables.g.cs`（生成文件，会被覆盖）。
- 代码里只写 `Loc.T("key")` / `Loc.F("key", args…)`；句子一律用 `{0}`/`{1}` 模板，
  **禁止字符串拼接**（英文/日文语序不同，拼接翻译不回来）。
- 会落盘的数据不许存文案：语言相关字段要用**稳定枚举/Id**（`RiskLevel`、`SysFlavor`、`RuleId`），
  显示名在渲染时翻译；旧快照靠 `AdviceEngine.IdFromLegacyName()` 之类反查兼容。
- 新增语言相关显示逻辑后，跑 `python tools/check_lang.py`（`build/publish.ps1` 会自动跑）：
  它会抓「key 拼错」「文案表之外写死中文」「占位符不一致」。
- 排版有预算：页签/按钮宽度是 `Theme.Measure` 实测的，自检里断言三语言都不溢出；
  改长文案（尤其英文）后跑 `--selftest`，别只肉眼看中文。

**内存（本轮已做的约束，别回退）**

- 快照很贵：每份完整快照约占 **1.3 MB 托管内存**。历史列表/趋势图只用 `SnapHeader`（约 100 字节），
  **不要**为了图省事去缓存完整 `Snapshot`（`Monitor.History()` 就是因此改成头的）。
- 扫描结束必须走 `Monitor.ReleaseMemory()`：`GCCollectionMode.Aggressive` + LOH 压缩才会把空闲段
  decommit 给系统；普通 `Forced` 收集会把 30 MB 私有内存一直留着。
- `TrimWorkingSet()` 只在**扫描结束后隔两个心跳**由 UI 线程调用（先重绘再 trim，否则白做）。
- 测量目录一律用 `Scanner.MeasureTree`（原生递归）；`FileSystemEnumerable` 递归会让句柄峰值冲到 6 万。
- 绘制不要每帧创建位图/画刷：后台缓冲在 `EnsureBackBuffer` 里复用，GDI 对象走 `Theme` 的缓存。

**扫描/后端**

- 内部路径缓冲带 `\\?\` 前缀（`PathPrefixLen = 4`），任何与普通路径比较的地方都要跳过它
  （历史上排除规则因此永久失效）。
- 扩展名分类按长度 `switch` 分桶：**每个字面量的长度必须等于所在 case**，否则该分支永不命中。
  改表后跑一遍自检（`wav/webm/svg/tgz/sqlite3` 有断言）。
- 扫描器是零分配设计：路径/查找/扩展名缓冲复用，`Top N` 用定长数组插入排序，
  不要在热路径里 `new`/LINQ/字符串拼接。
- 同时重定向 stdout+stderr 必须并行读（`BeginOutputReadLine`），顺序 `ReadToEnd` 会死锁。
- 每日扫描：`AutoScanDue()`（到点 + 今天没扫过 + 不在扫描中），不要再用 `_dailyDone` 那种布尔量。
- `Monitor.History()` 有缓存；改动快照仓库后要 `_historyCache = null`（`RefreshDerived` 里已做）。
- 对比逻辑里 `UsedDelta > 0` 表示**已用变多、空间变少**；结论文案方向别写反。
- 快照序列化有 `Version`（当前 5）：改格式必须升版本并保持旧版本可读（`Load` 里按 `ver` 分支）。
  v5 起头部区带 `Partial` / `UnreadableDirs` / `PathTooLongCount`：
  **扫描有目录读不到（几乎每台机器都有 `System Volume Information`）只是「不完整」，不是失败**——
  必须照常落盘并刷新界面，否则界面会永远停在旧快照上（历史缺陷，见 `docs/audit.md` 2026-11-12）。

## 改 UI 的推荐流程

1. 先改 `LayoutUi`（矩形）与 `Cols`（列定义），再改 `DrawXxx`；不要在两处各算一份坐标。
2. `dotnet build` → `--selftest`。
3. 肉眼/拍照验收（本机主模型不支持看图，必须交给视觉子代理）：
   ```bash
   # 已有实例会占用单实例互斥体，测试新版要临时换 MutexName 或先结束旧进程
   taskkill /F /IM CDiskWatch.exe
   start CDiskWatch.exe --preview --no-elevate
   powershell -File tools/shot.ps1 -Out C:\temp\shot.png
   ```
   然后把 PNG 路径交给 `browser-debug` 子代理，让它逐页读图评审（它是视觉模型）。
   需要切页签/悬停时可以 `PostMessage(hwnd, WM_KEYDOWN, VK_RIGHT, 0)` 或
   `WM_LBUTTONDOWN` + `WM_LBUTTONUP` 到客户端坐标（页签行 y≈`appBarH+heroH+12`+`tabH/2`）。
4. 改动涉及文案/交互时同步更新 `README.md`，涉及缺陷/设计取舍时更新 `docs/audit.md`。

## 文案与风格

- UI 文案全部中文、简短、面向普通用户（"可用空间" 而不是 "FreeBytes"）。
- 所有"清理"类文案必须带「本软件不会自动删除任何文件」的语义。
- 文件编码 UTF-8（`<CodePage>65001</CodePage>` 已在 csproj 里，防止 GBK 代码页乱码）。
- 新增 Win32 API 时：加到 `Win32.cs` 对应分区，并在 `SelfTest.Win32EntryPoints` 里登记，
  避免再把函数挂到错误的 DLL 上。
