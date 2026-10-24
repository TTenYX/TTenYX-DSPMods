# -*- coding: utf-8 -*-
"""生成 SmartDustbin 图标：深色圆角底 + 青色垃圾桶 + 404 信号菱形角标。256x256 PNG。"""
from PIL import Image, ImageDraw, ImageFont

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 深色圆角底（DSP 界面深蓝灰）
d.rounded_rectangle([8, 8, 248, 248], radius=44, fill=(24, 34, 46, 255), outline=(60, 84, 106, 255), width=4)

# 垃圾桶主体（梯形）
body = [(84, 92), (172, 92), (162, 204), (94, 204)]
d.polygon(body, fill=(38, 122, 132, 255), outline=(120, 214, 224, 255))

# 桶盖
d.rounded_rectangle([72, 66, 184, 88], radius=8, fill=(52, 158, 170, 255), outline=(140, 226, 236, 255), width=3)
# 盖上提手
d.arc([106, 44, 150, 78], start=180, end=360, fill=(140, 226, 236, 255), width=8)

# 桶身竖条（镂空感）
for x in (104, 124, 144):
    d.rounded_rectangle([x, 104, x + 10, 190], radius=5, fill=(24, 34, 46, 255))

# 右下角 404 信号角标：青色圆点 + 白点（呼应 SIGNAL-404 图标风格）
cx, cy, r = 196, 196, 34
d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(16, 94, 106, 255), outline=(140, 226, 236, 255), width=4)
d.text((cx, cy), "404", fill=(235, 250, 252, 255), anchor="mm",
       font=ImageFont.load_default(28))

img.save("icon.png")
print("icon.png written:", img.size)
