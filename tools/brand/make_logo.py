"""Cut the OpenRebar lockup from the brand sheet into two transparent PNGs.

  python tools/brand/make_logo.py

Every pixel is read as a blend of the sheet background and one brand color,
navy or signal orange. The output keeps that blend as alpha and paints the
pure brand color, so the mark sits cleanly on a light or a dark page.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SHEET = ROOT / "docs" / "brand" / "openrebar-brand-sheet.jpg"
CROP = (108, 56, 924, 270)

NAVY = (0x14, 0x22, 0x35)
ORANGE = (0xFF, 0x67, 0x38)
STONE = (0xF5, 0xF4, 0xF0)


def lum(rgb):
    return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]


def make(dark: bool, out: Path) -> None:
    sheet = Image.open(SHEET).convert("RGB")
    crop = sheet.crop(CROP)
    w, h = crop.size
    corners = [crop.getpixel((x, y)) for x in (2, w - 3) for y in (2, h - 3)]
    bg = tuple(sorted(c[i] for c in corners)[len(corners) // 2] for i in range(3))
    bg_lum = lum(bg)

    result = Image.new("RGBA", (w, h))
    src = crop.load()
    dst = result.load()
    for y in range(h):
        for x in range(w):
            p = src[x, y]
            if p[0] - p[2] > 25:
                alpha = (bg[2] - p[2]) / (bg[2] - ORANGE[2])
                color = ORANGE
            else:
                alpha = (bg_lum - lum(p)) / (bg_lum - lum(NAVY))
                color = STONE if dark else NAVY
            alpha = max(0.0, min(1.0, alpha))
            if alpha < 0.06:
                alpha = 0.0
            dst[x, y] = (*color, round(alpha * 255))
    result.save(out, optimize=True)
    print(out.relative_to(ROOT), result.size)


if __name__ == "__main__":
    make(False, ROOT / "docs" / "brand" / "openrebar-logo.png")
    make(True, ROOT / "docs" / "brand" / "openrebar-logo-dark.png")
