"""Probe SceneGame's local dt multiplier/pause gate, without assuming upstream time."""
import json
import struct
import sys
from pathlib import Path

from validate_native_rules import NativeProbe, ROOT, SCRATCH
from unicorn.arm64_const import UC_ARM64_REG_S0


def main():
    p = NativeProbe()
    scene, system, game, player = (SCRATCH + i * 0x1000 for i in range(4))
    p.write(scene + 0x150, "Q", game)
    speed, observed = 1, []
    p.hook(0x80c18, lambda *args: player)
    p.hook(0x2f0f0, lambda *args: speed)
    p.hook(0x5bc70, lambda *args: 0)

    def tick(*args):
        observed.append(struct.unpack("<f", struct.pack("<I", p.cpu.reg_read(UC_ARM64_REG_S0)))[0])
        return 0

    p.hook(0x7ec08, tick)
    vectors = []
    for ai in [False, True]:
        p.write(player + 0x54, "B", ai)
        for speed in range(1, 6):
            for paused in [False, True]:
                p.write(scene + 0x13d, "B", paused)
                observed.clear()
                p.cpu.reg_write(UC_ARM64_REG_S0, struct.unpack("<I", struct.pack("<f", 0.1))[0])
                p.call(0x9db74, scene, system)
                expected = [] if paused else [0.1 * ((speed - 1) * 0.5 if ai and speed >= 4 else 1)]
                assert len(observed) == len(expected) and all(abs(a - b) < 1e-6 for a, b in zip(observed, expected))
                vectors.append({"ai": ai, "speed": speed, "paused": paused, "inputDt": 0.1, "outputDt": list(observed)})
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "build/attached-effects-20261022/scene-clock.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"scope": "Original 9db74 local gate/multiplier; services intercepted; upstream clock and action updates not proven", "checks": len(vectors), "errors": 0, "vectors": vectors}, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"checks": len(vectors), "errors": 0, "output": str(output)}))


if __name__ == "__main__":
    main()
