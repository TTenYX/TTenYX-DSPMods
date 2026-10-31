# IconFix 图标选取附带名称

给《戴森球计划》的「图标选取」面板新增一个勾选框 **图标附带名称**（默认关闭）。

开启后，在图标选取面板点选任意图标，会同时插入「图标 + 名称」文本码，例如 `\meik-;`（显示为 图标+煤矿）；
关闭后保持原版行为，仅插入图标本身 `\meik;`。

**Shift+左键快速插入**（默认开启，可在配置中关闭）：勾选框关闭时，按住 Shift 左键点击图标，可直接插入带名称的 `\meik-;`；
勾选框开启时按住 Shift 点击则插入纯图标 `\meik;`，即 Shift 临时取反勾选框状态。

- 作用于所有使用图标选取的输入框：蓝图介绍、蓝图名称、星球/恒星备注、行星标记备注、物流站/控制面板名称等。
- 勾选状态跨游戏启动记忆，修改后立即生效。
- 无前置 MOD 要求（BepInEx 5 环境即可），不写入存档。

游戏版本 0.10.35 · BepInEx 5 · 作者 TTenYX

## 配置

全部配置在 `BepInEx\config\TTenYX.DSP.IconFix.cfg`，游戏内点击勾选框会自动保存：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| UI / IncludeNameWhenPick | false | 勾选框默认状态：开启时选取图标附带名称文本，关闭则仅插入图标 |
| UI / ShiftClickInsertName | true | 开启后，Shift+左键点击图标临时取反勾选框状态（未勾选时 Shift+点击插入带名称文本；已勾选时插入纯图标） |

---

# IconFix - Icon Picker With Name

Adds a checkbox **Icon With Name** (off by default) to the icon picker panel.

When enabled, clicking any icon in the picker inserts both the icon and its name text code,
for example `\meik-;` (rendered as icon + "Coal Mine"); when disabled, the vanilla behavior
is kept and only the icon `\meik;` is inserted.

**Shift+Click quick insert** (on by default, can be disabled in the config): with the checkbox off,
holding Shift while left-clicking an icon directly inserts the named tag `\meik-;`; with the checkbox
on, Shift+Click inserts the icon-only tag `\meik;` — Shift temporarily inverts the checkbox state.

- Works with every input that opens the icon picker: blueprint description, blueprint name,
  star/planet memos, planet marker comments, logistics station / control panel names, etc.
- The checkbox state persists across game sessions and applies immediately.
- No mod dependencies (BepInEx 5 only), does not write to save files.

Game version 0.10.35 · BepInEx 5 · Author TTenYX

## Configuration

All options live in `BepInEx\config\TTenYX.DSP.IconFix.cfg`. Toggling the checkbox in game saves automatically:

| Key | Default | Description |
|---|---|---|
| UI / IncludeNameWhenPick | false | Default state of the picker checkbox: when on, picking an icon also inserts its name text; when off, only the icon is inserted |
| UI / ShiftClickInsertName | true | When on, Shift+LeftClick on an icon temporarily inverts the checkbox state (inserts the named tag with the checkbox off, the icon-only tag with the checkbox on) |
