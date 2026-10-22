"""Probe original a90fc drop thresholds with fixed rand() inputs."""
import json
import sys
from pathlib import Path

from validate_native_rules import NativeProbe, ROOT, SCRATCH


def capture_dispatch():
    p = NativeProbe()
    battle, player, headquarters = SCRATCH, SCRATCH + 0x7000, SCRATCH + 0x8000
    tiles = [SCRATCH + 0x1000, SCRATCH + 0x1100]
    units = [SCRATCH + 0x2000, SCRATCH + 0x2200]
    profiles = [SCRATCH + 0x3000, SCRATCH + 0x3100]
    factions = [SCRATCH + 0x4000, SCRATCH + 0x4400]
    mode, collected, trace = 1, 0, []
    p.hook(0x85824, lambda _, cell, *args: tiles[cell])
    p.hook(0x4ef64, lambda tile, *_: p.read(tile + 0x70, "Q")[0])
    p.hook(0x31c7c, lambda *_: headquarters)
    p.hook(0x80c18, lambda *_: player)

    def damage(tile, amount, *_):
        unit = p.read(tile + 0x70, "Q")[0]
        health = max(0, 100 - amount)
        p.write(unit + 0x20, "i", health)
        if not health:
            p.write(tile + 0x70, "Q", 0)
        return int(not health)

    def drop(_, amount, tile, *args):
        trace.append(["roll", amount, tiles.index(tile)])
        return 1

    def award(_, amount, *args):
        trace.append(["account", amount])
        return 0

    def variable(_, key, *args):
        name = bytes(p.cpu.mem_read(key, 32)).split(b"\0", 1)[0]
        return collected if name == b"CollectMedal" else mode

    def store(_, key, value, *args):
        nonlocal collected
        assert bytes(p.cpu.mem_read(key, 13)).split(b"\0", 1)[0] == b"CollectMedal"
        collected = value
        trace.append(["collect", value])
        return 0

    p.hook(0x4f6b8, damage)
    p.hook(0xa90fc, drop)
    p.hook(0x2c320, award)
    p.hook(0x2f0f0, variable)
    p.hook(0x331a0, store)
    for address in [0x4ed18, 0x53808, 0x4f444, 0x52b20, 0x53004, 0x86b50, 0x5bc88,
                    0x58fb4, 0x4f4f4, 0x4eab8, 0x538bc, 0x7c1b8, 0xc60d0, 0xc6328]:
        p.hook(address, lambda *_: 0)
    vectors = []
    for mode in [1, 2, 3]:
        for attacker_human, defender_human in [(True, False), (False, True), (True, True), (False, False)]:
            for taken, dealt in [(0, 35), (35, 0), (35, 35), (0, 100), (100, 0)]:
                p.cpu.mem_write(battle, bytes(0x100))
                p.write(battle + 0x20, "2i", 0, 1)
                p.write(battle + 0x30, "2i", taken, dealt)
                p.write(battle + 0x3c, "i", -1)
                p.write(battle + 0x44, "B", int(taken > 0))
                for i, human in enumerate([attacker_human, defender_human]):
                    for address, size in [(tiles[i], 0x100), (units[i], 0x200), (profiles[i], 0x100), (factions[i], 0x400)]:
                        p.cpu.mem_write(address, bytes(size))
                    p.write(tiles[i] + 0x68, "2Q", factions[i], units[i])
                    p.write(units[i] + 8, "2Q", profiles[i], factions[i])
                    p.write(units[i] + 0x20, "2i", 100, 100)
                    p.write(factions[i] + 0x54, "B", int(not human))
                collected, trace = 0, []
                p.call(0x825d4, battle)
                expected = []
                if mode != 3:
                    for human, amount in [(attacker_human, dealt), (defender_human, taken)]:
                        if human:
                            expected.extend([["roll", amount, 1], ["account", 1], ["collect", 1 + sum(t[0] == "collect" for t in expected)]])
                assert trace == expected, (mode, attacker_human, defender_human, taken, dealt, trace, expected)
                vectors.append({"mode": mode, "human": [attacker_human, defender_human], "taken": taken, "dealt": dealt, "trace": list(trace)})
    return vectors


def main():
    p = NativeProbe()
    tile, building = SCRATCH, SCRATCH + 0x1000
    roll = 0
    calls = 0

    def fixed_rand(*_):
        nonlocal calls
        calls += 1
        return roll

    p.hook_import("rand", fixed_rand)
    vectors = []
    for kind in [-1, 0, 1, 2, 3, 4]:
        for level in [0, 1, 2, 3]:
            p.write(tile + 0x40, "Q", building if kind >= 0 else 0)
            p.write(building + 4, "2i", kind, level)
            for damage in [0, 19, 20, 24, 25, 29, 30, 34, 35, 100]:
                successes = []
                for roll in range(100):
                    actual = bool(p.call(0xa90fc, 0, damage, tile))
                    bonus = level * (3 if kind == 1 else 2 if kind in [0, 3] else 0)
                    expected = damage >= 20 and roll + bonus > (95 if damage < 25 else 91 if damage < 30 else 87 if damage < 35 else 82)
                    assert actual == expected, (kind, level, damage, roll, actual)
                    if actual:
                        successes.append(roll)
                vectors.append({"kind": kind, "level": level, "damage": damage, "successes": successes})
    assert calls == len(vectors) * 100
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "build/medal-drops-20261022/native.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    dispatch = capture_dispatch()
    report = {"scope": "Original a90fc/ef658/building getters with fixed rand(); 825d4 with supplied damage/progression/drop success; not libc sequence or device parity", "checks": calls + len(dispatch), "errors": 0, "vectors": vectors, "dispatch": dispatch}
    output.write_text(json.dumps(report, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps({"checks": report["checks"], "errors": 0, "output": str(output)}))


if __name__ == "__main__":
    main()
