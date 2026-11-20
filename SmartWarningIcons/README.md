# SmartWarningIcons 智能警告图标

给《戴森球计划》把原版笼统的「不工作」警告拆成两种一眼可辨的专用状态，外加可配置的矿脉枯竭警告：

1. **缺原料（红色图标）**：建筑设了配方、因缺原料停工时，显示红色「感叹号 + 敞口空料盒」图标，和原版笼统的「不工作」图标区分开。
2. **产物堆砌（黄色图标）**：生产设施的产物缓冲区装不下已完成的产物，导致生产停住时才显示堆料黄标。仍能生产时，即使已有积货也不显示。
3. **矿脉枯竭警告可配置**：矿机所采矿脉量低于阈值时显示「即将减产」；支持按**矿脉总量**（默认）或**单矿点最小量**判断，抽水机、抽油机等非矿脉矿机不会误报，供电类警告仍然优先显示。

设置界面（选项 → 其他设置）有红、黄两个独立开关，只控制建筑的告警图标。顶部汇总是否计入某建筑，由该建筑自己的「警报开关」控制。

- 已验证游戏版本：0.10.35.29151（BepInEx 5）。MOD 不影响存档。
- **不要与 Raptor 的 BetterWarningIcons 同时安装**：两者接管同一套警告逻辑，同时装会互相覆盖。

## 配置

配置文件 `BepInEx\\config\\TTenYX.DSP.SmartWarningIcons.cfg`，修改后重启游戏生效：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| InsufficientInput / Enable | true | 启用红色「缺原料」专用图标 |
| OutputPile / Enable | true | 启用黄色「产物堆砌」专用图标（制造台 + 生产模式研究站） |
| VeinDepletion / Enable | true | 接管矿脉枯竭警告（阈值与判断方式按下面两项生效） |
| VeinDepletion / UseTotalVeinAmount | true | true = 按矿机覆盖的所有矿点总量判断；false = 按单个最小矿点的量判断 |
| VeinDepletion / WarnThreshold | 1000 | 判断量低于该值时显示「即将减产」 |

游戏版本 0.10.35 · BepInEx 5 · 作者 TTenYX

## 致谢与来源

本 MOD 的**思路**来自 Raptor 的 [BetterWarningIcons](https://github.com/Velociraptor115-DSPModding/DSPMods)——该 MOD 从游戏 0.10.34 起停止适配（社区修复 PR [Velociraptor115-DSPModding/DSPMods#34](https://github.com/Velociraptor115-DSPModding/DSPMods/pull/34) 已提交、尚未合并）。由于原仓库未附带开源许可证，本插件为**独立重写实现**：全部代码与本图标素材均为原创，未复制上游的代码或美术资源。

---

# SmartWarningIcons

Splits Dyson Sphere Program's generic "not working" warning into two instantly distinguishable states, plus a configurable vein-depletion warning:

1. **Insufficient Input (red icon)**: when a building has a recipe set but stops for lack of input materials, it shows a red "exclamation + open crate" icon, distinct from the generic "not working" icon.
2. **Output Piled Up (yellow icon)**: the yellow pile-up sign appears only when a production building's output buffer cannot hold its finished products and production comes to a stop. As long as the building can keep producing, the sign stays off even with items already buffered.
3. **Configurable vein-depletion warning**: shows "cut production soon" when the amount a miner is working falls below a threshold; judge by the **total vein amount** (default) or the smallest single vein spot. Water pumps, oil extractors and other non-vein miners never trigger it, and power warnings keep priority.

The options window (Options → Other) has separate red and yellow toggles that control only the warning icons above buildings. Whether a building counts toward the warning summary at the top of the screen is decided by that building's own alarm switch.

- Verified on game version 0.10.35.29151 (BepInEx 5). The mod does not affect saves.
- **Do not install together with Raptor's BetterWarningIcons**: both mods take over the same warning logic and would overwrite each other.

## Config

Config file `BepInEx\\config\\TTenYX.DSP.SmartWarningIcons.cfg`, restart the game after editing:

| Entry | Default | Notes |
|---|---|---|
| InsufficientInput / Enable | true | Red insufficient-input sign |
| OutputPile / Enable | true | Yellow output-piled-up sign (assemblers + labs in production mode) |
| VeinDepletion / Enable | true | Take over the vein-depletion warning (threshold and mode follow the two entries below) |
| VeinDepletion / UseTotalVeinAmount | true | true = judge by the total amount over all vein spots the miner covers; false = by the smallest single spot |
| VeinDepletion / WarnThreshold | 1000 | Show "cut production soon" when the compared amount falls below this value |

Game 0.10.35 · BepInEx 5 · by TTenYX

## Credits

The **idea** for this mod comes from Raptor's [BetterWarningIcons](https://github.com/Velociraptor115-DSPModding/DSPMods) — that mod stopped being adapted as of game 0.10.34 (community fix submitted as [PR #34](https://github.com/Velociraptor115-DSPModding/DSPMods/pull/34), not yet merged). Because the upstream repository ships no license, this plugin is an **independent reimplementation**: all code and icon artwork here are original, with no upstream code or assets copied.
