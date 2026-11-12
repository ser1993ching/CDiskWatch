# C 盘容量监控（CDiskWatch）技术方案 v2（已实现）

> 后端：.NET 8 / **纯 Win32 P/Invoke 自绘 UI**（参照 `E:\code\启动器` MiniLaunch）
> 红线：**绝不删除 / 移动 / 修改 C 盘任何文件**，只读监控
> 硬指标：托盘常驻 **工作集 32 MB**（对照 WizTree/TreeSize 的 150~400 MB）

---

## 0. 需求确认结果（与 v1 的差异）

| 项 | v1 方案 | **最终决策** | 落地情况 |
|---|---|---|---|
| UI 框架 | WinForms | ❌ → **纯 Win32 自绘** | ✅ `MainWindow.cs` 8 页签 GDI 自绘 |
| 管理员权限 | 可选 | ✅ 需要（卷影副本/系统文件归因） | ✅ 运行时 `ShellExecuteEx("runas")` 自提权 |
| 历史基线 | 等明天自然产生 | ✅ **立即跑一次全盘做基线** | ✅ `history\baseline.snap` |
| 托盘显示 | GB 数字 | ✅ **GB + 百分比** | ✅ `C: 可用 93.50 GB · 31%` |
| 通知渠道 | 气泡 + 可选 Webhook | ✅ **只要气泡** | ✅ `Shell_NotifyIcon(NIF_INFO)` |
| 面板明细 | 无 | ✅ **Top 100 大文件 + Top 20 增量文件夹** | ✅ 「大文件」「增量」两个页签 |

### 提权方式的坑（实测踩坑记录）
- 清单里写 `requireAdministrator` 在本机导致 **SxS 激活失败**（`The manifest file root element must be assembly`），
  根因是清单命名空间写成了 `urn:schemas-microsoft:asm.v1`（少 `-com`）；修正命名空间后仍失败，
  改为 **清单 asInvoker + 运行时自提权**（`ShellExecuteEx("runas")`）→ 正常。
- 因此清单内容必须严格是 `urn:schemas-microsoft-com:asm.v1 / .v2 / .v3` 与
  `urn:schemas-microsoft-com:compatibility.v1`。

---

## 1. 实测基线（本机 C 盘）

| 指标 | 值 |
|---|---|
| 总容量 / 已用 / 可用 | 299.81 GB / 206.31 GB (68.8%) / **93.50 GB (31.2%)** |
| 文件数 | 778,765 |
| 文件逻辑合计 | 209.86 GB（比实际多 3.55 GB → WinSxS 硬链接重复计数） |
| 卷影副本/还原点 | 4.65 GB（不在任何文件夹里） |
| `hiberfil.sys` | **25.48 GB** |
| `swapfile.sys` | 256 MB |
| 全盘扫描 | **12~40 秒**（磁盘缓存冷热差异大） |
| 常驻（托盘空闲） | 工作集 **32 MB** / 私有 **10 MB** |
| 扫描后 | 工作集 **40 MB** / 私有 **10 MB** |
| 快照体积 | 117 KB/份（GZip + 自定义二进制） |

> **关键洞察**：「实际已用 − 文件逻辑合计」不为 0，说明存在**不在文件夹里**的占用
> （卷影副本、MFT、簇对齐损耗、hiberfil/pagefile）。这类占用传统工具看不到，
> 却是"昨天暴涨"最常见的原因 → 因此把**系统级占用列为一等监控对象**。

---

## 2. 架构

```
CDiskWatch.exe（单进程单 exe，12 MB）
├─ Program.cs      单实例 Mutex / 自提权 / 消息循环 / CLI(--scan --report --baseline --selftest)
├─ Tray.cs         托盘图标（按占用率变色）+ 气泡 + 右键菜单
├─ MainWindow.cs   8 页签自绘面板（双缓冲 GDI）
├─ Monitor.cs      调度：30s 空间探针 / 每日 02:00 全盘扫描 / 派生数据
└─ 核心层（无 UI 依赖，CLI 与托盘共用）
   ├─ Scanner.cs      原生 FindFirstFile/FindNextFile 全盘扫描器（每文件零分配）
   ├─ Categories.cs   15 类扩展名分类器（定长数组 + span 比较，零分配）
   ├─ SystemUsage.cs  卷影副本查询（CIM）、WinSxS 只读分析
   ├─ Snapshot/Store  快照模型 + 仓库（300MB 上限自动淘汰）
   ├─ Diff.cs         日对比（目录/类别/文件三级）+ 自然语言结论
   ├─ Alerts.cs       预警引擎（阈值可配 + 冷却去抖）
   └─ Advice.cs       删除建议规则引擎（30 条规则 + 目标目录精确测量）
```

---

## 3. 扫描器：为什么不用 `FileSystemEnumerable`

| | 框架 `FileSystemEnumerable` | 原生 `FindFirstFile/FindNextFile` |
|---|---|---|
| 每文件托管分配 | **约 80 字节**（实测 72MB / 94 万文件） | **0** |
| 扫描期 GC 堆峰值 | 42 MB（堆已提交 82MB） | **≈0 MB**（堆已提交 33MB） |
| 扫描后工作集 | 88~110 MB | **40 MB** |
| 长路径 | 自动加 `\\?\` | 自行加 `\\?\`（前缀 4 字节：`\ \ ? \`） |
| 联接/符号链接 | `AttributesToSkip=ReparsePoint` | 手动判断 `FILE_ATTRIBUTE_REPARSE_POINT` |

实现要点：
- `WIN32_FIND_DATAW` 偏移（**注意 FILETIME 只有 4 字节对齐**）：
  `0 属性 / 20 最后写入 / 28 大小高 / 32 大小低 / 44 文件名`
- 路径缓冲（1024 字符）+ 查找数据缓冲 + 扩展名缓冲**全部复用**，扫描期几乎不产生垃圾；
- 文件名/扩展名用 `ReadOnlySpan<char>` 直接读原生内存（`unsafe`），不做逐字符互操作；
- 分区键前缀缓存：目录层级未变时直接复用键字符串，避免重复截断。

---

## 4. 目录聚合：非重叠分区 + 祖先汇总

普通工具要么只统计一级目录（看不出深层增长），要么建 25 万条目录索引（内存爆）。

- **分区键** = 文件的 `min(父目录层级, 4)` 级祖先 → 每个文件只计入**一个**键，键之间互不重复；
- **祖先汇总** = 扫描结束后把分区键字节数向上累加到第 1~3 级 → 层级视图照常可用
  （`C:\Users` 81.6 GB → `C:\Users\72641` 80.5 GB → `…\AppData` 71.0 GB）；
- 快照只落盘 **≥1MB** 的分区键，单份 117 KB。

---

## 5. 变化归因（三级）

1. **目录级**：分区键 ΔSize 降序 → Top 20 增量文件夹（新增/消失单独标记）
2. **类别级**：15 类扩展名分类的 Δ → 回答"属于什么类型的文件暴涨"
3. **文件级**：Top 100 大文件中"上次没有、本次出现"的 → 回答"具体是哪个文件"

结论生成器输出人话，例如：

> 空间增加 21.40 GB（占用率 68.8% → 76.0%）。主因：hiberfil.sys +0 B；
> 目录 C:\Users\72641\AppData\Local\Temp +8.42 GB；视频类 +9.10 GB；新增大文件 MEMORY.DMP 4.10 GB。

---

## 6. 删除建议（只建议，不执行）

- 规则表 30 条（可扩展），路径支持 `%ENV%` 与 `*`；
- 目录类规则对目标目录做**精确测量**（独立递归求和），因此不受"目录聚合到第 4 级"的限制；
- 文件类规则匹配 Top 100 大文件与系统文件；
- 每条建议：`路径 | 占用 | 风险等级 | 推荐度 | 手动操作指引 | 命中规则`；
- 全部文案标注「本软件不会自动删除任何文件」，命令交由用户自行执行。

样例（本机）：

| 占用 | 风险 | 对象 | 建议 |
|---|---|---|---|
| 28.51 GB | 高 | `…\AppData\Local\Docker` | 在 Docker Desktop 内清理镜像/容器，不要手删 |
| 25.48 GB | 中 | `C:\hiberfil.sys` | 不需要休眠可自行 `powercfg /h off` |
| 17.99 GB | 中 | `C:\Windows\WinSxS` | 自行运行 DISM `/StartComponentCleanup` |
| 5.14 GB | 低 | `…\.nuget\packages` | `dotnet nuget locals all --clear` |
| 2.07 GB | 低 | `…\AppData\Local\Temp` | 关闭程序后可删 |

---

## 7. 只读红线（技术落地）

1. **源码层**：不使用 `File.Delete` / `Directory.Delete` / `File.Move` / `Directory.Move` / `File.Copy` / `File.Create`；
2. **构建期守卫**：`SelfTest.cs` 用 `System.Reflection.Metadata` 扫描程序集 IL 的 `call/callvirt/newobj`，
   精确到调用点断言"除 `SelfTest`、`Store` 外无删除类调用"；白名单类型只允许操作 `%AppData%\CDiskWatch`；
   `build/publish.ps1` 在打包前对独立 dll 执行该守卫，不通过即终止发布；
3. **运行时**：不申请 SeBackupPrivilege / SeTakeOwnershipPrivilege；子进程仅 `vssadmin list` / `DISM /AnalyzeComponentStore`；
4. **UI 层**：所有"清理"文案标注"仅建议，不会自动执行"。

---

## 8. 内存治理

| 手段 | 效果 |
|---|---|
| 原生目录枚举（零分配） | 扫描期 GC 堆增量 ≈ 0 |
| 目录聚合用非重叠分区 | 扫描期常驻 ~2 MB |
| `System.GC.ConserveMemory=9` | 已提交内存 82MB → 33MB |
| 扫描后 `GC.Collect(2, Forced, blocking, compacting)` ×2 | GC 堆 42MB → 4MB |
| 扫描期每 128K 文件一次 gen0 回收 | 抑制堆膨胀 |
| 快照只存 ≥1MB 目录 | 快照 1.27MB → 117KB |

---

## 9. 质量保障

- **57 项无头自检**（`--selftest`，退出码 0/1）：
  分类器 12 项、层级/截断 4 项、格式化 3 项、快照往返 7 项、扫描器 5 项、对比 5 项、
  建议 8 项、预警 1 项、**只读 IL 守卫 2 项**、图标 1 项、**Win32 入口点 6 项**；
- **Win32 入口点自检**：用 `GetProcAddress` 校验 6 个 DLL 的 80+ 个导出函数，
  防止 P/Invoke 挂错 DLL（开发期确实踩过 `DrawTextW`/`FillRect` 被挂到 gdi32.dll 导致启动即崩的坑）；
- **发布脚本四步**：构建 → dll 自检（含守卫）→ 单文件发布 → 产物冒烟自检。

---

## 10. 里程碑

| 里程碑 | 交付 | 状态 |
|---|---|---|
| M1 | 原生全盘扫描器 + 分类 + 系统占用 + CLI 报告 | ✅ |
| M2 | 快照仓库 + 基线 + 日对比（目录/类别/文件三级） | ✅ |
| M3 | 预警引擎 + 删除建议引擎 | ✅ |
| M4 | 托盘 + 8 页签自绘面板（含 Top 100 大文件 / Top 20 增量文件夹） | ✅ |
| M5 | 只读守卫、自检、图标、单文件发布、截图核验 | ✅ |
| M6（可选） | USN 增量追踪（秒级探测） | ⏸ 未做（12~40 秒的日扫描已足够） |

---

## 11. 已知限制

- 目录归属到第 4 级（深度更细的目录看「建议」页，那里是精确测量）；
- WinSxS 等硬链接导致逻辑合计略大于实际占用（报告已标注差额）；
- 联接/符号链接被跳过（防环路与重复统计），OneDrive 占位文件不计入；
- 非管理员运行时看不到受保护目录（卷影副本、`System Volume Information` 等），差异会在报告里体现。
