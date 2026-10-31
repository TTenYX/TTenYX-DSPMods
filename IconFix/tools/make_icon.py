# -*- coding: utf-8 -*-
"""生成 IconFix 图标 v2：蓝图卡片（网格纹理 + 标签条）承载「图标 + 名称文本」，大标题，勾选角标。256x256 PNG。"""
from PIL import Image, ImageDraw, ImageFont

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 深色圆角底（DSP 界面深蓝灰，与 SmartDustbin 同风格）
d.rounded_rectangle([8, 8, 248, 248], radius=44, fill=(24, 34, 46, 255), outline=(60, 84, 106, 255), width=4)

# ── 蓝图卡片（DSP 蓝图缩略图：蓝底 + 网格 + 顶部标签条）──
cx0, cy0, cx1, cy1 = 30, 26, 226, 168
d.rounded_rectangle([cx0, cy0, cx1, cy1], radius=12, fill=(21, 62, 92, 255), outline=(150, 200, 230, 255), width=4)
# 网格纹理（低对比蓝线）
grid = (48, 104, 140, 255)
for gx in range(cx0 + 18, cx1 - 6, 24):
    d.line([(gx, cy0 + 4), (gx, cy1 - 4)], fill=grid, width=1)
for gy in range(cy0 + 20, cy1 - 6, 24):
    d.line([(cx0 + 4, gy), (cx1 - 4, gy)], fill=grid, width=1)
# 顶部标签条（蓝图缩略图标志性的白色小标签）
d.rounded_rectangle([86, cy0 - 8, 170, cy0 + 10], radius=5, fill=(225, 240, 248, 255), outline=(60, 84, 106, 255), width=2)

# 卡片内主体：青色图标方块 + 加号 + 名称文本线
bx0, by0, bx1, by1 = 52, 74, 98, 124
d.rounded_rectangle([bx0, by0, bx1, by1], radius=9, fill=(38, 122, 132, 255), outline=(140, 226, 236, 255), width=3)
d.line([(bx0 + 13, by1 - 9), (bx0 + 33, by0 + 9)], fill=(140, 226, 236, 255), width=5)
d.line([(112, 99), (132, 99)], fill=(140, 226, 236, 255), width=6)
d.line([(122, 89), (122, 109)], fill=(140, 226, 236, 255), width=6)
d.rounded_rectangle([142, 86, 206, 99], radius=6, fill=(220, 238, 246, 255))
d.rounded_rectangle([142, 108, 190, 121], radius=6, fill=(110, 150, 176, 255))

# ── 标题（大）──
try:
    font_title = ImageFont.load_default(40)
except TypeError:
    font_title = ImageFont.load_default()
d.text((118, 204), "IconFix", fill=(225, 242, 250, 255), anchor="mm", font=font_title)

# 右下角标：勾选框
bx, by, bs = 190, 186, 42
d.rounded_rectangle([bx, by, bx + bs, by + bs], radius=9, fill=(16, 94, 106, 255), outline=(140, 226, 236, 255), width=4)
d.line([(bx + 9, by + 22), (bx + 18, by + 32), (bx + 34, by + 11)], fill=(235, 250, 252, 255), width=6, joint="curve")

img.save("icon.png")
print("icon.png written:", img.size)
