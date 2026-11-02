"""Execute original JNI clock, engine dispatch and recruit descent without graphics."""
import json
import struct
import sys
from pathlib import Path

from validate_native_rules import NativeProbe, ROOT, SCRATCH
from unicorn.arm64_const import UC_ARM64_REG_S0


def float_arg(probe):
    return struct.unpack('<f', struct.pack('<I', probe.cpu.reg_read(UC_ARM64_REG_S0)))[0]


def main():
    probe = NativeProbe()
    engine, scene, map_node, manager, cell, game, country, first, second, scene_vtable, map_vtable, units = (
        SCRATCH + index * 0x400 for index in range(12))
    wall_ms, speed, ai = 0, 1, False
    observed, landed = [], []

    def clock(address, *_):
        probe.write(address, '2q', 1000 + wall_ms // 1000, wall_ms % 1000 * 1000)
        return 0

    probe.hook_import('gettimeofday', clock)
    probe.hook(0x31844, lambda *_: engine)
    probe.hook(0x2f7f4, lambda *_: 0)  # Graphics submission is outside this clock probe.
    probe.hook(0x80c18, lambda *_: country)
    probe.hook(0x2f0f0, lambda *_: speed)
    probe.hook(0x5bc70, lambda *_: 0)
    probe.hook(0x7ec08, lambda *_: 0)
    probe.hook(0x4efb8, lambda *_: (landed.append(wall_ms) or 0))
    # Supply two one-entry scene containers; their callbacks and timing execute natively.
    probe.hook(0x156760, lambda entry, *_: engine + (0x58 if entry == first else 0xa0))
    probe.write(engine, 'i', 1)
    probe.write(engine + 0x68, 'Q', first)
    probe.write(engine + 0xb0, 'Q', second)
    probe.write(first + 0x28, 'Q', scene)
    probe.write(second + 0x28, 'Q', map_node)
    for node, vtable, update in [(scene, scene_vtable, 0x9db74), (map_node, map_vtable, 0x949ec)]:
        probe.write(node, 'Q', vtable)
        probe.write(vtable + 0x20, 'Q', update)
        probe.write(node + 0x10, 'Q', node + 0x10)
    probe.write(scene + 0x150, 'Q', game)
    probe.write(map_node + 0x20, 'Q', manager)
    probe.write(manager + 0x20, '2Q', units, units + 8)
    probe.write(units, 'Q', cell)
    vectors = []
    for ai in [False, True]:
        probe.write(country + 0x54, 'B', ai)
        for speed in range(1, 6):
            probe.write(cell + 0x80, 'Q', cell + 0x200)
            probe.write(cell + 0x98, 'f', -60)
            wall_ms = 0
            probe.write(0x221b20, 'q', 1000000)
            landed.clear()
            samples = []
            for wall_ms in range(0, 201, 10):
                probe.call(0x2ac94)
                offset = probe.read(cell + 0x98, 'f')[0]
                multiplier = (speed - 1) * .5 if ai and speed >= 4 else 1
                expected = min(0, -60 + wall_ms / 1000 * 1.15 * multiplier * 320)
                assert abs(offset - expected) < .0001, (ai, speed, wall_ms, offset, expected)
                samples.append({'wallMs': wall_ms, 'offset': offset})
            assert landed, (ai, speed, 'descent never finished')
            vectors.append({'ai': ai, 'setting': speed, 'landedByMs': landed[0], 'samples': samples})
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'build/recruit-timing-20261101/native.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    report = {'checks': len(vectors) * 22, 'errors': 0, 'vectors': vectors,
              'scope': 'Original JNI gettimeofday conversion -> engine -> scene speed gate -> map manager -> cell descent; synthetic containers, intercepted graphics/services; no Android device test',
              'clockPerWallMs': probe.read(0x2ad64, 'f')[0], 'descentPerSimulationSecond': probe.read(0x51914, 'f')[0]}
    output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: report[key] for key in ['checks', 'errors', 'clockPerWallMs', 'descentPerSimulationSecond']}))


if __name__ == '__main__':
    main()
