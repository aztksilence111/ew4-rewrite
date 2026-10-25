"""Probe original 50e00 capture diplomacy with ownership/UI services stubbed."""

import json

from validate_native_rules import NativeProbe, ROOT, SCRATCH


def main():
    p = NativeProbe()
    tile, unit, captor, victim, building, system = [SCRATCH + n * 0x1000 for n in range(1, 7)]
    rolls, talks = [], []
    mode = 1

    def capture(*args):
        p.write(tile + 0x68, "Q", captor)
        return 0

    def roll(*args):
        if not rolls:
            raise AssertionError("Unexpected native RNG draw")
        return rolls.pop(0)

    def talk(country, kind, *args):
        talks.append(kind)
        return 0

    for address in (0x52774, 0x5bc4c, 0x86ddc, 0x5906c, 0x86958, 0x86b50, 0x5c618,
                    0x31c7c, 0xc6328):
        p.hook(address, lambda *args: 0)
    p.hook(0x31844, lambda *args: system)
    p.hook(0xc60d0, lambda *args: system)
    p.hook(0x2f0f0, lambda *args: mode)
    p.hook(0x4f5a4, capture)
    p.hook(0xef754, roll)
    p.hook(0x5c12c, talk)
    vectors = []
    for source_camp, old_camp in ((1, 1), (1, 4), (2, 4), (3, 4), (1, 2)):
        for warned in (False, True):
            for draw in (0, 29, 30, 49, 50, 99):
                for choice in (0, 99):
                    for kind in (0, 4):
                        for mode in (1, 2, 3):
                            p.cpu.mem_write(tile, bytes(0x100))
                            p.cpu.mem_write(unit, bytes(0x200))
                            p.cpu.mem_write(captor, bytes(0x400))
                            p.cpu.mem_write(victim, bytes(0x400))
                            p.write(tile + 0x68, "Q", victim)
                            p.write(tile + 0xc8, "i", -1)
                            p.write(tile + 0x40, "Q", building)
                            p.write(building + 4, "i", kind)
                            p.write(unit + 0x10, "Q", captor)
                            p.write(captor, "i", source_camp)
                            p.write(victim, "i", old_camp)
                            p.write(victim + 0x54, "B", 1)
                            p.write(victim + 0x79, "B", int(warned))
                            rolls[:] = [draw, choice]
                            talks.clear()
                            p.call(0x50e00, tile, unit, 1)
                            expected_camp, expected_warned, expected_talk = old_camp, warned, []
                            eligible = mode != 3 and kind != 4 and old_camp in (source_camp, 4)
                            if eligible:
                                escalate = warned and draw < (30 if old_camp == source_camp else 50)
                                expected_warned = not (old_camp == source_camp and escalate)
                                expected_talk = [1 if escalate else 0] if old_camp == source_camp else [3 if escalate else 2]
                                if escalate:
                                    expected_camp = 4 if old_camp == source_camp else (source_camp - 1 + (1 if choice < 50 else 2)) % 3 + 1
                            actual = [p.read(victim, "i")[0], bool(p.read(victim + 0x79, "B")[0]), list(talks)]
                            expected = [expected_camp, expected_warned, expected_talk]
                            assert actual == expected, (source_camp, old_camp, warned, draw, choice, kind, mode, actual, expected)
                            vectors.append(dict(sourceCamp=source_camp, oldCamp=old_camp, warned=warned,
                                draw=draw, choice=choice, building=kind, mode=mode, result=actual,
                                draws=2-len(rolls)))
    output = ROOT / "reverse/reports"
    output.mkdir(parents=True, exist_ok=True)
    (output / "provocation_capture_vectors.json").write_text(json.dumps(vectors, separators=(",", ":")) + "\n", encoding="utf-8")
    capture_checks = len(vectors)
    p = NativeProbe()
    battle = SCRATCH
    tiles = [SCRATCH + 0x1000 + i * 0x100 for i in range(2)]
    units = [SCRATCH + 0x2000 + i * 0x200 for i in range(2)]
    definitions = [SCRATCH + 0x3000 + i * 0x100 for i in range(2)]
    factions = [SCRATCH + 0x4000 + i * 0x400 for i in range(2)]
    for address in (0x31c7c, 0x4ed18, 0x4f6b8, 0x53808, 0x4f444, 0x86b50, 0x5bc88,
                    0x58fb4, 0x4f4f4, 0x5c618, 0x4eab8, 0x80c18, 0x53004, 0x538bc, 0x53910,
                    0x52b94):
        p.hook(address, lambda *args: 0)
    p.hook(0x85824, lambda _, cell, *args: tiles[cell] if cell < 2 else 0)
    p.hook(0x4ef64, lambda site, *args: p.read(site + 0x70, "Q")[0])
    p.hook(0x2f0f0, lambda *args: mode)
    p.hook(0xef754, roll)
    p.hook(0x5c12c, talk)
    vectors = []
    for source_camp in (1, 2, 3):
        for warned in (False, True):
            for draw in (0, 29, 30, 49, 50, 99):
                for choice in (0, 99):
                    for mode in (1, 3):
                        for i, (site, army, definition, country) in enumerate(zip(tiles, units, definitions, factions)):
                            p.cpu.mem_write(site, bytes(0x100))
                            p.cpu.mem_write(army, bytes(0x200))
                            p.cpu.mem_write(definition, bytes(0x100))
                            p.cpu.mem_write(country, bytes(0x400))
                            p.write(site + 0x68, "Q", country)
                            p.write(site + 0x70, "Q", army)
                            p.write(army + 8, "Q", definition)
                            p.write(army + 0x20, "3i", 100, 100, 6)
                        p.write(factions[0], "i", source_camp)
                        p.write(factions[1], "i", 4)
                        p.write(factions[1] + 0x54, "B", 1)
                        p.write(factions[1] + 0x79, "B", int(warned))
                        p.cpu.mem_write(battle, bytes(0x100))
                        p.write(battle + 0x20, "2i", 0, 1)
                        p.write(battle + 0x3c, "i", -1)
                        rolls[:] = [draw, choice]
                        talks.clear()
                        p.call(0x825d4, battle)
                        escalate = mode != 3 and warned and draw < 50
                        expected_camp = (source_camp - 1 + (1 if choice < 50 else 2)) % 3 + 1 if escalate else 4
                        actual = [p.read(factions[1], "i")[0], bool(p.read(factions[1] + 0x79, "B")[0]), list(talks)]
                        expected = [expected_camp, True if mode != 3 else warned, [3 if escalate else 2] if mode != 3 else []]
                        assert actual == expected, (source_camp, warned, draw, choice, mode, actual, expected)
                        vectors.append(dict(sourceCamp=source_camp, warned=warned, draw=draw, choice=choice,
                            mode=mode, result=actual, draws=2-len(rolls)))
    (output / "provocation_attack_vectors.json").write_text(json.dumps(vectors, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps(dict(captureChecks=capture_checks, attackChecks=len(vectors), errors=0,
        scope="Original ARM64 50e00/825d4 diplomacy; ownership, damage, defeat, events and UI are stubs")))


if __name__ == "__main__":
    main()
