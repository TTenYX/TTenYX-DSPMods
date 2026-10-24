# SmartDustbin 垃圾桶

给《戴森球计划》加一组「垃圾桶」：把多余的物品按原版已有的开关直接消耗掉。
无需任何前置 MOD，不写入存档（判定条件全部是原版状态），卸载本 MOD 后一切还原。

游戏版本 0.10.35 · BepInEx 5 · 目标框架 netstandard2.1 · 作者 TTenYX

## 功能 1：储物仓 / 储液罐

**小型储物仓 / 大型储物仓**：把仓储窗口的「自动化输入限制」滑条拉到 0（即可用格数 0）时，储物仓变为垃圾桶，消耗所有输入的物品。输入形式包括：

- 手动放入（拖放、Ctrl 点击快速入库）；
- 分拣器输入（含开采器、无人机送货）；
- 储物仓上方的传送带末端落料、四向分流器落料。

判定条件就是滑条位置本身：拉回大于 0 的任意值立即恢复普通储物仓。
仓内已有的存量不会被清空，随时可以取回。

**储液罐**：把罐体窗口的「输入」开关设为关时，储液罐变为垃圾桶，消耗所有经传送带和手动注入的液体。只消耗液体类物品；固体货物照常通过，不会被误吞。
罐内已有存量不动：仍可手动取出或经输出侧排出。

## 功能 2：传送带图标标签（SIGNAL-404）

给传送带设置图标标签信号 **SIGNAL-404**（原版信号列表里的「404」图标）后，消耗经过该传送带的物品：

| 标签数量 | 行为 |
|---|---|
| 0（默认） | 全部消耗 |
| N &gt; 0 | 每统计周期消耗 N 个 |

- 数量在标签图标旁的输入框填写。例如填 `000600`，游戏内显示 `600`，即每分钟消耗 600 个。
- 速率按**物品个数**计，实际以**整堆货物**为单位扣减（一堆最多 4 个）：全是 4 堆叠货物时，600 个/分钟平均只需消耗约 150 堆，误差不超过一堆。
- 消耗量计入产量统计的「消耗」，可在统计面板观察实际效率。
- 蓝图复制粘贴带标签的传送带同样生效。

## 配置

全部配置在 `BepInEx\config\TTenYX.DSP.SmartDustbin.cfg`，修改后立即生效，无需重启：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| EnableStorageVoid | true | 是否启用功能 1 |
| EnableSmallStorage | true | 功能 1 是否对小型储物仓生效 |
| EnableLargeStorage | true | 功能 1 是否对大型储物仓生效 |
| EnableTank | true | 功能 1 是否对储液罐生效 |
| EnableBeltSignalVoid | true | 是否启用功能 2 |
| StatPeriodSeconds | 60 | 消耗速率的统计周期（秒），默认 1 分钟 |

## 构建

需要 .NET SDK，游戏程序集由 NuGet 包 `DysonSphereProgram.GameLibs`（0.10.35.29088-r.0，来自 BepInEx 源）提供，不依赖游戏本体：

```powershell
dotnet build SmartDustbin\SmartDustbin.csproj -c Release
```

产物：`SmartDustbin\bin\Release\netstandard2.1\SmartDustbin.dll`。
编译成功后会自动部署到 r2modman profile「TEST」（可用 `ProfileDir` 属性覆盖）；也可手动运行 `tools\deploy.ps1`。

无外网时先执行一次 `tools\export-offline-feed.ps1` 导出离线包源。

## 实现要点（维护者向）

- **储物仓**：prefix 拦截 `StorageComponent.AddItem`（计数/范围两个重载）、`AddCargo`、`AddItemStacked` 四条入库路径，条件为 `bans >= size` 且建筑原型为 1101/1102。判定结果按储物仓对象弱引用缓存；补丁位于调用方 `PlanetFactory.InsertInto*` 的 `lastFullItem` 门控之下，返回「全部收下」即不会让门控置位，配置热切换时再主动 `ResetOptimizationFlags()` 清理脏缓存。
- **储液罐**：prefix 在 `TankComponent.GameTick` 里对输入侧皮带执行与原版同款的 `TryPickItemAtRear`（液体掩码），取到即消耗；原方法照常运行（输入开关为关时其输入分支本就不执行）。手动注入由 `UITankWindow._OnUpdate` prefix 按原版 1~2 个/次的节奏从手上消耗。
- **传送带**：每 tick 对每个工厂环形扫描 16 条带，以 `entitySignPool[entityId].iconId0 == 404` 为唯一事实来源自愈式登记（蓝图粘贴直接写 `iconId0`，不走 `SetBeltSignalIcon`，事件法会漏）。限速模式按「预算 = 配额 × 经过 tick 数 / 周期 tick 数」连续补充，上限为一个周期配额。逻辑 tick 钩子在 `GameLogic.LogicFrame()` postfix（0.10.35 逻辑重构后原 `GameData.GameTick` 已不存在）。
- 参考实现：soarqin/DSP_Mods 的 Dustbin（AddCargo 返回语义、取货窗口、消耗统计写法）。

## 许可

未附带许可证文件。如需授权他人使用、修改或再分发，请先补充 `LICENSE`。
