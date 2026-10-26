"""Exercise original star payments/upgrades and export the recovered price table."""
import json
import sys
from pathlib import Path
from validate_native_rules import NativeProbe, SCRATCH


def main():
    p = NativeProbe()
    head, bank = SCRATCH + 0x1000, SCRATCH + 0x5000
    p.write(head + 0xb80, "Q", bank)
    costs = [list(p.read(0x1910d0 + slot * 16, "4i")) for slot in range(26)]
    checks = 0
    for zone in range(6):
        for slot in range(26):
            for level in range(-1, 4):
                cost = costs[slot][level + 1] if level < 3 else 0
                for balance in sorted({0, max(0, cost - 1), cost, 999}):
                    levels = [1] * 26
                    levels[slot] = level
                    p.write(head + zone * 0xe4 + 0x1ac, "26i", *levels)
                    p.write(bank + 0x24, "i", balance)
                    if level < 3:
                        assert p.call(0x2c89c, head, zone, slot) == cost
                        checks += 1
                    success = level < 3 and balance >= cost
                    assert p.call(0x2c99c, head, zone, slot) == int(success)
                    levels[slot] += int(success)
                    assert list(p.read(head + zone * 0xe4 + 0x1ac, "26i")) == levels
                    assert p.read(bank + 0x24, "i")[0] == balance - (cost if success else 0)
                    checks += 3
    for previous, stars in [(0, 5), (5, 3), (3, 5), (5, 5)]:
        for balance in [0, 995, 999]:
            p.write(head + 0x15c, "i", previous)
            p.write(bank + 0x24, "i", balance)
            # 5eb60/5eb6c stage records start at warzone +0x28.
            p.call(0x2c708, head, 0, 0, stars)
            assert p.read(bank + 0x24, "i")[0] == min(999, balance + max(0, stars - previous))
            checks += 1
    output = Path(sys.argv[1])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(dict(checks=checks, errors=0, costs=costs)), encoding="utf-8")
    print(json.dumps(dict(checks=checks, errors=0, costs=len(costs))))


if __name__ == "__main__":
    main()
