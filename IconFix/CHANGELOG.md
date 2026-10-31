# Changelog

#### v1.0.0 (DSP-0.10.35)
- 首个发布版：图标选取面板新增「图标附带名称」勾选框（默认关闭），开启时点选图标会同时插入名称文本（如 `\meik-;`）。
- Shift+左键点击图标可临时取反勾选状态（未勾选时插入带名称文本，已勾选时插入纯图标）。
- Shift+左键点击文本框中已插入的纯图标，原地补上名称（`\meik;` → `\meik-;`）；Ctrl+点击仍为查看物品详情。
- 同时修复原版文本框内 Ctrl+点击图标查看详情在多数输入框不生效的问题。
- 上述 Shift 功能均可在配置文件中开关，勾选状态自动保存。
- First release: adds an "Icon With Name" checkbox (off by default) to the icon picker; when on, picking an icon also inserts its name text (e.g. `\meik-;`).
- Shift+LeftClick on an icon temporarily inverts the checkbox state (inserts the named text when off, the icon-only tag when on).
- Shift+LeftClick on a plain icon already inserted in a text field upgrades it in place (`\meik;` → `\meik-;`); Ctrl+Click still opens the item details.
- Also fixes the vanilla Ctrl+Click "open details" on icons inside text fields, which did not work in most inputs.
- All Shift features can be toggled in the config file; the checkbox state is saved automatically.
