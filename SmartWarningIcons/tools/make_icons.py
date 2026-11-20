# -*- coding: utf-8 -*-
"""生成 SmartWarningIcons 的原创图标素材。

配色语义：
    红色圆角底板 + 感叹号 + 敞口空料盒  → 缺原料（insufficient input）
    黄色圆角底板 + 感叹号 + 三层料盒堆  → 产物堆砌（output piled up）
4 倍超采样后缩小，保证游戏内小尺寸下的边缘质量。

产物：
    assets/InsufficientInput.png       256×256  红色 图集格子
    assets/InsufficientInputSmall.png   80×80   红色 UI 图标
    assets/OutputPile.png              256×256  黄色 图集格子
    assets/OutputPileSmall.png          80×80   黄色 UI 图标
    icon.png                           256×256  Thunderstore 包封面（双色双板）
"""

from pathlib import Path

from PIL import Image, ImageDraw

S = 4  # 超采样倍数

RED = (224, 62, 62, 255)
RED_EDGE = (122, 22, 22, 255)
YELLOW = (245, 196, 60, 255)
YELLOW_EDGE = (138, 98, 10, 255)
WHITE = (255, 255, 255, 255)
NAVY = (30, 40, 55, 255)

# 图案相对底板（224 设计稿边长）的比例坐标。底板在 256 画布中占 16..240，
# 内容需与底板边框（宽约 6px）保持 ≥12px 净距，即内容底部不超过约 218/224。
GLYPH = {
    "bar_half_w": 15 / 224,
    "bar_top": 24 / 224,      # abs 40
    "bar_bottom": 88 / 224,   # abs 104
    "dot_cy": 112 / 224,      # abs 128
    "dot_r": 15 / 224,
    # 敞口料盒（缺原料）：盒身 + 两侧外翻盒盖
    "crate": {"l": 72 / 224, "r": 152 / 224, "t": 142 / 224, "b": 194 / 224,
              "flap": 20 / 224, "line_w": 12 / 224},
    # 料盒堆（产物堆砌）：上层一箱居中 + 下层两箱并排（扁宽货箱形）
    "stack": {"w": 52 / 224, "h": 36 / 224, "gap": 8 / 224, "row_t": 160 / 224,
              "top_t": 112 / 224, "line_w": 9 / 224},
    # 黄色版感叹号更紧凑（给料盒堆留空间，避免圆点压住盒顶）
    "exclaim_yellow": {"bar_half_w": 14 / 224, "bar_top": 18 / 224,
                       "bar_bottom": 72 / 224, "dot_cy": 90 / 224, "dot_r": 12 / 224},
}


def draw_plate(draw, plate, fill, edge):
    left, top, right, bottom = plate
    side = right - left
    line_w = max(2 * S, int(side * 6 / 224))
    draw.rounded_rectangle(
        plate, radius=int(side * 0.22), fill=fill, outline=edge, width=line_w)


def draw_exclaim(draw, plate, spec=None):
    left, top, right, _ = plate
    side = right - left
    cx = (left + right) / 2
    g = spec or GLYPH
    bar_w = g["bar_half_w"] * side
    draw.rounded_rectangle(
        (cx - bar_w, top + g["bar_top"] * side, cx + bar_w, top + g["bar_bottom"] * side),
        radius=bar_w, fill=WHITE)
    dot_r = g["dot_r"] * side
    dot_cy = top + g["dot_cy"] * side
    draw.ellipse((cx - dot_r, dot_cy - dot_r, cx + dot_r, dot_cy + dot_r), fill=WHITE)


def draw_open_crate(draw, plate):
    """敞口空料盒：缺原料意象。"""
    left, top, right, _ = plate
    side = right - left
    c = GLYPH["crate"]
    line_w = max(2 * S, int(c["line_w"] * side))
    box_l = left + c["l"] * side
    box_r = left + c["r"] * side
    box_t = top + c["t"] * side
    box_b = top + c["b"] * side
    flap = c["flap"] * side
    draw.rectangle((box_l, box_t, box_r, box_b), outline=WHITE, width=line_w)
    draw.line((box_l, box_t, box_l - flap, box_t - flap), fill=WHITE, width=line_w)
    draw.line((box_r, box_t, box_r + flap, box_t - flap), fill=WHITE, width=line_w)


def draw_stack(draw, plate):
    """三层货箱堆：产物堆砌意象。"""
    left, top, right, _ = plate
    side = right - left
    c = GLYPH["stack"]
    line_w = max(2 * S, int(c["line_w"] * side))
    box_w = c["w"] * side
    box_h = c["h"] * side
    gap = c["gap"] * side
    cx = (left + right) / 2
    row_t = top + c["row_t"] * side
    row_b = row_t + box_h

    # 下层两箱并排
    l1 = cx - gap / 2 - box_w
    l2 = cx + gap / 2
    draw.rectangle((l1, row_t, l1 + box_w, row_b), outline=WHITE, width=line_w)
    draw.rectangle((l2, row_t, l2 + box_w, row_b), outline=WHITE, width=line_w)
    # 上层一箱居中
    top_t = top + c["top_t"] * side
    draw.rectangle((cx - box_w / 2, top_t, cx + box_w / 2, top_t + box_h),
                   outline=WHITE, width=line_w)


def make_icon(size, plate_fill, plate_edge, glyph, background=None,
              plate_margin_ratio=16 / 256, exclaim=None):
    img = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    if background is not None:
        draw.rounded_rectangle(
            (0, 0, size * S - 1, size * S - 1), radius=36 * S, fill=background)

    m = size * S * plate_margin_ratio
    plate = (m, m, size * S - m, size * S - m)
    draw_plate(draw, plate, plate_fill, plate_edge)
    draw_exclaim(draw, plate, exclaim)
    glyph(draw, plate)

    return img.resize((size, size), Image.LANCZOS)


def make_package_icon(size):
    """包封面：藏青底 + 红、黄两块小底板并排。"""
    img = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle(
        (0, 0, size * S - 1, size * S - 1), radius=36 * S, fill=NAVY)

    side = size * S * (224 - 2 * 48) / 256  # 单块底板边长（留出边距与间隙）
    gap = size * S * 8 / 256
    total = side * 2 + gap
    origin_x = (size * S - total) / 2
    origin_y = (size * S - side) / 2

    for i, (fill, edge, glyph) in enumerate([
        (RED, RED_EDGE, draw_open_crate),
        (YELLOW, YELLOW_EDGE, draw_stack),
    ]):
        left = origin_x + i * (side + gap)
        plate = (left, origin_y, left + side, origin_y + side)
        draw_plate(draw, plate, fill, edge)
        draw_exclaim(draw, plate)
        glyph(draw, plate)

    return img.resize((size, size), Image.LANCZOS)


def main():
    root = Path(__file__).resolve().parent.parent
    assets = root / "assets"
    assets.mkdir(exist_ok=True)

    make_icon(256, RED, RED_EDGE, draw_open_crate).save(assets / "InsufficientInput.png")
    make_icon(80, RED, RED_EDGE, draw_open_crate).save(assets / "InsufficientInputSmall.png")
    make_icon(256, YELLOW, YELLOW_EDGE, draw_stack,
              exclaim=GLYPH["exclaim_yellow"]).save(assets / "OutputPile.png")
    make_icon(80, YELLOW, YELLOW_EDGE, draw_stack,
              exclaim=GLYPH["exclaim_yellow"]).save(assets / "OutputPileSmall.png")
    make_package_icon(256).save(root / "icon.png")

    print("图标已生成：assets/InsufficientInput*.png, assets/OutputPile*.png, icon.png")


if __name__ == "__main__":
    main()
