# ControlPanelPlus — 物流总控面板优化

《戴森球计划》0.10.35 的 BepInEx 5 插件，给游戏自带的**物流总控面板**（默认 `I` 打开）增加 8 组功能：展开/折叠策略、星图定位、物流塔设置复制粘贴、筛选交集、位置下拉框排序与热键、筛选快捷操作、非本地星球取放物品、存储模式鼠标快捷键。

需要 BepInEx 5.4.17+。把 `ControlPanelPlus.dll` 放到 `BepInEx/plugins/ControlPanelPlus/` 即可；配置写入 `BepInEx/config/com.dsp.controlpanelplus.cfg`，也可以装 BepInEx.ConfigurationManager 在游戏实时内调整。

界面文字跟随游戏语言：中文游戏显示中文，其他语言显示英文；配置文件说明为中英并列，本 README 提供中英文两版。

## 功能

### F1 星球条目默认展开 / 折叠 + 顶部按钮

- **多星球结果**（查看全星区，或某星系里有多个已建物流设施的星球）→ 打开面板或切换筛选后，所有星球条目**默认折叠**，仅展示满足条件的每个星球。
- **单星球结果**（筛选到某个星球，或某星系只有 1 个星球满足条件）→ 该星球条目**默认展开**。
- 以上折叠展开默认只在**切换筛选 / 打开面板**时套用，建塔、拆塔等列表刷新不会重置手动展开的状态。
- **重新打开面板会保留上次的展开/折叠状态**，优先级高于两个默认开关；状态仍写入游戏自己的 `astroExpands`（随存档保存）。
- 标题栏右上角有一个 **全部展开 / 全部折叠** 切换按钮，实现快速展开/折叠当前界面中所有星球的设施。

### F2 星图定位按钮

每个星球条目的导航按钮**左侧**新增一个「查看星球」的**浅蓝色图标按钮**

鼠标悬浮会弹出提示浮窗（固定三行，宽度随语言自适应），浮窗里列出三种点击方式：

- **左键**：打开星图并保留总控面板（看完返回时仍是原来的筛选/展开状态）；
- **右键**：打开星图并关闭总控面板；
- **Shift+右键**：打开星图、关闭总控面板，并把导航线指向该星球。

### F3 物流塔设置批量复制粘贴

在总控面板中选中一个物流塔后：

| 按键 | 行为 |
|---|---|
| `<`（逗号，可配置） | 复制当前选中物流塔的设置 |
| `>`（句号，可配置） | 粘贴到当前选中的物流塔，可以搭配多选实现批量粘贴 |
| `Ctrl` + 点击条目 | 多选/取消多选（选中的条目会高亮）|
| `Shift` + 点击条目 | 以当前选中条目为起点，批量选中两者之间的所有物流塔（可跨星球） |
| `Shift` + `>` | 批量粘贴到**当前星球**上槽位设置完全相同的所有物流塔 |

普通左键点击会清空多选；切换筛选或关闭面板也会自动清空。

复制内容分三块，各自独立开关（默认全开）：

1. **槽位设置**：每个槽位的物品种类、数量上限、供需关系；运输机/运输船自动填充开关。
2. **塔设置**：充电功率、运输机路程、运输船路程、曲速路程、起送量（%）、集装数量、轨道采集器、翘曲器必备。
3. **优先级设置**：塔名（仅点对点生效时）、星际航路优先级、分组、槽位优先级锁。

第一块与第二块还提供逐项开关：`Slot1`–`Slot5` 可单独控制粘贴时是否写入对应槽位，9 个 `Set*` 开关分别控制塔的每个设置项。
默认全开，关闭后粘贴不会覆盖该项。

安全约定：默认只改设置，**不搬运物品、不清空库存**——目标槽位已有别种物品且数量 > 0 时跳过该槽位；槽位数不足时只处理能对上的部分；大型采矿机 / 轨道采集器默认按缩减范围处理（只复制上限与供需，不复制物品种类）；配送物流、地图标记条目上按 `<`/`>` 无任何效果。

三个可选的粘贴策略（默认均关闭）：

- **强制粘贴**（`StationCopy/ForcePaste`）：目标槽位已有其他物品时直接覆盖，替换发生在原槽位（物流塔输入/输出口的槽位绑定保持不变）；被替换的物品默认取回背包。
- **直接销毁**（`StationCopy/DestroyReplacedItems`）：强制粘贴覆盖槽位或远程清空有物品的槽位时，直接销毁原有物品，而不是取回背包。
- **采矿机溢出**（`StationCopy/AllowMinerOverflow`）：大型采矿机 / 轨道采集器保留来源塔的原始槽位上限（不再缩减到目标塔容量），即允许物品溢出。

### F4 物品筛选并集 → 交集

原版物品筛选是并集（选 A+B 显示含 A **或** 含 B 的条目）。打开开关后改为交集（只显示同时含 A **且** 含 B 的条目），切换后列表立即刷新，默认关闭。

标题栏右上角有 **「筛选：并集 / 筛选：交集」** 按钮，点击即切换。

### F5 位置下拉框排序 / 热键

- **按星系名称排序**（默认开启）：前两项「查看全星系 / 查看当前星球」固定不动，其余按星系名排序，星球跟随其星系。
- **「当前星系」范围**：下拉框第三项新增「当前星系」，选中后只看当前所在星系的所有星球。
- **星系/星球区分**：星系名黄色、星球名缩进两格（与 StatsUITweaks 观感一致，随排序开关生效）。
- **热键**（仅总控面板打开、且没有输入框聚焦时生效）：
  - `PageUp` / `PageDown`：位置下拉框上/下一项；按住 `Ctrl` 只跳星系；这种方式会遍历每一个星球/星系，不论是否有满足条件的条目，不会自动跳过没有匹配条目的星球/星系
  - `←` / `→`：切换到上一个 / 下一个星球的第一个条目；条目类型跟随顶部标签页，自动跳过没有匹配条目的星球/星系；
  - `↑` / `↓`：在当前列表里逐条切换选择；
  - 以上三条的快捷键切换条目均会到首/末条时跨星球首尾循环；
  - **与 UXAssist 共存**：鼠标停在物流塔的存储槽位区域时，方向键优先实现 UXAssist 的存储容量控制；离开槽位后由本 MOD 移动选择。

### F6 物品筛选快捷

- **打开面板自动筛选**：打开总控面板时，自动把鼠标指向的物品设为物品筛选。
- **右键替换筛选**：在总控面板里右键条目中的物品图标，把物品筛选替换为该物品，会清空原筛选。
- **Shift+右键追加筛选**：在现有筛选上追加该物品（最多 5 个，超出时移除最旧的）。

### F7 存储模式鼠标快捷键

在总控面板右侧物流塔存储行上：

| 鼠标 | 行为 |
|---|---|
| 右键点击 | 供/需切换（供应 ↔ 需求；无 → 供应） |
| 中键点击 | 无/需切换（无 ↔ 需求；供应 → 无） |

命中区域与 UITweaks 相同（本地/远程供需按钮）；**若已安装 UITweaks，则以 UITweaks 为准**，本功能自动停用。

### F8 非本地星球取放物品

默认关闭（`Station/AllowRemoteItemTransfer`）。开启后可以在总控面板上直接从**非本地星球**的物流设施中取放物品：右键长按槽位图标拿取、手持同种物品左键放入、清空按钮取回。

**优先级低于 CheatEnabler**：检测到 CheatEnabler 时本功能不生效。

## 配置

配置文件：`BepInEx/config/com.dsp.controlpanelplus.cfg`，修改后重启游戏生效

建议搭配MOD ConfigurationManager 使用，在游戏内实时修改生效

| 配置 | 默认 | 说明 |
|---|---|---|
| `General/Enabled` | true | 总开关 |
| `Expand/CollapseAllDefaultMulti` | true | 多星球结果默认折叠 |
| `Expand/ExpandDefaultSingle` | true | 单星球结果默认展开 |
| `Locate/EnableStarmapButton` | true | 星球条目「定位」按钮 |
| `Station/AllowRemoteItemTransfer` | false | 允许从非本地星球取放物品（CheatEnabler 优先） |
| `StationCopy/CopyKey` | Comma | 复制键（`<`） |
| `StationCopy/PasteKey` | Period | 粘贴键（`>`） |
| `StationCopy/EnableSlots` | true | 复制/粘贴第一块：槽位设置 |
| `StationCopy/Slot1` … `Slot5` | true | 粘贴时是否写入第 1–5 槽位（可单独关闭以保护该槽位） |
| `StationCopy/ForcePaste` | false | 强制粘贴：覆盖已有其他物品的槽位（原槽位绑定保持） |
| `StationCopy/DestroyReplacedItems` | false | 强制粘贴/远程清空时直接销毁被替换物品 |
| `StationCopy/AllowMinerOverflow` | false | 大型采矿机/轨道采集器保留原上限并允许溢出 |
| `StationCopy/EnableSettings` | true | 复制/粘贴第二块：塔设置 |
| `StationCopy/SetChargePower` | true | 粘贴充电功率 |
| `StationCopy/SetTripDrones` / `SetTripShips` | true | 粘贴物流运输机/运输船航程 |
| `StationCopy/SetWarpDistance` | true | 粘贴曲速启用距离（星际站） |
| `StationCopy/SetWarperNecessary` | true | 粘贴「翘曲器必备」开关（星际站） |
| `StationCopy/SetOrbitCollector` | true | 粘贴「包含轨道采集器」开关（星际站） |
| `StationCopy/SetDeliveryDrones` / `SetDeliveryShips` | true | 粘贴配送运输机/运输船数量 |
| `StationCopy/SetPilerCount` | true | 粘贴集装数量 |
| `StationCopy/EnablePriority` | true | 复制/粘贴第三块：优先级设置 |
| `Filter/ItemFilterIntersect` | false | 物品筛选交集模式 |
| `AstroList/SortByName` | true | 星系名称排序 |
| `Hotkeys/ListUp` / `ListDown` | PageUp / PageDown | 下拉框切换热键 |

## 鸣谢

- **UXAssist**（soarqin）：物流控制面板的鼠标交互与「鼠标指向物品自动筛选」的判定思路、存储容量方向键的共存判定。
- **CheatEnabler**（soarqin）：Architect 模式下「非本地星球取放物品」的实现思路（本 MOD 用临时 `isLocal` 覆盖复用原版逻辑）。
- **UITweaks**（starfi5h）：物流塔存储行的右键/中键供需切换功能（本 MOD 按其命中区域与优先级实现）。
- **StatsUITweaks**（starfi5h）：位置下拉框的星系/星球区分（颜色 + 缩进）与按名称排序思路。

## English

ControlPanelPlus is a BepInEx 5 plugin for Dyson Sphere Program 0.10.35 that upgrades the built-in **logistics control panel** (opened with `I`).

Requirements: BepInEx 5.4.17+. Drop `ControlPanelPlus.dll` into `BepInEx/plugins/ControlPanelPlus/`; configuration lives in `BepInEx/config/com.dsp.controlpanelplus.cfg` (or use BepInEx.ConfigurationManager in game).

In-game text follows the game language (Chinese, otherwise English); configuration descriptions are bilingual and this README has both Chinese and English sections.

### Features

- **F1 Expand / collapse** — Multi-planet results collapse by default, single-planet results expand by default; a title-bar toggle button switches all rows; reopening the panel keeps the previous state (stored in the game's `astroExpands`).
- **F2 Locate button** — A small light-blue PlanetFinder-style "View Planet" icon button left of each planet row's navigation button; hovering it shows a three-line tooltip (width adapts to the language) with the three click modes. Left click opens the star map and keeps the panel; right click closes the panel; Shift+right click also points the navigation line at the planet.
- **F3 Copy / paste station settings** — `<` copies the selected station, `>` pastes. `Ctrl`+click multi-selects, `Shift`+click selects a range (across planets), `Shift`+`>` batch-pastes onto all stations on the current planet with identical slot layout. Data is split into three independently switchable blocks (slots, station settings, priority), and the first two blocks have per-item switches (`Slot1`–`Slot5`, nine `Set*` toggles). By default it never moves items or clears inventories; incompatible occupied slots are skipped. Optional strategies (all off by default): `ForcePaste` overwrites a slot that holds a different item in place (keeping the station's input/output slot binding; replaced items go back to the player), `DestroyReplacedItems` destroys those items instead (also for clearing remote slots), and `AllowMinerOverflow` keeps the original slot max for vein collectors / orbital collectors and allows overflow.
- **F4 Item filter intersection** — Turns the vanilla union filter into an intersection; a title-bar button toggles it instantly.
- **F5 Astro list sorting / hotkeys** — Sorts the location dropdown by star name (first two entries pinned), adds a "current star system" scope as the third entry, stars yellow and planets indented; `PageUp`/`PageDown` step the list (Ctrl = stars only), `←`/`→` jump to the previous/next planet, `↑`/`↓` step through the result list.
- **F6 Filter shortcuts** — Auto-filters by the item under the mouse when the panel opens; right-click a station item icon to replace the filter, `Shift`+right-click to append (max 5).
- **F7 Storage mode mouse shortcuts** — On station storage rows: right-click toggles Supply ↔ Demand, middle-click toggles None ↔ Demand. UITweaks takes precedence when installed.
- **F8 Remote item transfer** — Off by default (`Station/AllowRemoteItemTransfer`); when enabled, items can be taken from / placed into stations on other planets. CheatEnabler takes precedence when installed.

### Configuration

| Key | Default | Description |
|---|---|---|
| `General/Enabled` | true | Master switch |
| `Expand/CollapseAllDefaultMulti` | true | Collapse by default for multi-planet results |
| `Expand/ExpandDefaultSingle` | true | Expand by default for a single planet |
| `Locate/EnableStarmapButton` | true | Show the locate button on planet rows |
| `Station/AllowRemoteItemTransfer` | false | Allow remote item transfer (CheatEnabler wins) |
| `StationCopy/CopyKey` / `PasteKey` | Comma / Period | Copy / paste keys |
| `StationCopy/EnableSlots` | true | Block 1: slot settings |
| `StationCopy/Slot1` … `Slot5` | true | Paste gate for slot 1–5 |
| `StationCopy/ForcePaste` | false | Force paste: overwrite slots holding another item |
| `StationCopy/DestroyReplacedItems` | false | Destroy replaced items instead of returning them |
| `StationCopy/AllowMinerOverflow` | false | Keep the original max on miners/collectors and allow overflow |
| `StationCopy/EnableSettings` | true | Block 2: station settings |
| `StationCopy/SetChargePower` | true | Paste charging power |
| `StationCopy/SetTripDrones` / `SetTripShips` | true | Paste drone / ship trip range |
| `StationCopy/SetWarpDistance` | true | Paste warp distance (stellar) |
| `StationCopy/SetWarperNecessary` | true | Paste "warper required" (stellar) |
| `StationCopy/SetOrbitCollector` | true | Paste "include orbital collectors" (stellar) |
| `StationCopy/SetDeliveryDrones` / `SetDeliveryShips` | true | Paste delivery drone / ship counts |
| `StationCopy/SetPilerCount` | true | Paste piler count |
| `StationCopy/EnablePriority` | true | Block 3: priority settings |
| `Filter/ItemFilterIntersect` | false | Item filter intersection mode |
| `AstroList/SortByName` | true | Sort the astro list by star name |
| `Hotkeys/ListUp` / `ListDown` | PageUp / PageDown | Dropdown step keys |

### Credits

- **UXAssist** (soarqin): control-panel mouse interactions, the "filter by item under the mouse" idea and the arrow-key coexistence with storage capacity tweaks.
- **CheatEnabler** (soarqin): the remote item transfer approach in Architect Mode (this mod reuses the vanilla local path via a temporary `isLocal` override).
- **UITweaks** (starfi5h): right/middle-click supply-demand switching on station storage rows (this mod mirrors its hit areas and precedence).
- **StatsUITweaks** (starfi5h): star/planet distinction (colour + indent) and sorting-by-name idea for the location dropdown.
