"""Draws the images of the Doom screen: the backdrops, the frame corners and the buttons. Needs pillow and numpy.

    python make_assets.py

The backdrops are drawn once per aspect ratio and per screen height (1080, 1440 and 2160), so the game never has to
scale them far. They carry no element the code has to line up with: the frame lines, panels and text are drawn by
DoomScreen from the layout in DoomLayout.cs, so they match any real screen shape. The ratio names match
DoomLayout.AspectName.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))

RATIOS = {"4x3": 4 / 3, "16x10": 16 / 10, "16x9": 16 / 9, "21x9": 64 / 27}
HEIGHTS = (1080, 1440, 2160)

EMBER = np.array([0.86, 0.16, 0.06])
DEEP = np.array([0.030, 0.022, 0.026])
HULL = np.array([0.075, 0.045, 0.045])

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float32) / 16.0


def backdrop(width, height, name):
    y = np.linspace(0, 1, height, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, width, dtype=np.float32)[None, :]
    aspect = width / height

    # Base: dark hull colour, a little warmer towards the bottom.
    img = DEEP[None, None, :] + (HULL - DEEP)[None, None, :] * (y ** 1.6)[:, :, None]

    # Vignette, measured in screen heights so it is round on every ratio.
    dx = (x - 0.5) * aspect
    dy = (y - 0.5)
    r = np.sqrt(dx * dx + dy * dy)
    vignette = np.clip(1.0 - (r / (0.5 * aspect + 0.25)) ** 2.2, 0.0, 1.0)
    img = img * (0.35 + 0.65 * vignette)[:, :, None]

    # Ember glow along the bottom and a faint one at the top.
    bottom = np.clip((y - 0.72) / 0.28, 0.0, 1.0) ** 2.4
    top = np.clip((0.12 - y) / 0.12, 0.0, 1.0) ** 2.0
    glow = (0.20 * bottom + 0.05 * top) * (0.55 + 0.45 * np.cos((x - 0.5) * np.pi * 0.9))
    img = img + glow[:, :, None] * EMBER[None, None, :]

    # Scan lines, every 1/180 of the height.
    period = max(3, round(height / 180))
    rows = (np.arange(height) % period == 0)[:, None, None]
    img = np.where(rows, img * 0.82, img)

    # Dither to hide the gradient banding.
    ty = np.tile(BAYER, (height // 4 + 1, width // 4 + 1))[:height, :width]
    out = np.clip(img * 255.0 + (ty[:, :, None] - 0.5), 0, 255).astype(np.uint8)
    image = Image.fromarray(out, "RGB")

    # Thin tech lines near the edges.
    draw = ImageDraw.Draw(image)
    line = (92, 30, 22)
    unit = height / 1080.0
    inset = round(18 * unit)
    w = max(1, round(2 * unit))
    draw.rectangle((inset, inset, width - 1 - inset, height - 1 - inset), outline=line, width=w)
    tick = round(40 * unit)
    for k in range(1, 6):
        d = round(k * 14 * unit)
        draw.line((inset, inset + d + tick, inset + tick, inset + d), fill=line, width=w)
        draw.line((width - 1 - inset, inset + d + tick, width - 1 - inset - tick, inset + d), fill=line, width=w)
    image.save(os.path.join(HERE, name), optimize=True)


def corner(name, flip_x, flip_y):
    """A glowing L-shaped bracket, drawn top-left at 4x and flipped for the other corners."""
    size, ss = 256, 4
    big = size * ss
    layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    arm, thick, off = int(big * 0.92), int(big * 0.07), int(big * 0.04)
    d.rectangle((off, off, off + arm, off + thick), fill=(255, 98, 40, 255))
    d.rectangle((off, off, off + thick, off + arm), fill=(255, 98, 40, 255))
    d.rectangle((off + thick * 2, off + thick * 2, off + int(arm * 0.4), off + thick * 3), fill=(255, 190, 90, 255))
    d.rectangle((off + thick * 2, off + thick * 2, off + thick * 3, off + int(arm * 0.4)), fill=(255, 190, 90, 255))
    glow = layer.filter(ImageFilter.GaussianBlur(big * 0.025))
    out = Image.alpha_composite(glow, layer).resize((size, size), Image.LANCZOS)
    if flip_x:
        out = out.transpose(Image.FLIP_LEFT_RIGHT)
    if flip_y:
        out = out.transpose(Image.FLIP_TOP_BOTTOM)
    out.save(os.path.join(HERE, name), optimize=True)


def button(name, fill, edge, glow_alpha):
    """A plate with cut corners, for the close button."""
    w, h, ss = 256, 96, 4
    big = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    pad, cut = 10 * ss, 22 * ss
    pts = [(pad + cut, pad), (w * ss - pad, pad), (w * ss - pad, h * ss - pad - cut),
           (w * ss - pad - cut, h * ss - pad), (pad, h * ss - pad), (pad, pad + cut)]
    d.polygon(pts, fill=fill, outline=edge, width=3 * ss)
    if glow_alpha:
        glow = big.filter(ImageFilter.GaussianBlur(7 * ss))
        glow.putalpha(glow.getchannel("A").point(lambda a: int(a * glow_alpha)))
        big = Image.alpha_composite(glow, big)
    big.resize((w, h), Image.LANCZOS).save(os.path.join(HERE, name), optimize=True)


def main():
    for ratio, aspect in RATIOS.items():
        for height in HEIGHTS:
            width = round(height * aspect)
            name = f"DoomBg{ratio}_{height}.png"
            backdrop(width, height, name)
            print(name, width, height)

    corner("DoomCornerTL.png", False, False)
    corner("DoomCornerTR.png", True, False)
    corner("DoomCornerBL.png", False, True)
    corner("DoomCornerBR.png", True, True)
    button("DoomButton.png", (46, 14, 12, 235), (190, 60, 34, 255), 0.0)
    button("DoomButtonHover.png", (96, 24, 16, 245), (255, 150, 70, 255), 0.9)


if __name__ == "__main__":
    main()
