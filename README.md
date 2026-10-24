# TTenYX-DSPMods

《戴森球计划》（Dyson Sphere Program）的个人 mod 源码合集。

| Mod | 说明 | 版本 |
|---|---|---|
| [DarkFogWorkshop](DarkFogWorkshop/) | 黑雾工坊 —— 物流塔用科学矩阵自动产出黑雾材料 | v1.0.0 |
| [SmartDustbin](SmartDustbin/) | 垃圾桶 —— 储物仓/储液罐/传送带标签按条件消耗物品 | v0.1.0 |
| [ControlPanelPlus](ControlPanelPlus/) | 物流总控面板优化 —— 展开/折叠、星图定位、物流塔设置复制粘贴、筛选交集、排序热键、非本地取放、存储模式快捷键 | v0.1.4 |

---

## DarkFogWorkshop 黑雾工坊

**用科学矩阵，自动化生产黑雾材料。**

不加配方，不加建筑，不改动任何原版数据 —— 只给已有的**物流运输站**加一条新产线。

### 生效方式

一座物流运输站满足以下条件时自动生效：

1. **第 1 格**放入**科学矩阵**（电磁 / 能量 / 结构 / 信息 / 引力 / 宇宙）；
2. 第 1 格设置为**本地需求**；
3. **第 2 格起**放入想产出的**黑雾材料**，一格一种。

触发条件要求「第 1 格是科学矩阵」且「第 1 格设为本地需求」，两者缺一不可。
条件不满足的塔完全不受影响。

### 生效后有游戏内提示

物流塔配置窗口的**关闭按钮正下方会出现 `?`**，**鼠标悬停**即弹出实时账本：
矩阵转化算力速度、每分钟算力、各槽位的消耗速度与转化速度、全部物品的算力对照表。

### 算力算法

```text
每分钟算力（点）      = U × Y
单种材料生成速率      = 每分钟算力 ÷ 该材料算力          （个/分钟）
矩阵消耗速率          = 材料种数 × 每分钟算力 ÷ 矩阵算力  （个/分钟）
```

`U` = 黑雾工坊初始算力（配置项 `MatrixPointPerMinute`，默认 `7200`）；
`Y` = 矩阵转化算力速度，默认取存档的研究速度。

完整说明见 [DarkFogWorkshop/README.md](DarkFogWorkshop/README.md)。

---

## SmartDustbin 垃圾桶

**把原版已有的开关变成垃圾桶，按条件消耗输入物品。** 无需前置 MOD，不写入存档。

- **储物仓**：小型/大型储物仓把「自动化输入限制」滑条拉到 0 时，消耗所有输入的物品——手动放入、分拣器、开采器、无人机，以及储物仓上方的传送带末端与四向分流器落料。
- **储液罐**：「输入」开关设为关时，消耗所有经传送带和手动注入的液体（只消耗液体类物品；固体货物照常通过）。
- **传送带**：给传送带设置图标标签信号 **SIGNAL-404** 后消耗经过的物品。标签数量 0（默认）= 全部消耗；填 N = 每统计周期（默认 1 分钟）消耗 N 个。速率按物品个数计、以整堆货物为单位扣减（一堆最多 4 个）。

消耗量计入产量统计的「消耗」。全部开关与统计周期可在 cfg 中配置并热生效。
完整说明见 [SmartDustbin/README.md](SmartDustbin/README.md)。

---

## 构建

需要 .NET SDK（目标框架 `netstandard2.1`）：

```powershell
dotnet build DarkFogWorkshop\DarkFogWorkshop.csproj -c Release
```

产物：`DarkFogWorkshop\bin\Release\netstandard2.1\DarkFogWorkshop.dll`，
复制到 `BepInEx\plugins\` 即可。

### ⚠️ 关于 `DysonSphereProgram.GameLibs`

该包是**从游戏本体派生的程序集**，不在 nuget.org 上（那里只有一个占位版本），
也无法随仓库分发 —— 所以本仓库**无法做到 clone 后直接编译**，这是 DSP mod 开发的常态。

要编译，需要本机已有该包（装过 DSP mod 开发环境的话，它就在 NuGet 缓存里）。
`NuGet.config` 已把项目下的 `offline-feed\` 列为可选包源，无外网时执行一次：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File DarkFogWorkshop\tools\export-offline-feed.ps1
```

即可把缓存中的 `.nupkg` 导出到 `offline-feed\` 做离线编译。
该目录已被 `.gitignore` 忽略，不会提交。

其余依赖（BepInEx、HarmonyX、UnityEngine.Modules、Mono.Cecil）均可从 nuget.org 还原。

### 部署（可选）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File DarkFogWorkshop\tools\deploy.ps1
```

默认部署到 r2modman profile「0.10.35 new」；路径可用 `DarkFogWorkshop.csproj` 里的
`ProfileDir` 属性覆盖。

---

## 仓库结构

```text
TTenYX-DSPMods/
├── README.md
├── .gitignore
└── DarkFogWorkshop/
    ├── DarkFogWorkshop.cs          插件入口
    ├── DarkFogWorkshopConfig.cs    全部配置项
    ├── DarkFogWorkshopPatch.cs     算力结算核心逻辑
    ├── UIStationWindowPatch.cs     悬停提示控件
    ├── manifest.json               Thunderstore 清单
    ├── README.md                   mod 说明
    ├── README-thunderstore.md      Thunderstore 页面用简介
    ├── CHANGELOG.md
    ├── icon.png
    └── tools/
        ├── deploy.ps1              部署到 r2modman profile
        ├── export-offline-feed.ps1 离线包源导出
        └── make_icon.py            图标生成
```

## 许可

未附带许可证文件。如需授权他人使用、修改或再分发，请先补充 `LICENSE`。

## 环境

- 游戏版本 **0.10.35**
- **BepInEx 5**（x64）
- 目标框架 `netstandard2.1`
