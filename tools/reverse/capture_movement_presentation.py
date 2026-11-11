"""Probe original fractional movement offsets; graphics/path planning are intercepted."""
import json
import math
import struct
import sys
from pathlib import Path

from validate_native_rules import NativeProbe, SCRATCH
from unicorn import arm64_const as regs


def main():
    probe = NativeProbe()
    cell, unit = SCRATCH, SCRATCH + 0x1000
    positions = []
    probe.hook(0x524f8, lambda *_: 0)  # Unit animation update, separate from offsets.

    def position(*_):
        positions.append(tuple(struct.unpack('<f', struct.pack('<I', probe.cpu.reg_read(r)))[0]
                               for r in (regs.UC_ARM64_REG_S0, regs.UC_ARM64_REG_S1)))
        return 0

    probe.hook(0x52788, position)
    checks = 0
    for hz in (30, 60, 90, 120, 144):
        for dx, dy in ((64, 0), (32, 54), (-32, 54), (-64, 0), (-32, -54), (32, -54)):
            probe.cpu.mem_write(cell, bytes(0x200))
            probe.write(cell + 0xc, '2i', 320, 540)
            probe.write(cell + 0x88, 'Q', unit)
            probe.write(cell + 0x9c, '6f', -dx, -dy, dx * 4, dy * 4, dx, dy)
            positions.clear()
            for frame in range(1, math.floor(hz * .2) + 1):
                dt = struct.unpack('<I', struct.pack('<f', 1 / hz))[0]
                probe.cpu.reg_write(regs.UC_ARM64_REG_S0, dt)
                probe.call(0x5166c, cell)
                actual = positions[-1]
                expected = (320 - dx + dx * 4 * frame / hz, 540 - dy + dy * 4 * frame / hz)
                assert all(abs(a - b) < .001 for a, b in zip(actual, expected)), (hz, frame, actual, expected)
                checks += 1
    result = {'checks': checks, 'errors': 0, 'functions': ['5166c', '52788'],
              'conclusion': 'Original movement offsets retain fractional positions and advance linearly by dt in all six directions.',
              'scope': 'Original ARM64 offset update with injected velocity; no full-game speed, original particle integrator or Android device claim.'}
    if len(sys.argv) > 1:
        Path(sys.argv[1]).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
