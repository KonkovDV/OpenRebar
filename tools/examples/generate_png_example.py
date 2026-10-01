"""PNG twin of the 6000×4000 mm simple-slab DXF.

600×400 px of slab at 0.1 px/mm, a margin, and a legend of three classes
on the right. The slab is exact legend red (Ø20@150). The pixel ring
outside that rectangle is a 50% blend, so the edge is antialiased and the
classified area stays the rectangle.
"""

import pathlib
import struct
import zlib

MARGIN = 24
SLAB_W = 600
SLAB_H = 400
LEGEND_W = 160
WIDTH = MARGIN + SLAB_W + MARGIN + LEGEND_W
HEIGHT = MARGIN + SLAB_H + MARGIN
ORIGIN_X = MARGIN
ORIGIN_Y = HEIGHT - MARGIN

WHITE = (255, 255, 255)
PANEL = (245, 245, 245)
INK = (0, 0, 0)
RED = (255, 0, 0)
GREEN = (0, 255, 0)
YELLOW = (255, 255, 0)
BLEND = (255, 128, 128)

out = pathlib.Path(__file__).resolve().parents[2] / "examples" / "png" / "simple-slab" / "input.png"


def main():
    pixels = [WHITE] * (WIDTH * HEIGHT)

    def put(x, y, color):
        if 0 <= x < WIDTH and 0 <= y < HEIGHT:
            pixels[y * WIDTH + x] = color

    def fill(x, y, w, h, color):
        for py in range(y, y + h):
            for px in range(x, x + w):
                put(px, py, color)

    slab_x = ORIGIN_X
    slab_y = ORIGIN_Y - SLAB_H
    fill(slab_x, slab_y, SLAB_W, SLAB_H, RED)

    # One-pixel fringe outside the slab. These colors are not legend entries.
    for x in range(slab_x - 1, slab_x + SLAB_W + 1):
        put(x, slab_y - 1, BLEND)
        put(x, slab_y + SLAB_H, BLEND)
    for y in range(slab_y, slab_y + SLAB_H):
        put(slab_x - 1, y, BLEND)
        put(slab_x + SLAB_W, y, BLEND)

    panel_x = WIDTH - LEGEND_W
    fill(panel_x, 0, LEGEND_W, HEIGHT, PANEL)
    swatches = (GREEN, YELLOW, RED)
    for index, color in enumerate(swatches):
        sx = panel_x + 13
        sy = 17 + index * 44
        fill(sx - 1, sy - 1, 30, 30, INK)
        fill(sx, sy, 28, 28, color)

    raw = bytearray()
    for y in range(HEIGHT):
        raw.append(0)
        for x in range(WIDTH):
            raw.extend(pixels[y * WIDTH + x])

    def chunk(tag, data):
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(png)
    print(f"{out} {WIDTH}x{HEIGHT} origin {ORIGIN_X},{ORIGIN_Y} roi {slab_x},{slab_y},{SLAB_W},{SLAB_H}")


if __name__ == "__main__":
    main()
