# 蓝图长带化（BlueprintBeltSaver）全自动测试流程手册

> 交接文档：任何会话/agent 按本手册可从零复现整套全自动测试与 A/B 对比闭环。
> 成文：2026-11-01（第六轮），2026-11-02 随第八轮更新路径与判据。配套档案：同目录
> 《实验档案-逐轮测试记录.md》（共八轮：一~六轮含门禁 saga 与 A/B 闭环；第七轮密集蓝图
> 门禁解锁；第八轮跨纬度结构安全）。更早历史见《总纲-实验全史.md》（覆盖至 004.11）。

## 0. 一页速查（TL;DR）

```bash
R="X:/戴森球计划/【MOD】修改仓库"
# ① 构建（源码有改动时必须 -t:Rebuild，增量会拿旧产物假绿）
cd "$R" && dotnet build "1.我的MOD/BlueprintBeltSaver/BlueprintBeltSaver.csproj" -c Release -t:Rebuild
dotnet build ".work/belt-regression/Checks.csproj" -c Release -t:Rebuild
# ② 离线矩阵（当前权威基准 = suite-v7-跨纬度安全；新套件建 suite-vN 目录避免覆盖）
dotnet run --project .work/belt-regression/Checks.csproj -c Release --no-build -- \
  --matrix ".work/belt-regression/suite-v7-跨纬度安全/matrix-list.txt" 2.301172
# ③ 流程矩阵（换纬度回归）
... -- --flow ".work/belt-regression/suite-v7-跨纬度安全/matrix-list.txt" 2.301172
# ④ IL/夹具校验
dotnet run --project .work/bb-saver-check/BbSaverCheck.csproj -c Release --no-build   # 期望 ALL CHECKS PASSED
# ⑤ 断链审计（全链接 × 9 粘贴纬度越限扫描；经度接缝链接自动分类）
... -- --droplink "<蓝图或导出文件>" 2.301172
# ⑥ 蓝图A导出（游戏外长带化 + 逐图判定；探针已迁 tools/abprobe）
dotnet run --project "1.我的MOD/BlueprintBeltSaver/tools/abprobe/TopoProbe.csproj" -c Release --no-build -- \
  --export "7.蓝图Blueprints/BlueprintBeltSaver 测试蓝图" "7.蓝图Blueprints/BlueprintBeltSaver 长带化导出/蓝图A-游戏外"
# ⑦ 蓝图B（游戏内长带化）：见 §2.5（部署自测插件→清空插件→Doorstop 启动→守望）
# ⑧ A/B 对比（字节级粗筛 + 语义级最终裁决）
python  "1.我的MOD/BlueprintBeltSaver/tools/abprobe/_abdiff.py" "<A目录>" "<B目录>" "<out.md>"
TopoProbe --semdiff "<A文件>" "<B文件>"                                   # 语义级（最终裁决）
```

## 1. 资产地图

| 资产 | 路径 |
|---|---|
| MOD 源码 | `1.我的MOD/BlueprintBeltSaver/`（src/*.cs；产物 `bin/Release/netstandard2.1/`） |
| 离线校验台（矩阵/流程/逐链/断链审计） | `.work/belt-regression/`（workspace 级共享基建，**未版本化**；Checks.csproj 链接本 MOD 源码。⚠ 交接需一并拷贝：MatrixRunner/DropLinkAudit/Program.cs 于 004.16 改动未随 git 走） |
| IL/夹具校验 | `.work/bb-saver-check/`（未版本化；引用游戏 Managed + MOD 产物 DLL。⚠ 交接需拷贝：Program.cs 版本夹具已改 1.x 族） |
| A/B 探针（指纹/导出/语义对比/字节对比） | `tools/abprobe/`（本目录，随 git 版本化；引用**真实游戏 Assembly-CSharp**。`.work/bb-topoprobe/` 仅剩迁移指针与旧 bin/obj，可删） |
| 游戏内自测插件 | `tools/selftest/`（本目录，随 git 版本化；反射调真实 TryOptimize；RUN_TESTS 标记触发；报告后自动退出游戏。`.work/bbsaver-selftest/` 仅剩旧 bin，可删） |
| 测试蓝图（14 张，唯一权威源） | `7.蓝图Blueprints/BlueprintBeltSaver 测试蓝图/` |
| 导出产物（权威目录 = 跨纬度安全版） | `7.蓝图Blueprints/BlueprintBeltSaver 长带化导出/{蓝图A-游戏外,蓝图B-游戏内,判定报告.md}` |
| 测试套件（每版一目录） | `.work/belt-regression/suite-v1…v7/`（当前权威基准 = **suite-v7-跨纬度安全**；未版本化，交接需拷贝） |
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
- 判定映射（四准则）：越限新链节=①、空端/悬空/臂长=分拣器、顺序违规=②、碰撞盒=⑤、`after>before` 净增带直接 FAIL；汇总看 `SUMMARY` 行 `MATRIX ALL PASS`。
- 流程矩阵：`Checks --flow <list> <cap>`（复刻"库内省→拖纬度→增量复核→越限从真原件重省"，各纬度断言非原件越限 0）。
- 深度分析工具：`--onechains <file> <lat>`（逐链状态 CSV：optimized/distance_rejected/collision_unresolvable…）、`--oneat <file> <cap> <lat> [grid|resample] [proj]`（单图单纬复现，配 `BBS_VERBOSE=1` 出锚点构成）、`--seq`（跨纬度链路复现）、`--droplink <file> [cap]`（**断链审计**：全链接 × 9 粘贴纬度越限扫描；经度接缝链接 |Δx|>300 单独分类不计——模型伪影）。
- 汇总脚本：`_summarize.py`（单套件逐图汇总）、`_compare.py`（v1↔vN 逐图对比）、`_gap_analysis.py`（逐链理论最优差距）。⚠ 这些脚本**直写 UTF-8 文件**，勿依赖控制台回显（GBK 会乱码）。

### 2.3 IL/夹具校验
`dotnet run --project .work/bb-saver-check/BbSaverCheck.csproj -c Release --no-build` → 期望 `ALL CHECKS PASSED`（含 LongerBelts 避让 GUID、真实蓝图二次优化稳定性等夹具）。

### 2.4 蓝图A导出（游戏外长带化）
```
TopoProbe --export <蓝图目录> <输出目录> [0.5,30,45,72]
TopoProbe --models  <蓝图目录>        # 逐图校准模型行（游戏内外一致性排查用）
TopoProbe --bisect  <单蓝图>          # 五层指纹（源/Clone/格点/插值）
TopoProbe --semdiff <A文件> <B文件>   # 语义级对比（itemId+量化坐标+引用目标多重集）
```
- 口径=游戏内 TryOptimize 全套：赤道建模（库内）、cap×0.98×**BandMargin 0.95（004.16 跨纬度安全）**、格点主试+插值试验双模式采纳（OverNew==0 && 更少 && …）、`CheckBuildingData`→`InserterBinder.Repair`（线感知+基线保留）→硬上限自检（带内最坏）→**拓扑门禁（Clone 基线语义）+ 门禁反馈重跑**，导出前做序列化往返校验。
- 产物：`A_lib_<名>.txt` / `A_lat<L>_<名>.txt` + `判定报告.md`（逐图 逐纬度 判定）。

### 2.5 蓝图B（游戏内长带化，全自动）
1. **清空插件**：把 `TEST/BepInEx/plugins/` 下除 `BlueprintBeltSaver`、`BBSaver.SelfTest` 外的全部目录移到 `TEST/BepInEx/_selftest_disabled_others/`（BuildToolOpt 的 Clone 补丁在标题界面 NRE，必须移出；**做完必须还原**）。
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

## 4. 验收判定标准（本 MOD 的"四准则"→ 断言映射）

| 准则 | 矩阵断言 | 门禁（游戏内） |
|---|---|---|
| ① 带距（不过长，**跨纬度**：带内最坏） | OverNew=0（原件同跨度越限 KeptOver 豁免） | VerifyHardLimit（meters=CrossLatitudeSafety.Wrap 带内最坏） |
| ② 拓扑不变（不串线/分裂，⑦） | Inspect 连通分量对账 | VerifyTopology（Clone 基线 + 位置歧义豁免）+ 门禁反馈重跑 |
| ③ 分拣器不失效 | 悬空 0 / 臂长 0 / 空端 0 | InserterBinder.Repair（线感知 + **基线保留**） |
| ⑤ 不与建筑碰撞 | 碰撞盒命中 0 | 构造期定段 + 守卫 |
| ⑦ 长带化产物任意纬度可贴（004.16） | `--droplink` 全纬度越限 0（接缝链接分类不计） | 带内最坏判定源头保证 |
| 省带目标 | after<before（净增带 FAIL） | 采纳规则② |

## 5. 当前状态快照（2026-11-02 13:30，第八轮后）

- **TEST 已部署**：SHA256 `DC5DAB90…`（185856 字节）= 004.16（跨纬度结构安全，默认开：
  距离取 0~80° 每 1° 的带内最坏 + BandMargin 0.95；配置 `Belt.CrossLatitudeSafety` 可关）。
  旧版备份 `.bak-20261102-*`；30 插件原位（BBSaver.SelfTest 已从日常 profile 移除）。
- **第八轮根因**：长带化产物纬度绑定——长跨贴到其它纬度超游戏上限 → 粘贴期丢链接 →
  并带结构断开（150绿糖 实测 A_lib 在 20°~72° 有 53~129 条越限）。修复后全纬度 0 越限。
- **最新验证（全绿）**：--droplink 全纬度审计（C1/1080/16.4 = 0；6k 仅原件固有接缝 47 条
  已分类）、矩阵双 cap 84/84×2（suite-v7）、流程矩阵 84/84×2、bb-saver-check PASS、
  蓝图A 导出判定全 PASS（已统一为跨纬度安全版）、游戏内自测第八轮 32/32、
  **A/B 语义 14/14 IDENTICAL**。
- **省带代价（如实）**：带模式省带量明显回落（C1 游戏内 411→223 等），见 suite-v7；
  `CrossLatitudeSafety=false` 可切回省带优先。
- **BPT / 0.10.35 new**：仍为旧版（未同步）。
- **git**：第八轮已提交；push 仍暂缓（并发 WIP）。
- 断链审计命令：`Checks --droplink <蓝图文件> [cap=2.301172]`（全链接 × 9 纬度越限扫描）。

## 6. 遗留清单（按优先级，接手即做）

1. **GitHub 推送**：并发 WIP（ControlPanelPlus/IconFix/本 MOD 发布准备）清理后
   `git pull --rebase && git push`。
2. **发布收尾**（发布准备 WIP 属并发会话）：版本号经用户确认（004.15+004.16 都需并入
   发布说明）→ 全量回归（本轮管线直接复用，注意 suite-v7 是最新基准）→ 图标确认 →
   打包 Thunderstore。
3. **喷涂机精确锚定提省**：需游戏内 F9 导出 `addonAreaColPoses[0/1]`（准则③高风险，
   下一轮候选；带模式下省带余量收窄，精确锚定的价值更高）。
4. **80环带 数据侧补全（可选）**：功能已被 fail-soft + 头部底板兜住，优先级低。
5. ~~密集蓝图门禁解锁~~ 已完成（第七轮）；~~跨纬度断链~~ 已完成（第八轮）；
   ~~ModelAtPlayerPosition 注释矛盾~~ 已核实为误记。

## 7. 关键代码导读（改前必读）

- `BeltPathOptimizer.Optimize`（~1000 行，核心）：每 run 流程 = 建替换链（格点 kept 子集 / 插值**定段固定+分区等分**）→ `BuildRebinds`（分拣器就近重绑+臂长守卫 restoreIdx；机器类目标被删走 restoreIdx 回插；**目标仍在链中回指自身（004.15）**）→ `RunRestoreAndGuard`（弧位回插+碰撞守卫）→ `SpansValid`（**原件相邻对豁免**）→ 提交/悬空回滚。
- `BlueprintBeltSaverPlugin.TryOptimize`（门禁编排）：Clone → **基线抓取（preMergeLines/Refs+目标对象+带节点基线引用图，合并改写引用之前！）** → 格点主试 → CreateNew(快照) 插值试验 → 采纳 → **切换基线到所选分支** → CheckBuildingData → binder（线感知+基线保留） → VerifyHardLimit（**带内最坏 meters**） → VerifyTopology → **门禁反馈重跑（跨线/丢失→锚定基线目标窗口按位置重跑 ≤4 轮）** → 头部底板移植（rebuilt=source.Clone()+buildings 替换）。
- `VerifyTopology`：幸存者对账 o2r/r2o（**对象同源才可比**：preMerge↔result）+ 位置歧义豁免（posLines）。
- `InserterBinder`：**基线保留**（基线快照中已存在、目标存活且对象未变的引用一律保留，004.15）；线感知同线优先；跨线计数进 Report。
- `CrossLatitudeSafety.Wrap`（004.16）：单一模型距离 → 纬度带内最坏；调用方传入主模型与开关。
- 离线复刻的对应物：ExportTool.cs（A 侧全套）+ TopoProbe.VerifyTopologyBase/OverNew（门禁复刻）+ MatrixRunner（矩阵/流程）——**改插件判定/编排必须同步改这些处**（A/B 对比与矩阵会检验同步性）。
