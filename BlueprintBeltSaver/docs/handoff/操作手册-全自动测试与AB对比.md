# 蓝图长带化（BlueprintBeltSaver）全自动测试流程手册

> 交接文档：任何会话/agent 按本手册可从零复现整套全自动测试与 A/B 对比闭环。
> 成文：2026-11-01（第六轮），2026-11-02 随第八轮更新路径与判据，2026-11-04 随第九轮（004.17
> 全合法粘贴姿态包络）更新。配套档案：同目录《实验档案-逐轮测试记录.md》（共九轮：一~六轮含门禁
> saga 与 A/B 闭环；第七轮密集蓝图门禁解锁；第八轮跨纬度结构安全；第九轮全合法粘贴姿态包络）。
> 更早历史见《总纲-实验全史.md》（覆盖至 004.11）。

## 最新补充：徒手拉带米制托底（2026-11-18）

「沿纬线/沿经线最远距离」单位是**格**（换算常数是每格弧长，不是米），「最远距离」与「蓝图合并单段上限」单位是**米**。格数超过约 1.84（含 2 格、3 格）时徒手拉出的长带当场可用，但保存成蓝图后原版粘贴预检（位移平方 > 5.3）每段报「距离太远」，无法复制粘贴。2026-11-18 按用户定稿实现米制托底：格数仍是期望步长，`LongerSnapLineNonAlloc` / `LongerGeodesic` 出口统一把相邻吸附点的实际弦长（单位向量 × 真实半径；已带半径的出口 ×1，插值用 Slerp 不归一化）钳到 `min(最远距离, 2.301172×0.98)`，超限段 slerp 加密——3 格在允许的星球/高度仍是一段，不允许时自动加密，最终产物一定可粘贴。弱约束间距（shortestUnlimit，慎用通道）开启时不钳制。面板三个长度标签已带单位后缀，传送带路径 SelectionGrid 改为互斥勾选框（点击当前项不变化）。

托底插值必须用 `LongerBeltPath.SlerpStable`（托管 slerp，方向球面插值 + 模长线性插值，与 Unity Slerp 语义一致）：`Vector3.Slerp` 是引擎 internal call，离线 net8.0 进程无法执行，导致拉带生成本来无法离线断言；换成 SlerpStable 后游戏内外行为一致。专项验证器 `tools/beltcap-verify/`（不链接生产源码，反射调用构建 DLL）：10 项用例覆盖 3 格旧设置、默认 1.75 格、高空带半径、45° 纬线、经线方向、测地线普通/螺旋，全部 PASS（如 3 格 + 顶格 2.301172：34 段超限自动加密为 69 节点，每段 ≤ 2.2551 m）。跑法：`dotnet run --project BlueprintBeltSaver/tools/beltcap-verify/BeltCapVerify.csproj -c Release`，退出码非 0 即失败。UI 与托底改动自测 122/122 通过后部署，提交 797fa81（UI）与本轮托底提交。

## 最新补充：网格复原（2026-11-05）

TEST 最新为原生网格复原修复版，SHA256 `ED5D120F…`，公有版本仍为 1.0.4。独立测试 88/88、游戏内 122/122、A/B 15/15，省带回归全绿；31 个其他插件恢复、自测插件和 RUN_TESTS 移除。用户复原用例001在默认/严格模式均得到 15 个整数网格节点，原标记保留；原因和证据见《专题/复原测试001-原因与修复.md》。此前 `681B1063…` 第十轮构建仅作历史基线。

入口：`tools/run-restoration-regression.sh`（不覆盖旧 suite-v8 的省带回归）、`tools/restorecheck/`（不链接生产源码的复原验收器）、`tools/run-restoration-selftest.ps1`（仅启动和关闭自己创建的进程，失败恢复旧 DLL）。PowerShell 脚本必须保留 UTF-8 BOM，否则 Windows PowerShell 会误解中文路径。完整命令、配置语义与保留点报告见《专题/网格复原-设计与验收.md》。

复原允许净增带，禁止把省带侧 after≤before 验收条件套到复原结果；所有安全条件仍有效。新增源文件索引必须在原版 CheckBuildingData 读取前准备好。试算和快照不得直接调用源图 Export，因为它会改变源图的私有版本字段；使用独占图克隆。

## 0. 一页速查（TL;DR）

```bash
R="X:/戴森球计划/【MOD】修改仓库"
# ① 构建（源码有改动时必须 -t:Rebuild，增量会拿旧产物假绿）
cd "$R" && dotnet build "1.我的MOD/BlueprintBeltSaver/BlueprintBeltSaver.csproj" -c Release -t:Rebuild
dotnet build ".work/belt-regression/Checks.csproj" -c Release -t:Rebuild
dotnet build "1.我的MOD/BlueprintBeltSaver/tools/abprobe/TopoProbe.csproj" -c Release -t:Rebuild
# ② 一键离线全量（004.17）：矩阵双 cap + 流程双 cap + 蓝图A 导出（TEST 同口径 + 出厂默认配置）
#    + 游戏同式全姿态审计 + 断链审计 + 附加机普查；产物在 .work/belt-regression/suite-v8-全姿态包络/
bash .work/belt-regression/run-suite-v8.sh
# ③ IL/夹具校验
dotnet run --project .work/bb-saver-check/BbSaverCheck.csproj -c Release --no-build   # 期望 ALL CHECKS PASSED
# ④ 游戏同式 × 全合法粘贴姿态审计（准则①/⑧ 地面真值；产物目录自动按名配对原件）
dotnet "1.我的MOD/BlueprintBeltSaver/tools/abprobe/bin/Release/net8.0/TopoProbe.dll" --pasteaudit <产物目录或文件> <原件目录> [--out 报告.md]
# ⑤ 诊断：--dims（区域/合法姿态）、--gap（锚点构成与理论缺口，BBS_SPRAY / BBS_SMOOTH_DEG 实验旋钮）、
#    --anchorwhy（结构锚点归因）、--addons <产物目录> <原件目录>（喷涂机/监测器/炮塔接带普查）
# ⑥ 游戏内自测（部署+清空插件+启动 → 守望 SUMMARY → 收尾还原，见 §2.5）
bash .work/belt-regression/ingame-selftest-start.sh   # 之后轮询 plugins/BBSaver.SelfTest/selftest.log
bash .work/belt-regression/ingame-selftest-finish.sh  # 必跑：删 RUN_TESTS、移除自测插件、逐个还原插件
# ⑦ A/B 对比（字节级粗筛 + 语义级最终裁决）
bash .work/belt-regression/run-ab-v8.sh
```

## 1. 资产地图

| 资产 | 路径 |
|---|---|
| MOD 源码 | `1.我的MOD/BlueprintBeltSaver/`（src/*.cs；产物 `bin/Release/netstandard2.1/`） |
| 离线校验台（矩阵/流程/逐链/断链审计） | `.work/belt-regression/`（workspace 级共享基建，**未版本化**；Checks.csproj 链接本 MOD 源码。⚠ 交接需一并拷贝：MatrixRunner/DropLinkAudit/Program.cs 于 004.16、004.17 改动未随 git 走；004.17 的 Checks.csproj 新增链接 PasteEnvelope.cs；改前原样副本 `MatrixRunner.cs.pre004.17` / `DropLinkAudit.cs.pre004.17`） |
| IL/夹具校验 | `.work/bb-saver-check/`（未版本化；引用游戏 Managed + MOD 产物 DLL。⚠ 交接需拷贝：Program.cs 版本夹具已改 1.x 族） |
| A/B 探针（指纹/导出/语义对比/字节对比/全姿态审计/诊断） | `tools/abprobe/`（本目录，随 git 版本化；引用**真实游戏 Assembly-CSharp**。004.17 新增 PasteAudit/GapAnalysis/AnchorWhy/AddonCensus；输出 UTF-8。`.work/bb-topoprobe/` 仅剩迁移指针与旧 bin/obj，可删） |
| 游戏内自测插件 | `tools/selftest/`（本目录，随 git 版本化；反射调真实 TryOptimize；RUN_TESTS 标记触发；报告后自动退出游戏；004.17 起顺带导出附加机连接位预制数据 addon-dump.txt。`.work/bbsaver-selftest/` 仅剩旧 bin，可删） |
| 测试蓝图（14 张，唯一权威源） | `7.蓝图Blueprints/BlueprintBeltSaver 测试蓝图/` |
| 导出产物（权威目录 = 004.17 全姿态包络版） | `7.蓝图Blueprints/BlueprintBeltSaver 长带化导出/{蓝图A-游戏外,蓝图B-游戏内,AB对比报告.md}`（004.16 旧产物备份在 `suite-v7-跨纬度安全/exports-004.16-backup/`） |
| 测试套件（每版一目录） | `.work/belt-regression/suite-v1…v8/`（当前权威基准 = **suite-v8-全姿态包络**：cap2.301172/、cap1.9/、A-export/、A-export-defaultcfg/、pasteaudit-*.md、addons-census*.txt、ingame/；未版本化，交接需拷贝） |
| 一键脚本（004.17，未版本化，交接需拷贝） | `.work/belt-regression/run-suite-v8.sh`（离线全量）、`ingame-selftest-start.sh` / `ingame-selftest-finish.sh`（游戏内自测部署与还原）、`run-ab-v8.sh`（A/B） |
| 测试记录（逐轮档案） | `docs/handoff/实验档案-逐轮测试记录.md`（与本手册同目录，随 git 版本化） |
| 历史档案（总纲/准则与待办/专题/参考/归档/logs/data） | `docs/handoff/`（本目录，随 git 版本化；`.work/handoff/` 已留迁移指针） |
| 部署目标 | `%APPDATA%/r2modmanPlus-local/DysonSphereProgram/profiles/TEST/BepInEx/plugins/BlueprintBeltSaver/` |
| 游戏本体 | `D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program`（Steam 需运行） |

## 2. 标准流程（每版必走，顺序固定）

### 2.1 构建
1. MOD：`dotnet build ...BlueprintBeltSaver.csproj -c Release -t:Rebuild`（**必须 -t:Rebuild**，增量构建会拿旧产物假绿；`-t:Rebuild` 失败会清掉产物，看错误再修）。
2. 离线校验台：`dotnet build .work/belt-regression/Checks.csproj -c Release -t:Rebuild`（它 `<Compile Include>` 链接 MOD 源码，MOD 源变必须重build）。

### 2.2 离线矩阵（全自动判定的核心）
- 建套件目录：`.work/belt-regression/suite-vN-<名>/matrix-list.txt`（每行一个蓝图绝对路径，`#` 注释行）。
- 运行：`Checks --matrix <list> <cap>`，cap 用 **2.301172**（默认）与 **1.9**（保守档）各跑一轮。
- ⚠ `matrix-result.md` **固定写在 list 同目录同名**——多 cap 复用同一 list 会互相覆盖；要么分目录放 list，要么跑完立即改名。
- 判定映射（四准则）：越限新链节=①、空端/悬空/臂长=分拣器、顺序违规=②、碰撞盒=⑤、`after>before` 净增带直接 FAIL；004.17 起硬上限按插件同口径（meters = 全合法粘贴姿态包络）判定，并新增「闸门姿态 / 闸门越限新链节」两列（保护集逐姿态展开、游戏 float 同式），闸门越限必须为 0；汇总看 `SUMMARY` 行 `MATRIX ALL PASS`。004.17 包络与观测纬度无关，6 个纬度的结果应完全相同。
- 流程矩阵：`Checks --flow <list> <cap>`（复刻"库内省→拖纬度→增量复核→越限从真原件重省"；004.17 起逐纬度复核改为"该纬度最近的游戏合法粘贴姿态"上的单姿态精确模型——多区域蓝图在 0.5°~72° 锚点上本身不合法）。
- 深度分析工具：`--onechains <file> <lat>`（逐链状态 CSV：optimized/distance_rejected/collision_unresolvable…，单姿态模型）、`--oneat <file> <cap> <lat> [grid|resample] [proj]`（单图单纬复现，配 `BBS_VERBOSE=1` 出锚点构成）、`--seq`（跨纬度链路复现）、`--droplink <file> [cap]`（**断链审计**，004.17 改为游戏同式 × 全合法姿态、不分新旧；接缝链接不再需要单独豁免——合法姿态上自然闭合）。
- 汇总脚本：`_summarize.py`（单套件逐图汇总）、`_compare.py`（v1↔vN 逐图对比）、`_gap_analysis.py`（逐链理论最优差距）。⚠ 这些脚本**直写 UTF-8 文件**，勿依赖控制台回显（GBK 会乱码）。

### 2.3 IL/夹具校验
`dotnet run --project .work/bb-saver-check/BbSaverCheck.csproj -c Release --no-build` → 期望 `ALL CHECKS PASSED`（含 LongerBelts 避让 GUID、真实蓝图二次优化稳定性等夹具）。

### 2.4 蓝图A导出（游戏外长带化）
```
TopoProbe --export <蓝图目录> <输出目录> [0.5,30,45,72]   # BBS_REBIND=0 BBS_FORCEREBIND=0 = 出厂默认配置路径
TopoProbe --pasteaudit <产物目录或文件> <原件目录> [--out 报告.md]   # 游戏同式 × 全合法姿态（地面真值）
TopoProbe --dims    <蓝图目录>        # 区域表 + 每偏航的合法光标纬度区间
TopoProbe --gap     <蓝图目录> [cap]  # 锚点构成 + 逐链状态 + 相对 ceil(L/cap)+1 的缺口
TopoProbe --anchorwhy <蓝图目录>      # 结构锚点归因（分叉/合流、等级、转折角度分档、元数据）
TopoProbe --addons  <产物目录> <原件目录>  # 喷涂机/监测器/炮塔连接口普查（产物须保住原最近节点）
TopoProbe --models  <蓝图目录>        # 逐图校准模型行（游戏内外一致性排查用）
TopoProbe --bisect  <单蓝图>          # 五层指纹（源/Clone/格点/插值）
TopoProbe --semdiff <A文件> <B文件>   # 语义级对比（itemId+量化坐标+引用目标多重集）
```
- 口径=游戏内 TryOptimize 全套：赤道观测（库内，004.17 包络与观测无关）、cap×SafetyMargin 0.98（**004.17 起 BandMargin 退役**）、格点主试+插值试验双模式采纳（meters 口径 Over==0 && 更少 && …）、`CheckBuildingData`→`InserterBinder.Repair`（线感知+基线保留）→硬上限自检（包络最坏）→**粘贴姿态闸门（004.17）**→**拓扑门禁（Clone 基线语义）+ 门禁反馈重跑**，导出前做序列化往返校验。
- 产物：`A_lib_<名>.txt` / `A_lat<L>_<名>.txt` + `判定报告.md`（逐图 逐纬度 判定；004.17 新增「闸门姿态/越限新」列，各纬度产物相同）。

### 2.5 蓝图B（游戏内长带化，全自动）
> 004.17 起用脚本一键执行：`ingame-selftest-start.sh`（守卫：游戏在运行或 `_selftest_disabled_others` 非空即中止；部署 DLL 前备份、SHA256 去转义比对；名单留档后清空插件；部署自测插件+RUN_TESTS；Doorstop 启动）→ 轮询 `selftest.log` 出现 `SUMMARY` → `ingame-selftest-finish.sh`（删 RUN_TESTS、归档 selftest.log/addon-dump.txt/LogOutput.log 到 `suite-v8-全姿态包络/ingame/`、移除自测插件、按名单逐个还原，输出插件数/残留数/RUN_TESTS 数）。手工步骤如下：

1. **清空插件**：把 `TEST/BepInEx/plugins/` 下除 `BlueprintBeltSaver`、`BBSaver.SelfTest` 外的全部目录移到 `TEST/BepInEx/_selftest_disabled_others/`（BuildToolOpt 的 Clone 补丁在标题界面 NRE，必须移出；并发会话的其它自测插件如 `UITweaks.SelfTest` 也一并移出；**做完必须还原**）。
2. 部署自测插件：`cp tools/selftest/bin/Release/netstandard2.1/BBSaver.SelfTest.dll <TEST>/BepInEx/plugins/BBSaver.SelfTest/`；`touch RUN_TESTS`（改时间戳即触发/重跑）；删旧 `selftest.log`。⚠ **测完必须删除 RUN_TESTS 标记**（否则玩家日常启动 TEST 会在标题界面自动跑测试并退出游戏——2026-11-02 踩过）。
3. 启动：`cd <游戏目录> && ./DSPGAME.exe --doorstop-enable true --doorstop-target "<TEST>/BepInEx/core/BepInEx.Preloader.dll"`（Steam 需运行；标题界面即跑，无需进存档）。
4. 守望：轮询 `plugins/BBSaver.SelfTest/selftest.log` 出现 `SUMMARY`（自测完成会 `Application.Quit` 自动退游戏）；产物在 `7.蓝图Blueprints/.../蓝图B-游戏内/B_lib_<名>.txt`。
5. **还原全部插件**（逐个 mv 回来，数量应对齐）。

### 2.6 A/B 对比
1. 字节级：`python tools/abprobe/_abdiff.py <A目录> <B目录> <out.md>`（逐字节+首差异偏移；**字节不一致是常态**——B 侧多一次序列化往返，浮点重编码）。
2. 语义级（最终裁决）：`TopoProbe --semdiff <A文件> <B文件>` → `SEMANTIC-IDENTICAL` 即等价（itemId+量化坐标+引用目标多重集逐项对账）。
3. 判定写回 `AB对比报告.md`。

### 2.7 收尾
- 部署：备份 `.bak-<时间戳>` → 覆盖 → SHA256 校验一致 → DLL 内 grep 新符号（字符串 UTF-16LE、字段名 UTF-8）。
- 归档：套件目录 + 测试记录文档追加本轮章节（改动/数据/分析/遗留）。
- git：只 `git add` 本 MOD 的路径（仓库含多 MOD 且常有并发会话 WIP）；push 前需 fetch+rebase，工作区有他会话未暂存改动时**暂缓 push**。

## 3. 已知坑与教训（全部实测踩过）

| # | 坑 | 处理 |
|---|---|---|
| 1 | `dotnet build` 增量跳过重编译（假绿） | 一律 `-t:Rebuild`；Rebuild 失败会删产物，先看错误 |
| 2 | `matrix-result.md` 固定名写在 list 同目录 | 多 cap/多版本分目录 |
| 3 | Checks/BbSaverCheck 的 HintPath 曾指向已删的 probe472 | 已改指游戏 `Managed/` 与 BepInEx `core/`（稳定路径） |
| 4 | `BlueprintData.CreateNew(text)` 跳过头部 → 后续 `Clone()` 返空 | 加载一律 `new BlueprintData().FromBase64String(text)`（插件注释也警告过） |
| 5 | `Clone()` 是深拷贝（ToBase64String→FromBase64String） | 对象身份、`.index`（会重编号）跨 Clone 一律不可用；按**数组位置**或**量化坐标**对齐 |
| 6 | 序列化往返浮点重编码 | 字节对比不可靠（B 侧天然多一次往返），用 `--semdiff` 语义多重集 |
| 7 | BuildToolOpt 的 `BlueprintData_Clone` Harmony 补丁在标题界面 NRE | 干净环境测试需移出该插件，测完还原 |
| 8 | bash 中文路径/heredoc 转义、GBK 控制台 | 脚本用 write 工具落盘再执行；输出直写 UTF-8 文件后用 read 读；Python 避免 `\\n` 字面量（用 chr(10)） |
| 9 | 并发会话同时改同一批文件 | 每次编辑前重读；git 只 add 自己的路径；push 在工作区有他会话 WIP 时暂缓 |
| 10 | 拓扑门禁三缺陷（已修）：对象身份跨 Clone 恒空映射；门禁基线未随 trial 分支切换；CreateNew 缺头部导出失败 | 全部修复（Clone 基线语义 + 分支基线切换 + source 底板移植）；位置歧义（同格位多线节点）已豁免 |
| 11 | summary"传送带总数：0 → …" | 标题界面 `_inspector` 为空的显示问题，不影响结果 |
| 12 | 80环带 游戏内 Clone 返空 | 该蓝图数据缺头部字段，游戏原生序列化即失败（与优化器无关），待数据侧修复 |
| 13 | 游戏可被本会话 `taskkill //F //IM DSPGAME.exe`（非高完整性） | 双实例冲突时先清后启；启动后 `Application.Quit` 自动退出 |
| 14 | RUN_TESTS 标记残留 → 玩家日常启动 TEST 在标题界面自动跑测试并退出（2026-11-02 实踩） | 自测收尾必须删标记并移除 BBSaver.SelfTest（见 §2.5 步骤 2 警示） |
| 15 | 工具工程显式 `<Compile Include>` MOD 源码清单——新增 src/*.cs 必须同步 TopoProbe.csproj 与 Checks.csproj，否则 CS0103 | 004.16 CrossLatitudeSafety.cs 踩过 |
| 16 | 长带化产物纬度绑定：静态导出/存库换纬度粘贴会断链 | 004.16 带内最坏判定治本；导出/发布文件一律用跨纬度安全管线生成 |
| 17 | 004.16 的"带内最坏"本身有洞：经度格宽按节点纬度查表（游戏按区域参考纬度）、只采样偏航 0°、又采样到游戏不允许的姿态 | 004.17 改为全合法粘贴姿态包络 + 精确闸门；任何距离模型改动都必须过 `--pasteaudit`（游戏同式地面真值） |
| 18 | `sha256sum` 对含反斜杠的路径（`$APPDATA`）输出前缀 `\` → 哈希比对假失败 | 比对前 `tr -d '\\'`（ingame-selftest-start.sh 已处理） |
| 19 | suite-v7 目录的 matrix-result.md 是 11-03 旧构建写入的（C1 84、刺笋 30），与 004.16 源码复跑（C1 223、刺笋 97）不符 | 以 `suite-v7-复核-20261104/` 复跑为 004.16 基线；任何对比先用当前源码复跑 |
| 20 | 矩阵/导出/TEST 都用分拣器重绑开启配置，出厂默认（两开关 false）路径从未被测——该路径下喷涂机窗口根本不执行（004.17 前） | 004.17 把附加机窗口移出分支；套件新增 `BBS_REBIND=0 BBS_FORCEREBIND=0` 导出 + `--pasteaudit` + `--addons` |
| 21 | 单姿态模型在非法姿态上没有意义（多区域蓝图在 0.5°~72° 锚点不合法），拿它判越限会制造假 FAIL | 矩阵/导出判定一律用包络 meters + 闸门；流程矩阵逐纬度吸附到最近合法姿态 |
| 22 | 游戏内日志"原件最坏链节 392 m"（6k，004.17 中间版） | 单姿态模型在非法观测姿态上的伪值；最终版改按包络口径输出 |

## 4. 验收判定标准（本 MOD 的"四准则"→ 断言映射）

| 准则 | 矩阵断言 | 门禁（游戏内） |
|---|---|---|
| ① 带距（不过长，**跨纬度、跨朝向**：全合法粘贴姿态最坏） | OverNew=0 + 闸门越限新链节=0（原件同跨度越限 KeptOver 豁免） | VerifyHardLimit（meters = PasteEnvelope 包络）+ 粘贴姿态闸门（逐姿态 float 同式） |
| ② 拓扑不变（不串线/分裂，⑦） | Inspect 连通分量对账 | VerifyTopology（Clone 基线 + 位置歧义豁免）+ 门禁反馈重跑 |
| ③ 分拣器不失效 | 悬空 0 / 臂长 0 / 空端 0 | InserterBinder.Repair（线感知 + **基线保留**） |
| ③ 喷涂机/监测器/炮塔不脱带（004.17 扩展） | `--addons`：有带连接口全部保持原最近节点（TEST 配置与出厂默认配置两条路径） | 附加机 1.6 格窗口（三种分拣器策略一律执行） |
| ⑤ 不与建筑碰撞 | 碰撞盒命中 0 | 构造期定段 + 守卫 |
| ⑧ 长带化产物任意纬度、任意朝向可贴（004.16 立项，004.17 精确化） | `--pasteaudit` 全合法姿态新增越限 0；`--droplink` 全合法姿态越限 0 | 包络 + 闸门源头保证；粘贴期按真实姿态单姿态复核 |
| 省带目标 | after<before（净增带 FAIL） | 采纳规则② |

## 5. 当前状态快照（2026-11-04 23:10，第九轮后）

- **TEST 已部署**：SHA256 `24C31648…`（195584 字节）= 004.17（全合法粘贴姿态包络，默认开；
  配置 `Belt.CrossLatitudeSafety` 可关 = 单姿态精确模型）。004.16 备份 `BlueprintBeltSaver.dll.bak-20261104-2242`
  （`DC5DAB90…`）；31 个其它插件原位，RUN_TESTS 已删，BBSaver.SelfTest 已移除。
- **第九轮根因**：004.16 产物经游戏同式审计实为不安全（6k 合法姿态 206 条越限；80环带/长带测试图01
  旋转越限），同时又因采样到非法姿态 + BandMargin 而过度保守；出厂默认配置下喷涂机窗口不执行。
- **最新验证（全绿）**：矩阵双 cap 84/84×2（suite-v8，闸门 0）、流程双 cap 84/84×2、bb-saver-check PASS、
  蓝图A 导出 69+69 PASS（TEST 口径 + 出厂默认）、`--pasteaudit` 原件/A/A出厂默认/B 全 PASS、
  `--droplink` 14/14 CLEAN、`--addons` 544 口全部保持、游戏内自测 32/32、**A/B 语义 14/14 IDENTICAL**。
- **省带**：14 图合计 4032（004.16 同口径复跑 3016，+33.7%）；cap 1.9 合计 2563；出厂默认配置 2777。
- **BPT / 0.10.35 new**：仍为旧版（未同步）。
- **git**：第九轮已提交；push 仍暂缓（并发 WIP：本 MOD 的 csproj/README/CHANGELOG 发布准备、ControlPanelPlus 等）。

## 6. 遗留清单（按优先级，接手即做）

1. **GitHub 推送**：并发 WIP 清理后 `git pull --rebase && git push`。
2. **发布收尾**（发布准备 WIP 属并发会话）：版本号经用户确认（004.15 + 004.16 + 004.17 都需并入发布说明；
   004.17 要点：全合法姿态包络省带 +33.7%、修复 6k/旋转粘贴断链、修复出厂默认配置喷涂机脱带、监测器/炮塔保护）
   → 全量回归（`run-suite-v8.sh` + 游戏内自测 + A/B）→ 图标确认 → 打包 Thunderstore。
3. **出厂默认配置的省带差距**：分拣器重绑关闭时 2777 vs 开启 4032。是否把 `RebindSorterLinks` /
   `SorterForceRebind` 改为默认开启需用户决定（配置说明仍标"实验"；TEST 日常即开启状态）。
4. **余量类候选（均已量化，未采纳）**：拉直阈值 3°/10° +49/+134；喷涂机窗口上界 +298（可证等价需要
   更多钉点，见实验档案第九轮）；SafetyMargin 0.98→0.99 实测 +89（+2.2%，最坏新链节 2.2779 m）。
5. **玩家实测验收**：重点验收 6k 类多区域整圈蓝图、旋转 90° 粘贴、高纬带（45°~57°）粘贴、出厂默认配置下
   喷涂机；通过后 BPT / 0.10.35 new 统一同步新版。
6. **80环带 数据侧补全（可选）**：功能已被 fail-soft + 头部底板兜住，优先级低。
7. ~~密集蓝图门禁解锁~~（第七轮）；~~跨纬度断链~~（第八轮）；~~跨纬度安全的省带损失 / 6k 与旋转断链~~（第九轮）。

## 7. 关键代码导读（改前必读）

- `BeltPathOptimizer.Optimize`（~1000 行，核心）：每 run 流程 = 建替换链（格点 kept 子集 / 插值**定段固定+分区等分**）→ `BuildRebinds`（分拣器就近重绑+臂长守卫 restoreIdx；机器类目标被删走 restoreIdx 回插；**目标仍在链中回指自身（004.15）**）→ `RunRestoreAndGuard`（弧位回插+碰撞守卫）→ `SpansValid`（**原件相邻对豁免**）→ 提交/悬空回滚。
- `BlueprintBeltSaverPlugin.TryOptimize`（门禁编排）：Clone → **基线抓取（preMergeLines/Refs+目标对象+带节点基线引用图，合并改写引用之前！）** → 格点主试 → CreateNew(快照) 插值试验 → 采纳 → **切换基线到所选分支** → CheckBuildingData → binder（线感知+基线保留） → VerifyHardLimit（**带内最坏 meters**） → VerifyTopology → **门禁反馈重跑（跨线/丢失→锚定基线目标窗口按位置重跑 ≤4 轮）** → 头部底板移植（rebuilt=source.Clone()+buildings 替换）。
- `VerifyTopology`：幸存者对账 o2r/r2o（**对象同源才可比**：preMerge↔result）+ 位置歧义豁免（posLines）。
- `InserterBinder`：**基线保留**（基线快照中已存在、目标存活且对象未变的引用一律保留，004.15）；线感知同线优先；跨线计数进 Report。
- `PasteEnvelope`（004.17，`src/PasteEnvelope.cs`）：`Build`（合法姿态 = 游戏 `GenerateAreaGratBoxByBPData`；保护集 = 原件全净姿态；按"同偏航、c 连续、各区域格宽不变"分组）→ `Distance`（每组取链节中点最贴近赤道的 c，连续松弛上界）→ `Gate`（保护集逐姿态展开、float 同式的独立复核）→ `FrameFor`/`Nearest`/`DistanceAt`（单姿态精确模型：粘贴期复核与关闭跨纬度安全时用）。`MakeFrame`/`LPos` 逐字复刻 RefreshBuildPreview，改动必须同步 tools/abprobe/PasteAudit.cs（独立审计副本）并重跑 `--pasteaudit`。
- `CrossLatitudeSafety.Wrap`（004.17）：包络 / 单姿态的选择器；BandMargin 恒 1.0（仅为兼容旧调用点）。
- `BeltSpacingCalibration`：单姿态模型（日志、跨带标记、库内试算），经度格宽 004.17 起取区域参考纬度。
- 离线复刻的对应物：ExportTool.cs（A 侧全套）+ TopoProbe.VerifyTopologyBase/OverNew（门禁复刻）+ MatrixRunner（矩阵/流程）——**改插件判定/编排必须同步改这些处**（A/B 对比与矩阵会检验同步性）。
