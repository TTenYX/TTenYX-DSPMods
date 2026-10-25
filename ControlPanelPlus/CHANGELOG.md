# CHANGELOG

## 0.2.6

- 配置界面把 `ForcePaste` / `DestroyReplacedItems` / `AllowMinerOverflow` 三项移到 `StationCopy` 段最上方（按键设置之前）。

## 0.2.5

- 定位按钮改回文字按钮「定位」（英文界面 Locate），移除程序绘制的图标与相关透明/去高亮处理；悬浮提示保留。

## 0.2.4

- 定位按钮图标去除发光：从原图标贴图提取实心图形（alpha 阈值），贴图不可读时改用程序绘制的干净放大镜图标。

## 0.2.3

- 定位按钮取消悬浮高亮与缩放：图标不再随鼠标悬浮变亮或放大。

## 0.2.2

- 定位按钮改为透明底色：只保留图标，鼠标悬浮时显示淡白色反馈。

## 0.2.1

- 粘贴提示区分跳过原因：物品冲突（并提示可在 `StationCopy/ForcePaste` 开启强制覆盖）与目标槽位不足；强制覆盖时显示覆盖数量。
- 批量粘贴提示同样显示物品冲突数量。

## 0.2.0

- 定位按钮：浅蓝色图标底色；悬浮提示固定三行、宽度按语言自适应；打开仪表盘时定位会强制关闭仪表盘（优先于 DashboardOverhaul 的锁定）。
- 新增强制粘贴（`StationCopy/ForcePaste`，默认关闭）：覆盖已有其他物品的槽位，保持原槽位绑定，替换物品默认取回。
- 新增替换物品直接销毁（`StationCopy/DestroyReplacedItems`，默认关闭）：强制粘贴与远程清空有物品槽位时直接销毁原有物品。
- 新增采矿机溢出（`StationCopy/AllowMinerOverflow`，默认关闭）：开启后大型采矿机 / 轨道采集器保留原上限并允许溢出；关闭时显式缩减到目标塔容量（即使 UXAssist 的溢出补丁处于开启状态）。
- 位置下拉框新增「当前星系」范围（第三项）。
- README 按功能重排（存储快捷键改为 F7、非本地取放改为 F8），移除实现细节；新增 AGENTS.md 默认制作规则。

## 0.1.7

- 游戏内界面文字按游戏语言自动切换：中文游戏显示中文，其他语言显示英文（定位按钮浮窗、展开/折叠按钮、筛选模式按钮、复制粘贴与多选提示）。
- 配置文件全部说明改为中英双语并列；README 保持中英文两版。
- 新增 `Lang.T(zh, en)` 统一处理。

## 0.1.6

- 定位按钮改为与 PlanetFinder「view planet」同款的图标按钮（星图「查看星球」图标 + 同款按钮模板），鼠标悬浮显示提示浮窗，浮窗内列出左键/右键/Shift+右键三种行为；功能不变。

## 0.1.5

- 定位按钮改为按点击方式区分：左键保留总控面板、右键关闭面板、Shift+右键关闭面板并把导航线指向该星球。
- 删除 `Locate/ClosePanelOnLocate` 与 `Locate/LinkNavigation` 两个配置项（行为已由点击方式固定），`Locate` 段只剩 `EnableStarmapButton`。

## 0.1.4

- 删除 `AstroList` 的四个前后缀配置与相关代码（`PlanetPrefix`、`PlanetPostfix`、`StarPrefix`、`StarPostfix`）。
- 保留 `AstroList/SortByName` 与全部导航热键；下拉框固定使用内置样式：星系名黄色、星球名缩进两格。

## 0.1.3

- 配置界面顺序调整：用 `ConfigurationManagerAttributes.Order` 固定显示顺序（配置管理器按 Order 倒序、再按名称排序）。
  - `Locate`：EnableStarmapButton → ClosePanelOnLocate → LinkNavigation。
  - `StationCopy`：CopyKey → PasteKey → EnableSlots（+ Slot1–5）→ EnableSettings（+ 9 个逐项开关）→ EnablePriority。
- 新增 `StationCopy/Slot1`–`Slot5`（默认开启）：粘贴时可单独关闭某个槽位，避免覆盖该槽位。
- 新增 9 个第二块逐项开关（默认开启）：`SetChargePower`、`SetTripDrones`、`SetTripShips`、`SetWarpDistance`、`SetWarperNecessary`、`SetOrbitCollector`、`SetDeliveryDrones`、`SetDeliveryShips`、`SetPilerCount`。

## 0.1.2

- MOD 更名为 **ControlPanelPlus**（原 ControlPanelTweaks），插件 GUID 改为 `com.dsp.controlpanelplus`，AssemblyVersion 重置为 0.1.2。
- 代码命名空间、日志前缀（`[CPP]`）、部署目录与自测工程同步更名；功能与 1.2.0 一致。

## 1.2.0

第三轮：第二轮问题修改 + 新增功能。

### 修改

- 星图「定位」默认**保留总控面板**并打开星图；新增 `Locate/ClosePanelOnLocate`（点击后关闭面板）与 `Locate/LinkNavigation`（联动导航线）两个配置，默认均为关闭。
- 「全部展开 / 全部折叠」合并为一个切换按钮，标签跟随当前状态，样式与「筛选：并集/交集」一致。

### 新增

- `Station/AllowRemoteItemTransfer`（默认关闭）：在总控面板上从非本地星球取放物品；检测到 CheatEnabler 时本项不生效（优先 CheatEnabler）。
- 物流塔存储行鼠标快捷键：右键供/需切换、中键无/需切换；检测到 UITweaks 时本功能自动停用（优先 UITweaks）。
- README 增加对 UXAssist、CheatEnabler、UITweaks、StatsUITweaks 的鸣谢。

## 1.1.0

第二轮：第一轮问题修复 + 新增功能。

### 修复

- Ctrl 多选现在包含当前已选中的塔（第一次 Ctrl+点击即 2 座）；普通左键点击清空多选。
- 多选高亮改用游戏原生行选中高亮，与普通单选一致。
- 星图「定位」按钮独立：不再点亮/切换导航线。
- 位置下拉框默认区分星系与星球：星系名黄色、星球名缩进（四项前后缀全空时生效）。
- 选中焦点闪烁动画缩短到原版 20%；`←`/`→` 改为切换上/下一个星球；鼠标位于物流塔存储槽位上时方向键交给 UXAssist 的存储容量调整。

### 新增

- 打开总控面板时自动按鼠标指向的物品筛选（手持物品 / 图标提示 / 制造配方 / 背包格子 / 生产条目）。
- 总控面板里右键物品图标＝替换物品筛选；Shift+右键＝在现有筛选上追加（上限 5）。
- Shift+左键批量框选两座塔之间（可跨星球）的所有物流塔。
- 重新打开面板保留上次星球展开状态（优先于两个默认展开/收起配置）。
- 标题栏新增「筛选：并集 / 交集」快速切换按钮。

## 1.0.0

首个版本，对应《总控面板优化ControlPanelTweaks（详细版·AI目标指令书）》的 F1–F5。

- **F1 展开 / 折叠**
  - 多星球结果默认全部折叠、单星球结果默认展开（两个独立开关）。
  - 面板标题栏新增「全部展开 / 全部折叠」按钮。
  - 默认值只在打开面板或切换筛选时套用，建塔/拆塔引起的列表刷新不会重置手动状态。
- **F2 星图定位**
  - 星球条目导航按钮左侧新增「定位」按钮；点击进星图、`SetViewStar` + 相机 `SetViewTarget` 对准星球，点亮导航线并尽量选中该星球。
- **F3 物流塔设置复制粘贴**
  - `<` 复制 / `>` 粘贴，按键可配置。
  - 三块内容独立开关：槽位设置、塔设置、优先级设置。
  - `Ctrl` + 点击多选塔（高亮 + 一键粘贴）；`Shift` + `>` 批量粘贴到当前星球槽位相同的塔。
  - 目标槽位里有别种物品且库存 > 0 时跳过，绝不搬运/清空库存。
  - 大型采矿机 / 轨道采集器只复制「上限 + 供需」与塔设置。
  - 点对点：先同名命名目标塔，再补建源塔的点对点航线。
- **F4 物品筛选交集**
  - 配置开关（默认关闭）：开启后 A+B 只显示同时含 A、B 的条目。
- **F5 位置下拉框**
  - 按星系名排序（前两项固定）。
  - 星球名 / 星系名前后缀（支持富文本）。
  - `PageUp`/`PageDown` 切换列表项（Ctrl 只跳星系）、`←`/`→` 跳到当前星球首个条目、`↑`/`↓` 跨星球逐条选择。
