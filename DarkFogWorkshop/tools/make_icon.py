"""生成 DarkFogWorkshop 的 icon.png（256x256，不依赖任何第三方库）。

图案：深色底 + 青蓝圆环（"车间"）+ 中央 3x3 矩阵方阵，方阵中若干格高亮成黑雾紫。
"""

import struct
import zlib

SIZE = 256

BG = (18, 26, 34)
RING = (74, 190, 214)
RING_DARK = (44, 122, 142)
MATRIX = (236, 244, 248)
FOG = (176, 132, 232)

px = [[BG for _ in range(SIZE)] for _ in range(SIZE)]

cx = cy = (SIZE - 1) / 2.0
r_out = 116.0
r_in = 100.0
r_glow = 124.0


def put(x, y, color):
    if 0 <= x < SIZE and 0 <= y < SIZE:
        px[y][x] = color


# 圆环
for y in range(SIZE):
    for x in range(SIZE):
        d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
        if r_in <= d <= r_out:
            # 简单明暗：左上亮、右下暗
            t = ((x - cx) + (y - cy)) / (2.0 * r_out)
            color = tuple(
                int(RING[i] + (RING_DARK[i] - RING[i]) * max(0.0, min(1.0, t + 0.5)))
                for i in range(3)
            )
            put(x, y, color)
        elif r_out < d <= r_glow:
            alpha = 1.0 - (d - r_out) / (r_glow - r_out)
            put(x, y, tuple(int(BG[i] + (RING_DARK[i] - BG[i]) * alpha * 0.55) for i in range(3)))

# 中央 3x3 矩阵方阵
cell = 34
gap = 8
total = 3 * cell + 2 * gap
x0 = int(cx - total / 2.0) + 1
y0 = int(cy - total / 2.0) + 1
fog_cells = {(0, 1), (1, 0), (2, 2), (1, 2)}

for row in range(3):
    for col in range(3):
        x1 = x0 + col * (cell + gap)
        y1 = y0 + row * (cell + gap)
        color = FOG if (row, col) in fog_cells else MATRIX
        for y in range(y1, y1 + cell):
            for x in range(x1, x1 + cell):
                # 圆角
                dx = min(x - x1, x1 + cell - 1 - x)
                dy = min(y - y1, y1 + cell - 1 - y)
                if dx < 4 and dy < 4 and (4 - dx) ** 2 + (4 - dy) ** 2 > 16:
                    continue
                # 左上到右下的轻微渐变
                t = ((x - x1) + (y - y1)) / (2.0 * cell)
                put(x, y, tuple(int(color[i] * (1.0 - 0.18 * t)) for i in range(3)))

raw = bytearray()
for y in range(SIZE):
    raw.append(0)  # filter type 0
    for x in range(SIZE):
        r, g, b = px[y][x]
        raw += bytes((r, g, b))


def chunk(tag, data):
    return (
        struct.pack(">I", len(data))
        + tag
        + data
        + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    )


png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
png += chunk(b"IEND", b"")

with open("icon.png", "wb") as f:
    f.write(png)
print("icon.png written:", len(png), "bytes")
