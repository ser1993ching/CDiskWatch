# -*- coding: utf-8 -*-
"""
CDiskWatch 应用图标生成器（离线脚本，不参与程序构建）。

设计：深色圆角方块 + 圆环「占用率」表盘；主图标为中性蓝，
另生成 4 个状态图标（绿/黄/橙/红）供托盘按占用率切换。

输出：
  CDiskWatch/app.ico        多尺寸图标（16..256）
  CDiskWatch/icon-ok.ico    占用率 < 70%（绿）
  CDiskWatch/icon-warn.ico  70%~85%（黄）
  CDiskWatch/icon-high.ico  85%~93%（橙）
  CDiskWatch/icon-crit.ico  >= 93%（红）
  tools/icon-preview.png    预览图

用法：python tools/make_icon.py
依赖：Pillow（仅开发机需要）
"""
import math
import os
import struct
import zlib
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "CDiskWatch")
TOOLS = os.path.join(ROOT, "tools")

BG_TOP = (0x25, 0x2A, 0x33)
BG_BOTTOM = (0x16, 0x19, 0x1F)
ACCENT = (0x5B, 0x9C, 0xF6)
STATES = {
    "ok":   (0x4C, 0xC0, 0x8A),
    "warn": (0xF0, 0xA6, 0x4B),
    "high": (0xE8, 0x8B, 0x3A),
    "crit": (0xE0, 0x5C, 0x5C),
}
USED_RATIO = {
    "app":  0.42,
    "ok":   0.55,
    "warn": 0.75,
    "high": 0.88,
    "crit": 0.97,
}


def vgradient(size, top, bottom):
    """竖向渐变底"""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / max(1, size - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=c + (255,))
    return img


def rounded_mask(size, radius_ratio=0.22):
    m = Image.new("L", (size * 4, size * 4), 0)
    d = ImageDraw.Draw(m)
    r = int(size * 4 * radius_ratio)
    d.rounded_rectangle([0, 0, size * 4 - 1, size * 4 - 1], radius=r, fill=255)
    return m.resize((size, size), Image.LANCZOS)


def make_icon(color, ratio, size):
    """圆环表盘：底环 + 已用弧"""
    ss = 4
    S = size * ss
    base = vgradient(S, BG_TOP, BG_BOTTOM)
    mask = rounded_mask(S).point(lambda v: v)
    base.putalpha(mask)

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    cx = cy = S / 2
    r_out = S * 0.34
    r_in = S * 0.22

    # 底环
    d.ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], fill=(0x2E, 0x34, 0x40, 255))

    # 已用弧（扇形环）
    start = -90.0
    end = -90.0 + 360.0 * ratio
    steps = max(3, int(abs(end - start) / 3))
    for i in range(steps):
        a0 = math.radians(start + (end - start) * i / steps)
        a1 = math.radians(start + (end - start) * (i + 1) / steps)
        p0 = (cx + r_in * math.cos(a0), cy + r_in * math.sin(a0))
        p1 = (cx + r_out * math.cos(a1), cy + r_out * math.sin(a1))
        d.polygon([(cx, cy), p0, p1], fill=color + (255,))

    # 中心点
    d.ellipse([cx - r_in, cy - r_in, cx + r_in, cy + r_in], fill=(0x1B, 0x1F, 0x26, 255))

    out = Image.alpha_composite(base, layer)
    return out.resize((size, size), Image.LANCZOS)


def ico_bmp_frame(img):
    """32 位 BMP 帧（带 AND 掩码）"""
    size = img.size[0]
    b, g, r, a = img.split()
    px = img.load()
    data = bytearray()
    # BMP 自底向上
    for y in range(size - 1, -1, -1):
        row = bytearray()
        for x in range(size):
            rr, gg, bb, aa = px[x, y]
            row += bytes((bb, gg, rr, aa))
        data += row
    # AND 掩码：全 0（不透明），BGRA 模式下 alpha 已足够
    data += b"\x00" * ((size + 31) // 32 * 4 * size)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(data), 0, 0, 0, 0)
    return header + bytes(data)


def png_bytes(img):
    import io
    b = io.BytesIO()
    img.save(b, format="PNG")
    return b.getvalue()


def build_ico(images):
    """images: list of (size, PIL.Image)"""
    frames = []
    for size, img in images:
        if size <= 48:
            frames.append((size, 40, ico_bmp_frame(img)))
        else:
            frames.append((size, 0, png_bytes(img)))
    out = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    offset = 6 + 16 * len(frames)
    entries = bytearray()
    datas = bytearray()
    for size, _, data in frames:
        w = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        datas += data
    return bytes(out + entries + datas)


def save_set(ico_path, color, ratio, sizes):
    os.makedirs(os.path.dirname(ico_path), exist_ok=True)
    imgs = []
    for s in sizes:
        imgs.append((s, make_icon(color, ratio, s)))
    with open(ico_path, "wb") as f:
        f.write(build_ico(imgs))
    return imgs


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    master = save_set(os.path.join(OUT_DIR, "app.ico"), ACCENT, USED_RATIO["app"], sizes)
    for name, color in STATES.items():
        save_set(os.path.join(OUT_DIR, f"icon-{name}.ico"), color, USED_RATIO[name], sizes)

    os.makedirs(TOOLS, exist_ok=True)
    prev = Image.new("RGBA", (4 * 256, 256), (0, 0, 0, 0))
    for i, (name, color) in enumerate([("app", ACCENT)] + list(STATES.items())):
        img = make_icon(color, USED_RATIO[name], 256)
        prev.paste(img, (i * 256, 0), img)
    prev.save(os.path.join(TOOLS, "icon-preview.png"))
    print("icons written to", OUT_DIR)


if __name__ == "__main__":
    main()
