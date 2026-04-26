import pathlib
import struct
import zlib

out = pathlib.Path("examples/png/simple-slab/input.png")
out.parent.mkdir(parents=True, exist_ok=True)

width, height = 8, 8
raw = b"".join(b"\x00" + b"\xff\x00\x00" * width for _ in range(height))


def chunk(tag: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(tag + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)


png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(raw, 9))
png += chunk(b"IEND", b"")
out.write_bytes(png)
print(out)
