# DarkFogWorkshop 黑雾工坊

用物流塔把**科学矩阵**（糖）自动转化为**黑雾材料**。

需要 **BepInEx 5** · 适配游戏 **0.10.35** · 作者 **TTenYX**

---

## 这个 MOD 解决什么问题

原版拿黑雾材料只有两条路：击杀黑雾后拾取，或者手动用元数据兑换。前者有人不想开黑雾、也有人嫌卡顿；后者无法自动化，手动操作成本高。

装了这个 MOD，**往物流塔里塞矩阵，塔就会持续产出黑雾材料**，完全自动化；还可以用元数据自动补矩阵，彻底免维护。

## 玩法 1：把塔配成黑雾工坊

| 槽位 | 放什么 | 设置 |
|---|---|---|
| **第 1 格** | 六种科学矩阵之一 | 必须选 **本地需求** |
| **第 2 格起** | 一种或多种黑雾材料 | 供应 / 需求随意 |

设置好后，塔会持续消耗第 1 格的矩阵，并为每一种材料**各自独立**产出：放 3 种材料就是 3 倍产量，矩阵消耗同样是 3 倍。

> 只有第 1 格是科学矩阵且选择了「本地需求」时才会触发，普通物流塔完全不受影响。

**悬停塔窗口右上角的 `?`**，可随时查看当前每分钟的消耗与产出。

### 可以兑换的材料

| 科学矩阵 | 单价 | 黑雾材料 | 单价 |
|---|---|---|---|
| 电磁矩阵 | 50 | 能量碎片 | 50 |
| 能量矩阵 | 100 | 黑雾矩阵 | 150 |
| 结构矩阵 | 250 | 硅基神经元 | 100 |
| 信息矩阵 | 750 | 负熵奇点 | 100 |
| 引力矩阵 | 1500 | 物质重组器 | 100 |
| 宇宙矩阵 | 3000 | 核心素 | 300 |

单价即算力点数，全部可在配置文件中修改。

## 玩法 2：用元数据自动补矩阵

不想手动补矩阵？让塔自己用元数据兑换：

1. 第 1 格放科学矩阵，槽位设为 **本地仓储**（注意：不是本地需求）。
2. 打开塔窗口，勾选第一槽位上方的「**元数据兑换科学矩阵**」。
3. 塔会按 **600 个/分钟**（可配置）自动兑换矩阵并补进第 1 格。

- 消耗当前账号的元数据余额；取消勾选立即停止。
- 可与玩法 1 叠加在同一座塔：第 2 格起放材料并设供应，即形成「元数据 → 矩阵 → 黑雾材料」的全自动产线。
- 多座塔同时勾选时，各自独立累积速率、共同消耗元数据余额，不会重复兑换。

> 小贴士：物流塔槽位的「选择物品」界面里，六种黑雾材料会**直接出现**（原版需要拾取解锁后才显示），可直接选为供应物品。

## 产出速度怎么算

速度均按**每分钟**计，只有三个公式：

```text
每分钟算力（每种材料各一份） = U × Y
消耗矩阵点数               = 材料种数 × 每分钟算力
产出 1 个材料所需点数       = X × 该材料单价
```

| 符号 | 含义 | 配置项 | 默认 |
|---|---|---|---|
| `U` | 基础算力（每分钟） | `MatrixPointPerMinute` | 7200 |
| `Y` | 研究速度倍率（%），自动读取存档 | `UseGameTechSpeed` | 存档值 |
| `X` | 兑换系数 | `ExchangeFactor` | 1 |

默认 `U = 7200`、`Y = 100%` 时，每种材料每分钟产出：

| 能量碎片 | 黑雾矩阵 | 硅基神经元 | 负熵奇点 | 物质重组器 | 核心素 |
|---|---|---|---|---|---|
| 144 | 48 | 72 | 72 | 72 | 24 |

升级游戏里的「**研究速度**」科技会按比例提高 `Y`，产量同比提升（`Y = 1500%` 时就是上表的 15 倍）。

### 增产剂加成

第 1 格的**矩阵喷过增产剂**时，同样的矩阵会**多产出材料**，矩阵消耗不变——最高 +40%（增产等级 10）。

### 其他规则

- 材料槽位**全部满时不会扣矩阵**，避免浪费。
- 单帧算力买不起 1 个材料时，零头会攒在该槽位，凑够 1 个再产出。
- 消耗与产出都计入统计面板。

## 配置

首次进游戏后生成 `BepInEx\config\TTenYX.DSP.DarkFogWorkshop.cfg`，**全部数值可改**，重启游戏生效。

| 常用配置项 | 默认 | 说明 |
|---|---|---|
| `MatrixPointPerMinute` | 7200 | 基础算力 `U`，控制整体快慢 |
| `ExchangeFactor` | 1 | 兑换系数 `X` |
| `OutputSlotLimit` | 6 | 参与产出的材料槽位上限（2–20） |
| `MetadataMatrixPerMinute` | 600 | 元数据兑换速率（个/分钟） |
| `VerboseLog` | false | 排查「不产出」时开启逐槽位日志 |

配置文件中另有六种矩阵与六种材料各自的单价。

## 兼容性

- 不修改塔名、不改变耗电、不修改游戏原型数据。
- 与运输塔扩容类 MOD 兼容（配合 `OutputSlotLimit` 使用）。
- OrbitalRing：其共享仓储窗口发生数组越界时兜底放行，不会报错锁死。
- TowerFactory：两者处理的塔互不重叠，互不干扰。
- 只在物流塔槽位的选物品界面补显黑雾材料，其他界面保持原版。

## 安装

1. 安装 **BepInEx 5**（x64）。
2. 把 `DarkFogWorkshop.dll` 放进 `BepInEx\plugins\`。
3. 启动游戏即可；用 r2modman / Thunderstore Mod Manager 安装则无需手动操作。

## 更新日志

**1.1.3** — 重写说明文档，重点改为玩法与使用方式；精简更新日志。

**1.1.0** — 新增元数据自动兑换矩阵、选物品界面显示黑雾材料；兼容 OrbitalRing；修复矩阵未写入物流塔。

**1.0.1** — 修正依赖声明。

**1.0.0** — 首个发布版。

---

## English

**DarkFogWorkshop** turns a logistics station into a workshop that converts **science matrices** into **Dark Fog materials** automatically.

**Play 1:** put a science matrix in **slot 1** and set it to **Local Demand**; put Dark Fog materials in **slots 2+**. Each material is produced independently (3 kinds = 3× output and 3× matrix consumption). Hover the **`?`** button in the station window for live per-minute rates.

**Play 2:** set slot 1 to **Local Storage** and tick **元数据兑换科学矩阵** above slot 1 (600 items/min by default) to refill the matrix from your metadata balance.

**Rates:** per-material credit per minute = `U × Y`; consumed matrix points = kinds × credit; 1 item costs `X × its point value`. `U` defaults to 7200 (`MatrixPointPerMinute`), `Y` follows the in-game **Research Speed** percentage, `X` is 1 (`ExchangeFactor`); all values are configurable in `BepInEx\config\TTenYX.DSP.DarkFogWorkshop.cfg`.

Proliferated matrices in slot 1 yield more output (up to +40%) with the same matrix consumption. The mod does not rename stations, change power usage, or modify game prototype data.
