"""Capture native morale sprite selection and unit draw placement in isolation."""
import json
import struct
import sys
from pathlib import Path

from validate_native_rules import NativeProbe, ROOT, SCRATCH
from unicorn import arm64_const as registers


def main():
    p = NativeProbe()
    renderer, unit, profile, motion, faction = (SCRATCH + n * 0x1000 for n in range(5))
    names = {renderer + 0x800 + i * 8: name for i, name in enumerate([
        "morale_down3.png", "morale_down2.png", "morale_down1.png", None, "morale_up.png"])}
    for i, sprite in enumerate(names):
        p.write(renderer + 0x80 + i * 8, "Q", sprite)
    p.write(unit + 8, "Q", profile)
    p.write(unit + 0x10, "Q", faction)
    p.write(unit + 0x78, "Q", motion)
    p.write(unit + 0xe8, "Q", renderer)
    p.write(unit + 0xd8, "f", 1)
    draws = []

    def capture(sprite, *_):
        xy = [struct.unpack("<f", struct.pack("<I", p.cpu.reg_read(getattr(registers, f"UC_ARM64_REG_S{i}"))))[0] for i in (0, 1)]
        draws.append({"sprite": names[sprite], "x": xy[0], "y": xy[1]})
        return 0

    p.hook(0xc31c8, capture)
    p.hook(0x532e8, lambda *_: 0)
    p.hook(0x60958, lambda *_: 0)
    vectors = []
    for morale in range(-3, 2):
        draws.clear()
        p.call(0x60b50, renderer, morale)
        direct = list(draws)
        draws.clear()
        p.write(unit + 0x44, "i", morale)
        for i, value in enumerate([320, 540]):
            p.cpu.reg_write(getattr(registers, f"UC_ARM64_REG_S{i}"), struct.unpack("<I", struct.pack("<f", value))[0])
        p.call(0x5335c, unit, 0)
        expected = [] if morale == 0 else [{"sprite": names[renderer + 0x800 + (morale + 3) * 8], "x": 335, "y": 515}]
        assert draws == expected, (morale, draws)
        assert [d["sprite"] for d in direct] == [d["sprite"] for d in expected]
        vectors.append({"morale": morale, "draws": list(draws)})
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "build/morale-restoration-20261022/native.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    report = {"scope": "Original ARM64 60b50 and 5335c; graphics intercepted, no device test", "unitPosition": [320, 540], "vectors": vectors, "checks": 10, "errors": 0}
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"checks": report["checks"], "errors": 0, "output": str(output)}))


if __name__ == "__main__":
    main()
