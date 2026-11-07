# 多语言（i18n）方案 · 已实施

> 目标：界面、托盘、CLI 报告、历史数据全链路可切换语言，且**不破坏单文件发布 + 零依赖 + 自绘即全部像素**这三条既有约束。
> 现状：全部 439 处中文字面量原本硬编码在 8 个源文件里（去重 372 条，其中面向用户约 294 条）——**已全部迁移到文案表**。

**拍板结论（已按此实施）**

| 决策 | 结果 |
|---|---|
| 语言范围 | **中文 / 日文 / 英文**（`zh-CN` / `ja-JP` / `en-US`），不做繁体 |
| 切换方式 | **跟随系统 + 配置**（不做菜单内热切换，改语言重启生效）；`--lang` 与 `CDISKWATCH_LANG` 供脚本/测试用 |
| 外部语言包 | **开放**：`%AppData%\CDiskWatch\lang\<code>.json`（覆盖/扩充内置文案） |
| 结构化报告 | **做了**：`--report --format json` |
| 英文风格 | **简洁技术风**（`Free space` / `Rescan now` / `Cleanup suggestions`） |

---

## 一、现状盘点（决定工作量的真实数字）

| 文件 | 中文字面量 | 去重 | 性质 |
|---|---|---|---|
| `MainWindow.cs` | 135 | 96 | 页签/表头/按钮/状态栏/结论文案/胶囊 |
| `Advice.cs` | 92 | 72 | 规则名 30 + 规则 Tip 30 + 风险等级 + 建议语 |
| `SelfTest.cs` | 94 | 89 | **测试日志，按设计保持中文不译** |
| `Report.cs` | 27 | 22 | CLI 文本报告 |
| `Alerts.cs` | 19 | 14 | 预警标题/正文 |
| `Program.cs` | 19 | 17 | CLI 提示/Usage |
| `Categories.cs` | 15 | 15 | 15 个类别名 |
| `Tray.cs` | 11 | 9 | 托盘 tooltip + 6 个菜单项 + 气泡 |
| `Diff.cs` | 9 | 8 | 结论文案（拼接式） |
| `Scanner.cs` / `Monitor.cs` / `SystemUsage.cs` | 18 | 14 | 扫描阶段状态、系统文件 Kind/Note |

### 盘点时发现的 4 个硬问题（已全部修掉）

| # | 问题 | 后果 | 修法 |
|---|---|---|---|
| 1 | `MainWindow` 用 `c.Risk == "高"` 做等值比较决定颜色；`Risk` 落盘为中文串 | 换语言后风险颜色全部失效（逻辑被文案绑架） | `RiskLevel` 枚举 + `RiskLabel`/`RiskCode` |
| 2 | `AdviceCard.Rule / Risk / Tip`、`SysFileStat.Kind / Note` **写进了快照二进制** | 历史快照永远是旧语言 | 快照 v4：只存 `RuleId` / `RiskLevel` / `SysFlavor`，文案渲染时翻译；v3 旧档按中文反查还原 |
| 3 | `Diff.BuildVerdict` 用 `；`、`（`手工拼句子 | 英文语序翻译不回来 | 改为「结构化条目 + 模板渲染」 |
| 4 | `Fmt.*` 用 `ToString("F2")` 未指定 culture | 跟随系统区域可能出现 `1,5 GB` | 全部显式 `InvariantCulture` |

### 两个好消息（省了大量改造）

* **类别是按索引存的**（`long[] CatBytes[i]`），名字没落盘 → 只需翻译显示名，`Cat.Ids` 补上稳定 id 即可；
* **页签宽度、顶栏按钮宽度本来就是 `Theme.Measure` 实测 + padding** → 英文变宽自动撑开，布局算法未改。

---

## 二、最终架构

```
tools/lang/lang.tsv          唯一事实源：key <TAB> zh-CN <TAB> ja-JP <TAB> en-US（294 条）
        │  python tools/gen_lang.py        （校验占位符一致、key 唯一）
        ▼
CDiskWatch/src/i18n/Tables.g.cs           生成的三个 string[]（每语言 key/value 交替）
        │
CDiskWatch/src/i18n/Loc.cs    Loc.T(key) / Loc.F(key, args) / Load(code) / FontFace / DateShortPattern
                              + LangPack（外部 JSON 覆盖，逐条校验占位符）
        │
   所有界面 / 托盘 / CLI / 报告代码（不再出现任何中文文案）
        ▲
tools/check_lang.py          开发期守卫（publish.ps1 调用）：key 拼错、表外写死中文、占位符不一致
```

调用形态：

```csharp
Theme.Text(hdc, _fTitle, Loc.T("app.title"), ...);                       // 静态标签
sb.Append(Loc.F("verdict.headGrew", Fmt.Bytes(d.UsedDelta), ratio));     // 句子模板
```

回退链：`外部语言包[key]` → `内置目标语言[key]` → `内置中文[key]` → `key`（并在 `Loc.Missing` 记账，自检断言为空）。

### 语言相关的基础设施

| 项 | 规则 |
|---|---|
| 字体 | 中 `Microsoft YaHei UI` / 日 `Yu Gothic UI` / 英 `Segoe UI`（`Loc.FontFace`） |
| 日期 | 中·日 `MM-dd HH:mm`，英 `yyyy-MM-dd HH:mm`（不用月份名，规避 `InvariantGlobalization`） |
| 数字 | 一律 `InvariantCulture`（`Fmt.Bytes/Pct/Num`） |
| 优先级 | `--lang` > `CDISKWATCH_LANG` > `config.json` 的 `Lang`（`auto` = 跟随系统 UI 语言） |

---

## 三、快照 v3 → v4（语言无关的持久化）

| 字段 | v3 | v4 |
|---|---|---|
| `AdviceCard.Risk` | 字符串 `"低"/"中"/"高"` | `byte` 枚举 `RiskLevel` |
| `AdviceCard.Rule` | 规则中文名 | `RuleId`（`temp-user` / `winsxs` …） |
| `AdviceCard.Tip` | 中文长句 | **不落盘**（渲染时按 Id 取卡片文案） |
| `SysFileStat.Kind` | `"系统文件"/"崩溃转储"` | `byte` 枚举 `SysFlavor` |
| `SysFileStat.Note` | 中文备注 | **不落盘**（由 `SysFlavor` 生成） |

兼容：`ver == 3` 时读旧字段，用 `AdviceEngine.IdFromLegacyName()`（中文名 → Id）与 `RiskFromLegacy()`、
`SysFileStat.FlavorOf(文件名)` 还原；**只读兼容，不重写用户已有快照**。
`SnapHeader` 只读到 `HardlinkBytes`（在 Advice 之前）→ 历史页/趋势图的轻量头不受 v4 影响。

---

## 四、自检与守卫（89 项，其中多语言 14 项）

| 断言 | 防的是什么 |
|---|---|
| 三种语言 key 集合完全一致 | 漏翻 / 多翻 |
| 占位符一一对应 | `FormatException` 崩溃 |
| 无空译文 | 劣质翻译 |
| 每种语言全部 key 可解析（`T` 不吐 key、`F` 不抛异常） | key 拼错、参数个数错 |
| **排版预算**：页签/按钮在三语言下 `Measure` 不超宽 | 英文变长撑破固定宽度 |
| 字体/日期格式随语言切换 | 配置没生效 |
| 旧快照规则名 / 风险等级可还原 | v3 兼容 |
| `tools/check_lang.py`（发布前跑） | **文案表之外写死中文**、key 拼错、僵尸词条 |

---

## 五、实测结果

```
$ CDiskWatch.exe --selftest --lang zh-CN   → 89 项通过，0 项失败
$ CDiskWatch.exe --selftest --lang ja-JP   → 89 项通过，0 项失败
$ CDiskWatch.exe --selftest --lang en-US   → 89 项通过，0 项失败
$ python tools/check_lang.py               → ✓ 294 条词条，221 处调用引用
```

界面回归（`tools/langshots.ps1` 用 ←/→ 遍历 7 页 × 中/日/英，视觉逐页核对）：
三个语言下页签、表头、胶囊、图表、建议页的长文本均完整，无截断/重叠/中文残留；
日文使用 `Yu Gothic UI`，字形无豆腐块。

实施过程中被视觉复核抓到并修掉的问题：① 英文 `Free on 31.1% · 68.9% used` 读不通（模板参数语义错位）；
② 标题里 `Top 20(` 括号前缺空格（改为把说明并进标题模板，去掉字符串拼接）；③ 有历史快照时状态栏仍显示「尚未扫描」
（`RefreshDerived` 里补状态）。

---

## 六、已知限制

* **不做 RTL**（阿拉伯语/希伯来语需要镜像整套自绘布局，成本远高于收益）；
* 语言切换需**重启进程**（按拍板的「跟随系统 + 配置」方案，未做菜单内热切换）；
* 外部语言包只覆盖已有 key，新增语言的字体/日期格式目前跟随语言代码里的固定映射；
* `--selftest` 的诊断日志按设计保持中文（断言本身已与语言无关）。
