"""Execute native path initialization; stub map lookup and neighbor expansion only."""
import itertools
import json
from validate_native_rules import NativeProbe, ROOT, SCRATCH


def main():
    p = NativeProbe()
    path, tile, unit, definition = SCRATCH, SCRATCH + 0x11000, SCRATCH + 0x12000, SCRATCH + 0x13000
    p.cpu.mem_map(SCRATCH + 0x10000, 0x10000)
    p.hook(0x85824, lambda *args: tile)
    p.hook(0x4ef64, lambda *args: unit)
    p.hook(0x8dbd4, lambda *args: 0)
    p.write(unit + 8, "Q", definition)
    p.write(definition + 0xc, "i", 0)
    vectors = []
    for sea, transport, budget, override in itertools.product((0, 1), (0, 1), (2, 6, 12, 25), (0, 30)):
        p.write(tile + 0x1c, "B", sea)
        p.write(unit + 0x59, "B", transport)
        p.write(unit + 0x28, "i", budget)
        p.call(0x8dd7c, path, 0, 1, override)
        actual = p.read(path + 0x10, "i")[0]
        expected = 2 if sea and transport else override or budget
        assert actual == expected, (sea, transport, budget, override, actual)
        vectors.append(dict(sea=bool(sea), transport=bool(transport), budget=budget, override=override, expected=actual))
    output = ROOT / "reverse/reports/transport_budget_vectors.json"
    output.write_text(json.dumps(vectors, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(checks=len(vectors), errors=0)))


if __name__ == "__main__":
    main()
