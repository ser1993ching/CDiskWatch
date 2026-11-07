# CDiskWatch 代码审计与 UI 重构报告

> 审计范围：`CDiskWatch/src/*.cs` 全部 20 个源文件（约 5.6k 行）、构建脚本与文档。
> 记忆：31 项缺陷 + 5 项内存/资源问题（见文末「内存审计补充发现」）。
> 结论先说：**仓库当时处于「编译不通过」状态**；即使编译通过，UI 也因为一个 Win32 常量写错而完全不可用。
> 本轮已完成 UI 重构（浅色主题 + 统一布局/命中测试 + 交互）、后端缺陷修复与内存瘦身，自检从 57 项扩到 **75 项全绿**。

---

## 一、致命问题（P0：直接导致「没法用」）

| # | 位置 | 问题 | 后果 | 处理 |
|---|---|---|---|---|
| 1 | `Program.cs:102-108` | 调试残留：lambda 块缺少闭合 `}`，`try{...}catch{}` 后面直接跟了一个裸块 | **整个项目无法编译**（7 个语法错误） | 重写该事件订阅，后台线程只 `PostMessage`，UI 线程刷新 |
| 2 | `Win32.cs` | `DT_CALCRECT` 写成 `0x00000100`（那是 `DT_NOCLIP`），正确值是 `0x00000400` | `Theme.MeasureText` **恒返回 0**：所有按文本宽度算的布局全部塌缩——页签互相重叠、按钮文字被 `DT_END_ELLIPSIS` 截成「数…」、状态胶囊文字溢出 | 修正常量，并在自检里加断言（见 #12） |
| 3 | `Theme.cs` / `MainWindow.cs` | `CreateFontW` 用 `ANSI_CHARSET`(0) + **正** `lfHeight` | GDI 用拉丁字体的 advance + 字链接的 CJK 字形，**字宽 < 字形墨迹宽度 → 相邻汉字互相压叠、糊成一团**；字号还比设定值小 30% | 新增 `Theme.CreateUiFont`：负高度（em）+ `DEFAULT_CHARSET` + ClearType；自检断言「墨迹宽 ≤ 字宽」 |
| 4 | `MainWindow.cs:DrawContent` | 行从 `y = _contentTop + _preH - _scroll` 开始画，而表头也画在 `_contentTop + _preH` | **第一行数据和表头完全重叠** | 重写：表头/列表区各自独立矩形，行绘制用 `SaveDC + RectRgn` 裁剪到列表区 |
| 5 | `MainWindow.cs:RowIndexAt` | 命中测试起点 `+S(24)` 与绘制起点 `-S(24)` 不一致 | 点击行会**错行**，点 A 行选中 B 行 | 绘制与命中测试共用同一套布局矩形（`LayoutUi` + `RowHeight`） |

## 二、功能性缺陷（P1：显示/数据不对）

| # | 位置 | 问题 | 处理 |
|---|---|---|---|
| 6 | `MainWindow.cs` | `WM_APP_SCAN_DONE` 有处理分支但**全项目没有任何地方 PostMessage 它** → 扫描完成后列表不重建，必须手动切页签才看到新数据 | `Monitor` 新增 `OnDataChanged`，扫描完成后 `PostMessage(WM_APP_SCAN_DONE)`；`OnTick` 再用 `DataVersion` 兜底 |
| 7 | `Monitor.Tick` | ① 每日扫描被 `Cfg.ScanOnStartup` 错误地门控（关掉「启动扫描」= 永不每日扫描）；② `_dailyDone` 置 true 后**永不复位**（第二天不再扫）；③ 只在计划时刻后 1 小时内触发，错过就整天不扫 | 改为 `AutoScanDue()`：到点 + 今天未扫过 + 不在扫描中；扫描完成后记录日期，天然复位 |
| 8 | `Scanner.PathStartsWith` | 比较时用的是带 `\\?\` 前缀的内部缓冲，而排除列表是普通路径 | **所有排除规则永久失效**（包括自身数据目录），自查报告里「已排除」是假的 | 跳过 4 字符前缀比较，并加目录边界判断（`C:\Temp` 不匹配 `C:\Temp2`）；新增 `ExcludedDirs` 计数与自检 |
| 9 | `MainWindow.DrawHistoryChart` | `Polygon()` 画折线 → 首尾相连成**填充多边形** | 「趋势图」变成一块诡异色块 | 面积用 `Polygon`（闭合到底边）+ 折线用 `Polyline`，并补 y 轴刻度/网格/x 轴标签 |
| 10 | `MainWindow.DrawHistoryChart` / `Monitor.History` | 每次重绘都 `Store.History()` → **把所有快照从磁盘 GZip 全量解压一遍** | 秒级重绘时 CPU 打满、页面卡顿 | `Monitor` 增加历史快照缓存，`RefreshDerived` 时失效 |
| 11 | `DiffEngine.BuildVerdict` | `UsedDelta > 0` 时输出「空间释放 X」 | 结论**说反了**（占用变多却说释放），用户会被误导 | 改为「已用空间增加/减少」；变化极小时输出「基本持平」而不是「68.7% → 68.7%」 |
| 12 | `Categories.ByExtension` | 扩展名按**长度分桶**，但桶内字面量长度写错（`wav` 写在 2 字符桶、`webm`/`m2ts`/`divx` 写在 3 字符桶、`sqlite`/`apphost` 写在 6 字符桶…） | 这些分支**永远不可达**：`.wav`（音频）、`.webm`（视频）、`.tgz`/`.zst`、`.cpp`/`.hpp`、`.evtx`、`.vsix`、`.appx`、`.msix` 全被归到「其他」 | 重写整张表并加脚本校验「每个字面量长度 == 所在 case」；自检新增 5 条分类断言 |
| 13 | `Categories.Colors` | 注释写「橙」实际是蓝色（`0x00BBGGRR` 与 RGB 通道搞反） | 类别条颜色与名称不符 | 用 `Win32.Rgb()` 直接写 RGB，与注释一致 |
| 14 | `SystemUsage.RunProcess` | 先 `stdout.ReadToEnd()` 再 `stderr.ReadToEnd()`（两者都重定向） | **经典管道死锁**：子进程写满 stderr 缓冲就卡住，父进程还在等 stdout 结束 → 查询卷影副本可能永久挂起 | 改为 `BeginOutputReadLine`/`BeginErrorReadLine` 并行读取 + 超时杀进程树 |
| 15 | `MainWindow.DrawContent` | `_rows.IndexOf(r)` 在逐行绘制里调用 | O(n²)，400 行时每帧 8 万次引用比较 | 改为索引循环 |
| 16 | `DiffEngine.Compare` | 判断「旧目录是否消失」用 `to.Dirs.Any(...)` | O(n²)（分区键可达数万） | 改用 `HashSet` |
| 17 | `AlertEngine.Fire` | 冷却表以「文件完整路径」为 key，**无上限** | 长期运行内存缓慢增长 | 超过 512 条时清理过冷却期条目 |
| 18 | `Theme.DrawText` | 未 `SetBkMode(TRANSPARENT)` | `DrawText` 用不透明背景模式，**文字背后刷白色矩形**（暗色主题下尤其刺眼） | 所有文本绘制强制透明背景 |

## 三、与"只读红线"和自检相关

| # | 位置 | 问题 | 处理 |
|---|---|---|---|
| 19 | `SelfTest.ScanForbiddenCallSites` | 同一处违规被 `Add` 两次（重复报告） | 删除重复行 |
| 20 | `SelfTest` | 没有关注文本度量/字体，所以 #2/#3 这类「UI 完全坏掉」的 bug 无法被自检捕获 | 新增 3 条渲染断言：字体可创建、测量宽度有意义、汉字墨迹不溢出字宽 |
| 21 | `SelfTest` | 没有覆盖「排除规则」「结论文案方向」「扩展名分桶」 | 新增 8 条断言（总数 57 → 68） |

## 四、体验与工程卫生（P2/P3）

| # | 问题 | 处理 |
|---|---|---|
| 22 | `Program.cs` 每个 1 秒 tick 都往 `%TEMP%\cdw_dbg.log` 追加一行，`WM_SHOWWINDOW` 也写日志 | 全部删除（无谓磁盘写入 + 隐私噪音） |
| 23 | 托盘提示在**后台线程**直接调 `Shell_NotifyIcon` | 移到 UI 线程：`Monitor.OnStateChanged` 只 `PostMessage`，`WM_APP_TICK` 里 `Tray.SyncTip()` |
| 24 | `StartMinimized` 默认 true，首次运行只出现托盘图标 | 首次运行（无快照）无条件显示面板；再次双击 exe 时把已有窗口叫到前台（`FindWindowW`），不再静默退出 |
| 25 | 概览页提示「点击工具栏「立即扫描」」，但**界面上根本没有工具栏** | 顶栏新增「数据目录 / 复制报告 / 立即扫描」三个按钮（带 hover/禁用态） |
| 26 | 窗口没有图标（`WNDCLASSW.hIcon` 为空） | 注册窗口类 + `WM_SETICON` 设置大/小图标 |
| 27 | DPI：`_scale` 恒为 1，`WM_DPICHANGED` 只声明未处理 | 启动读取 `GetDpiForWindow`、处理 `WM_DPICHANGED`，字体与全部尺寸按缩放重建 |
| 28 | `_preH`（历史页图表预留高度）只在切页签时清零，键盘切页会残留 | 图表高度改为按页签即时计算 |
| 29 | `Program.SetClipboardText` 失败时 `GlobalAlloc` 句柄泄漏 | 失败路径 `GlobalFree` |
| 30 | `ToggleAutoStart` 注册表键可能为 null（`CreateSubKey` 失败） | 空值回滚并提示 |
| 31 | UI 没有选中态、键盘导航、滚动条拖动、鼠标手势 | 新增：行选中/悬停高亮、`↑↓ PgUp PgDn Home End` 导航、`←/→` 切页签、`F5` 扫描、`Enter` 打开、双击打开资源管理器、滚轮、可拖动滚动条（含轨道与点击翻页）、手型指针 |

## 五、这一轮 UI 重构做了什么

- **浅色主题**：画布 `#F2F5F9`、卡片纯白 + 1px 描边、品牌蓝 `#2B62E8`、语义色（绿/琥珀/红）低饱和柔光底；正文 `#1B2535`、次要 `#465367`、弱化 `#606C7F`，对比度均达 WCAG AA。
- **布局重做**：顶栏（Logo/标题/副标题 + 三个操作按钮）→ Hero 卡片（可用空间大数字、占用率进度条、变化徽标、扫描时间/下次计划）→ 页签胶囊 → 内容卡片（独立表头 + 斑马纹/分隔线 + 可拖动滚动条）→ 状态栏（状态点 + 提示 + 快捷键说明）。
- **绘制与命中测试共用同一套矩形**（`LayoutUi`），彻底消除「画在这、点在那」的错位。
- **GDI 资源缓存**：画刷/画笔按颜色复用（原来每次重绘创建上百个 GDI 对象），双缓冲不变。
- **7 个页面全部重做表格样式**，并按内容调整列：
  - 概览：结论提到「本次变化」下方（原来滚到最底部才看得到，还被裁一半）；未归因说明按是否管理员区分措辞。
  - 目录：分区键（第 4 级）+ 祖先汇总统一按占用排序，缩进表示层级，进度条按**真实占比**（原来按列表最大值归一，42% 的目录画出满条）。
  - 建议：长操作说明移到整行副标题（原来被挤在 36% 宽的列里被省略号截断，命令抄不下来）；列表底部给出合计。
  - 历史：折线 + 面积 + y 轴刻度 + 网格 + x 轴时间。
  - 长路径统一用 `DT_PATH_ELLIPSIS`（保留文件名尾部）；**悬停任意行时状态栏显示完整路径**。

## 六、仍未处理（明确的设计取舍）

1. **目录只聚合到第 4 级**（`MaxDirLevel`）：更深的目录看「建议」页，那里是精确递归测量。
2. **硬链接重复计数**：WinSxS/System32 的硬链接会让「文件逻辑合计」大于实际占用，报告里单独列出差额。
3. **非管理员运行时**：`System Volume Information` 等目录读不到，会体现为「未归因占用」偏高；提示文案已区分，但快照里没有记录「本次是否提权」，跨权限对比会有噪声（建议后续在快照里加 `IsElevated` 字段）。
4. **建议引擎的规则匹配**仍是「字面前缀 + 单段通配」，不支持多段通配与正则。
5. **没有 USN 增量追踪**：每日一次全盘扫描（10~40 秒）对当前需求足够。
6. **自绘 UI 没有无障碍（UIA）支持**：没有实现 `WM_GETOBJECT`，屏幕阅读器读不到内容。

---

## 七、验证

```bash
dotnet build CDiskWatch/CDiskWatch.csproj -c Release      # 0 warning / 0 error
CDiskWatch.exe --selftest                                  # 68 项通过，0 失败
CDiskWatch.exe --report                                    # 结论文案方向正确
CDiskWatch.exe --report --baseline
CDiskWatch.exe --preview --no-elevate                      # 免提权预览面板
```

自检新增的关键断言：

| 断言 | 防的是什么 |
|---|---|
| 渲染：中文文本测量有意义 | `DT_CALCRECT` 之类常量写错导致测量为 0 |
| 渲染：汉字字形不溢出字宽 | 字符集/字高设置错误导致的叠字 |
| 扫描：排除路径生效 | `\\?\` 前缀导致排除规则失效 |
| 对比：结论文案方向正确 | 「变多」被写成「释放」 |
| 分类：wav/webm/svg/tgz/sqlite3 | 扩展名分桶长度错位 |

---

## 八、内存审计补充发现（36~41，已修复）

| # | 位置 | 问题 | 后果 | 处理 |
|---|---|---|---|---|
| 36 | `Monitor` | 扫描后的两次普通 `compacting` GC **不会把空闲堆段还给系统** | 扫描后私有内存 83 MB、工作集 113 MB 常驻不退（活数据只有 4 MB） | 改用 `GCCollectionMode.Aggressive` + `LargeObjectHeapCompactionMode.CompactOnce`：私有 30 → 10 MB（实测） |
| 37 | `MainWindow.PaintTo` | 每帧 `CreateCompatibleBitmap(1180×780)` = **3.5 MB**，鼠标悬停每次重绘都新建一次 | GDI/堆抖动 | 缓存后台缓冲位图与 DC，尺寸变化才重建 |
| 38 | `Monitor._historyCache` | 缓存的是**完整快照**（≈1.3 MB/份）且永不释放；历史仓库上限 300 MB ≈ 2500 份 | 看一次历史页就多吃 12.5 MB；长期可涨到数百 MB | 改为只缓存 `SnapHeader`（每份约 100 字节），历史页/趋势图都只用它 |
| 39 | `AdviceEngine.MeasureDir` | `FileSystemEnumerable` 递归遍历（WinSxS 等） | 句柄峰值 **59,596**（原生遍历只有 211），且产生 ~10 MB 垃圾 | 改用 `Scanner.MeasureTree` 原生递归（结果与耗时一致） |
| 40 | `Monitor.RefreshDerived` | 扫描后把 Latest/Prev/Baseline **重新从磁盘读一遍** | 两次多余反序列化 + 一份重复对象图；Baseline 根本没人用 | 复用内存里的新快照（新→Latest、旧→Prev）；Baseline 改为按需加载 |
| 41 | `Scanner.Finish` / `Snapshot.ComputeRollups` | 祖先汇总用 `lv < p.Level`，**漏掉分区键自身所在层级的直属文件** | 层级视图偏低（如 `C:\Windows\WinSxS` 少了它根目录下的文件） | 改为 `lv <= p.Level`，并在目录页按路径去重（汇总值即整棵子树） |

附：还有一处**句柄**问题（峰值 59,596）的定位过程与结论记录在 `docs/memory-plan.md` 第一节，便于日后回归。

## 2026-11 大文件夹分析功能

- 新增按需的大文件夹分析页，后台扫描常见开发依赖缓存、项目构建产物、微信/QQ 聊天数据、常见应用数据、下载目录及文档/桌面一级个人文件夹。
- 所有大小测量复用 `Scanner.MeasureTree`；跳过重解析点，按配置盘符过滤，最小展示阈值 64 MiB，最多展示 100 项。
- 删除风险只作保守初判：依赖缓存和构建产物为中风险；已安装开发扩展、应用数据、下载和个人文件为高风险；微信/QQ 数据为极高风险。
- 双击条目沿用现有 Explorer 定位功能。程序不提供删除按钮，不增加任何修改用户文件的 API。
- 分析仅在用户打开页面或点击工具栏按钮时触发，后台线程执行，不影响主 UI 消息循环。
- 风险是基于路径类型的启发式判断，不代表文件一定可删除；尤其聊天记录、应用数据库和个人资料应优先使用应用自身管理功能并先备份。
