"""Generate original geometric demo artwork using only the Python standard library."""
import pathlib
import struct
import zlib


def png(size):
    def pixel(x, y):
        x, y = x / size, y / size
        if (x - .5) ** 2 + (y - .37) ** 2 < .13 ** 2:
            return bytes((224, 193, 151))
        if .19 < x < .81 and .19 < y < .28:
            return bytes((26, 38, 53))
        if .2 < x < .8 and .52 < y < .94:
            return bytes((38, 87, 139) if abs(x - .5) > .025 else (230, 202, 120))
        return bytes((222, 217, 204))
    raw = b''.join(b'\0' + b''.join(pixel(x, y) for x in range(size)) for y in range(size))
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')


if __name__ == '__main__':
    output = pathlib.Path(__file__).parent / 'rewrite' / 'skins'
    output.mkdir(exist_ok=True)
    for name, size in [('expedition', 384), ('expedition-marker', 128)]:
        (output / (name + '.png')).write_bytes(png(size))
