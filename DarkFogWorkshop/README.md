# DarkFogWorkshop 黑雾工坊

作者：**TTenYX**

用物流塔把**科学矩阵**（玩家社区中俗称"糖"）兑换成各种**黑雾材料**。

需要 **BepInEx 5**。适配游戏版本 **0.10.35**。

---

## 这个 MOD 做什么

截止0.10.35，游戏原版获取黑雾材料的方式为2种，击杀黑雾拾取或者元数据手动兑换。

前者有人困扰于不想开黑雾或者黑雾卡顿高，后者尚且无法自动化且消耗较高，本MOD给出了一种可自动化的方案。

给物流塔（星际物流运输站和行星内物流运输站均可）添加了将科学矩阵研究为黑雾材料的功能：

把第 1 格的**科学矩阵**按"算力"折算成点数，
再用这些点数在第 2 格起**同时产出多种黑雾材料**。

原本要手动搬运的黑雾材料（能量碎片、黑雾矩阵、硅基神经元、负熵奇点、物质重组器、核心素），
现在只要往塔里塞矩阵，就能按你设定的速率持续产出。

**核心特点**

- **每种材料各自独立结算**，互不挤占（放 3 种就是 3 倍消耗+3倍产能）。
- 一套**算力（点数）体系**把矩阵与材料统一换算，比例全部可在配置文件里改。
- 塔里放什么、就有对应的产出速率，**悬停提示**实时显示每分钟的消耗与产出。
- **不修改任何塔名、不改变耗电**，与原版物流塔行为一致。

## 玩法：把塔配成"黑雾工坊"

| 槽位 | 放什么 | 设置 |
|---|---|---|
| **第 1 格** | 科学矩阵（电磁 / 能量 / 结构 / 信息 / 引力 / 宇宙矩阵） | 必须勾选 **本地需求** |
| **第 2 格起** | 任意黑雾材料，可放多种 | 按需要设供应 / 需求 |

三个条件同时满足时，每帧消耗第 1 格的矩阵，并给**每一种**材料各自生成对应的产出。

> 第 1 格勾"本地需求"是**必要的触发条件**。没选择本地需求的塔不会被本 MOD 触发检测，
> 所以不会误伤普通的星际塔。

参与兑换的材料槽位默认到**第 6 格**（原版塔的全部槽位）。
装了扩容类 MOD 后可把配置项 `OutputSlotLimit` 调更大（最大 20）。

可参与兑换的六种黑雾材料：

| 黑雾材料 | 默认算力 |
|---|---|
| 能量碎片 | 50 |
| 黑雾矩阵 | 150 |
| 硅基神经元 | 100 |
| 负熵奇点 | 100 |
| 物质重组器 | 100 |
| 核心素 | 300 |

可参与兑换的六种科学矩阵：

| 科学矩阵 | 默认算力 |
|---|---|
| 电磁矩阵 | 50 |
| 能量矩阵 | 100 |
| 结构矩阵 | 250 |
| 信息矩阵 | 750 |
| 引力矩阵 | 1500 |
| 宇宙矩阵 | 3000 |

以上数值参考来源：

1.游戏内的黑雾通讯器休战时长（元数据之间等价数值确定）。

2.元宇宙（白糖元数据）兑换各种黑雾材料的比例（用于衡量各种黑雾材料的价值），其中硅基神经元、负熵奇点、物质重组器三种被认为是等价的。

## 算力规则

算力以**每分钟**计。整个 MOD 只有三个公式：

```text
每分钟算力（每种材料各拿一份） = U × Y × SpeedMultiplier
需要消耗的矩阵点数             = 材料种数 × 每分钟算力
产出 1 个材料                  = 需要 X × 该材料算力 点
```

| 符号 | 含义 | 配置项 | 默认值 |
|---|---|---|---|
| `U` | 矩阵转化算力速度（**每分钟**算力） | `MatrixPointPerMinute` | **7200** |
| `Y` | 矩阵转化算力速度倍率，**百分数**（`1500%` = 15 倍） | `UseGameTechSpeed` / `TechSpeedPercent` | 自动取存档值 |
| `X` | 兑换系数 | `ExchangeFactor` | 1 |
| — | 调试倍率 | `SpeedMultiplier` | 1 |

> **`U` 是每分钟口径**，所以公式里**不再乘任何帧率系数**。
> 例如 `U=7200`、`Y=1500%` ⇒ 每分钟算力 `= 7200 × 1500% = 108000`。
> （`Y=1500%` 即 15 倍，与游戏面板「科技研究速度 1500%」一致。）

**矩阵消耗速度**：每分钟要花掉 `材料种数 × 每分钟算力` 点矩阵点数，
再按第 1 格矩阵的算力折算成矩阵个数。

> **升级「研究速度」科技可以提高算力。** `Y` 默认自动读取存档里的研究速度，
> 每升一级研究速度，本塔每分钟算力就按比例提高。悬停塔窗口的 `?` 可以看到
> **当前 `Y`（百分数）与对应的每分钟算力**。

### 每种材料的速度相互独立

第 2 格起的每一种材料都**单独拿一份**每分钟算力，互不挤占：

| 塔里的材料种数 | 每种材料的速率 | 矩阵消耗 |
|---|---|---|
| 1 种 | 1 倍速 | 1 倍 |
| 2 种 | 各自 1 倍速 | 2 倍 |
| 3 种 | 各自 1 倍速 | 3 倍 |

也就是说，**放的材料种类越多，产能越高，但矩阵也消耗得越快**。

### 算力结余不结转

一帧花不完的算力直接作废（`unusedPoints`），不会留到下一帧。

唯一的例外是"本帧配额**买不起 1 个材料**"的情况：这时会把零头**按槽位**攒起来，
凑够 1 个再产出。这是必须的——否则单帧算力太小（如 `U=120`、`Y=100%` ⇒ 每分钟只有 120 点、
单帧仅 0.033 点）时，核心素（300 点）这类材料**永远产出 0 个**。

槽位空着时每个槽位最多攒 64 个材料的点数；槽位满了只留 1 个的点数，
避免长期堵塞把累积量堆到天文数字。

把 `AccumulatePartialPoints` 设为 `false` 就变成**严格按本帧配额结算**。

### 材料槽位全满时不消耗矩阵

如果第 2 格起的所有材料槽位都满了（没有空间放产出），
本帧**不会扣除矩阵**，避免矩阵白白消失。

### 增产剂加成

第 1 格的**科学矩阵喷过增产剂**（带增产点数）时，**同一份矩阵会多产出材料**，
矩阵消耗量不变 —— 这就是游戏里「增产」的语义：同样的输入，多出产品。

加成比例取自游戏自身的增产表（`Cargo.incTableMilli`，等级 0–10）：

| 增产等级 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| 产出加成 | +12.5% | +20% | +22.5% | +25% | +27.5% | +30% | +32.5% | +35% | +37.5% | +40% |

等级的判定方式与游戏引擎一致：**累计增产点数 ÷ 物品数**（向下取整）。

因此它是**整栈的平均等级**：整栈喷同一等级时等级最准确；混着放会按点数平均后取整。
例如 100 个里 50 个喷到等级 6，`inc = 300`、`count = 100`，等级就是 3（而不是 6）。

从黑雾工坊取走矩阵时，**增产点数按引擎同样的算法等比扣减，等级保持不变**，
不会被清零或降级。

> 例：核心素（300 点）在 `U=7200`、`Y=100%`（每分钟 7200 点）时是 24 个/分钟；
> 矩阵喷了 Mk.II（等级 2，+20%）后变成 **28.8 个/分钟**，而矩阵消耗不变。

### 举例

默认 `U=7200`、`Y=100%`、`X=1` 时，每种材料每分钟 7200 点：

| 材料 | 单价 | 速率 |
|---|---|---|
| 能量碎片 | 50 | 144 个/分钟 |
| 黑雾矩阵 | 150 | 48 个/分钟 |
| 硅基神经元 | 100 | 72 个/分钟 |
| 负熵奇点 | 100 | 72 个/分钟 |
| 物质重组器 | 100 | 72 个/分钟 |
| 核心素 | 300 | 24 个/分钟 |

- 塔里同时放 3 种材料 ⇒ **每种都是上表的速率**，但矩阵消耗也是 3 倍。
- `X=10`：1 个电磁矩阵（50 点）只能换 `50/10 = 5` 点，即 0.1 个能量碎片（50 点）。
- 研究速度越高越快：`Y=1500%` 时每分钟算力 `= 7200 × 1500% = 108000`，
  上表速率整体变成 **15 倍**（核心素 360 个/分钟、能量碎片 2160 个/分钟）。

## 配置文件

第一次进游戏后在下面这个位置生成cfg，**全部数值都可改**，改完重进游戏生效：

```
BepInEx\config\TTenYX.DSP.DarkFogWorkshop.cfg
```

### `[1. 通用]`

| 配置项 | 默认 | 说明 |
|---|---|---|
| `ExchangeFactor` | `1` | 兑换系数 `X`。可小数（如 0.5、2.5），必须 > 0 |
| `MatrixPointPerMinute` | `7200` | 矩阵转化算力速度 `U`（**每分钟**算力，正整数）。最终 **每分钟算力 = `U × Y`**，公式里不再乘帧率系数 |
| `TechSpeedPercent` | `100` | 矩阵转化算力速度 `Y` 的默认百分比（100 = 100%） |
| `UseGameTechSpeed` | `true` | `true` 时 `Y` 自动取存档的研究速度；`false` 时用 `TechSpeedPercent` |
| `SpeedMultiplier` | `1` | 调试倍率，最终每分钟算力 = `U × Y × 本项` |
| `AccumulatePartialPoints` | `true` | 单帧算力买不起 1 个材料时，是否攒零头凑够 1 个 |
| `VerboseLog` | `false` | 详细诊断日志：每次兑换逐槽位打印物品/数量/上限/单价/配额/产出 |
| `OutputSlotLimit` | `6` | 参与兑换的材料槽位上限（第几格），范围 2–20 |

### `[2. 科学矩阵算力]`

六种矩阵各一项，默认值见前面表格。必须是**正整数**。

| 配置项 | 默认 |
|---|---|
| `ElectromagneticMatrix` | `50` |
| `EnergyMatrix` | `100` |
| `StructureMatrix` | `250` |
| `InformationMatrix` | `750` |
| `GravityMatrix` | `1500` |
| `UniverseMatrix` | `3000` |

### `[3. 黑雾材料算力]`

六种材料各一项，默认值见前面表格。必须是**正整数**。

| 配置项 | 默认 |
|---|---|
| `EnergyFragment` | `50` |
| `DarkFogMatrix` | `150` |
| `SiliconNeuron` | `100` |
| `NegentropySingularity` | `100` |
| `MatterReassembler` | `100` |
| `CoreElement` | `300` |

### `[4. 界面]`

| 配置项 | 默认 | 说明 |
|---|---|---|
| `DiagnosticLog` | `false` | 把每座塔每 2 秒的兑换判定过程写入日志，排查"不兑换"用 |

### 常见调参

| 想要的效果 | 怎么改 |
|---|---|
| 整体加快/变慢 | 调 `MatrixPointPerMinute`（`U`，每分钟算力） |
| 只要某一种材料更快 | 调低那一种材料的算力值 |
| 让矩阵更"耐用" | 调大矩阵算力值，或把 `X` 调小 |
| 只让前几格参与 | 把 `OutputSlotLimit` 调小（最小 2） |
| 排查"某材料不产出" | 先把 `VerboseLog` 改成 `true` 看逐槽位日志 |

## 界面

塔的配置窗口标题栏**关闭按钮的正下方**有一个 **`?` 提示控件**，
只在塔满足"黑雾工坊"条件时出现。

鼠标悬停（**不需要点击**）会弹出提示，标题为 **`黑雾工坊 DarkFogWorkshop`**，内容包括：

1. **第 1 槽位（消耗）**：矩阵名 + 消耗速度（个/分钟）+ 合计
2. **矩阵转化算力速度 `Y`（百分数，如 `1500%`）与对应的每分钟算力**
   （显示为 `U 7200 × Y 1500% = 108000`），并注明升级「研究速度」科技可以提高算力
3. **增产状态**：矩阵喷了增产剂时显示「增产<sup>额外产出</sup>：+20%（等级 2）→ ×1.2」
   （"额外产出"用游戏自身的增产蓝 `#61D8FF`）；
   没喷时提示"科学矩阵喷涂增产剂可获得额外产出"
4. **各产出槽位**：材料名 + 生成速度（个/分钟，**已含增产加成**）
5. **算力表**：科学矩阵与黑雾材料各自的算力一览
6. 一行提示：各物品算力可在配置文件中修改

它是纯提示，**点击没有任何反应**，也不会在启停时弹窗。

## 兼容性

- **不修改任何塔名**，不会碰你的命名。
- **不改变物流塔耗电**，功率与原版一致。
- 消耗与产出都会计入统计面板的生产 / 消耗。
- 只判断**第 1 格**与**第 2 格起**，因此与"运输塔扩容"类 MOD 兼容。
- 只有第 1 格被设为**本地需求**、且里面是**科学矩阵**时才会触发，
  不会影响普通的供需塔（星际塔的第 5 格是翘曲器，与本 MOD 无关）。

## 安装

1. 安装 **BepInEx 5**（x64）。
2. 把 `DarkFogWorkshop.dll` 放到 `BepInEx\plugins\`。
3. 启动游戏，配置窗口会显示 `?` 提示控件即安装成功。

用 r2modman / Thunderstore Mod Manager 安装则无需手动操作。

## 编译

```powershell
dotnet build 'DSP-DarkFogWorkshop\DarkFogWorkshop.csproj' -c Release
```

- 目标框架 `netstandard2.1`，产物 `bin\Release\netstandard2.1\DarkFogWorkshop.dll`。
- 依赖 `DysonSphereProgram.GameLibs` 0.10.35.29088-r.0，编译时游戏本体无需安装。
- 编译后会自动部署到 r2modman 的 profile（游戏运行中会部署失败，关掉游戏再编译）。

## 更新日志

### 1.0.1

- **修正 Thunderstore 依赖声明**：由通用的 `BepInEx-BepInExPack-5.4.2100` 改为
  DSP 社区专用的 **`xiaoye97-BepInEx-5.4.17`**。原先的写法会让 r2modman
  在 DSP 配置里拉取错误的 BepInEx。
- 补充 `website_url`。功能与 1.0.0 相同。

### 1.0.0 — 首个发布版

- 用物流塔把**科学矩阵**兑换成**黑雾材料**。
- 第 1 格放科学矩阵并勾选**本地需求**、第 2 格起放黑雾材料即可生效。
- **每种材料各自独立结算**，互不挤占；矩阵消耗随材料种数成倍增长。
- 材料槽位全部占满时**不消耗矩阵**。
- 算力结余不结转，仅按槽位攒"买不起 1 个"的零头。
- 全部数值可在配置文件中调整。
- 物流塔窗口新增 **`?` 悬停提示**，显示每分钟的消耗与产出速率。
- **不修改塔名**，**不改变耗电**。

---
## English summary

**DarkFogWorkshop** lets a logistic station convert **science matrices** into
**Dark Fog materials**. Put a matrix in **slot 1** and set it to **Local Demand**;
put one or more Dark Fog materials in **slots 2+**. The station then consumes the
matrix and produces every listed material **independently** (not split between them).

```text
per-minute credit per material = U × Y × SpeedMultiplier
matrix points consumed         = material kinds × per-minute credit
cost of 1 material             = X × that material's point value
```

Defaults: `U = 7200` (`MatrixPointPerMinute`, **already a per-minute value** — no frame-rate
factor is applied), `Y` = game research speed **as a percentage** (`1500%` = 15x; upgrade the
**Research Speed** tech to raise it), `X = 1` (`ExchangeFactor`).
Example: `U=7200`, `Y=1500%` ⇒ `7200 × 1500% = 108000` points/min.
All values are configurable in `BepInEx\config\TTenYX.DSP.DarkFogWorkshop.cfg`.

If the **slot-1 matrices carry proliferator points**, the same matrix input
yields **more output** (up to +40% at proliferator level 10); the matrix
consumption stays the same. The bonus table is the game's own
`Cargo.incTableMilli`, and the level is `total proliferator points ÷ item count`,
exactly as the game's assemblers compute it.

The mod does **not** rename stations, does **not** change power consumption,
and only acts on stations whose slot 1 holds a science matrix set to Local Demand.

Hover the **`?`** button below the close button of a station window to see the
current research-speed multiplier, the proliferator status, and the per-minute
consumption and production rates.
