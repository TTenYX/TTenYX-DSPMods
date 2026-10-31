# Changelog

#### v1.0.0 (DSP-0.10.35)
- 首个发布版：图标选取新增「图标附带名称」勾选框（默认关闭），开启时点选图标会同时插入名称文本。
- Shift+左键点击图标可临时取反勾选框状态（默认开启，可在配置文件中关闭）：未勾选时按住 Shift 点击直接插入带名称的图标文本；已勾选时则插入纯图标。
- Shift+左键点击文本框中已插入的纯图标（默认开启，可在配置文件中关闭），会原地替换为带名称图标（`\meik;` → `\meik-;`）；Ctrl+点击仍为查看物品详情。
- First release: adds an "Icon With Name" checkbox (off by default) to the icon picker; when on, picking an icon also inserts its name text.
- Shift+LeftClick temporarily inverts the checkbox state (on by default, configurable): with the checkbox off, Shift+Click directly inserts the named icon text; with the checkbox on, it inserts the icon-only tag.
- Shift+LeftClick on a plain icon already inserted in a text field (on by default, configurable) upgrades it in place to the named version (`\meik;` → `\meik-;`); Ctrl+Click still opens the item details.
