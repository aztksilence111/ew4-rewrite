"""Sample the original skill-31/32 damage and RNG branches for focused comparison."""
import json
import sys
from pathlib import Path
from validate_native_combat import CombatProbe, Combatant
from validate_native_rules import ROOT


def main():
    probe = CombatProbe()
    vectors = []
    for offense, defense in [(False, False), (True, False), (True, True)]:
        attacker = Combatant(health=100, maximum=100, minimum_attack=2, maximum_attack=9,
                             skills=(31,) if offense else ())
        defender = Combatant(health=100, maximum=100, minimum_attack=2, maximum_attack=9,
                             skills=(32,) if defense else ())
        for seed in range(1, 201):
            result = probe.run(attacker, defender, 1, seed)
            vectors.append(dict(seed=seed, offense=offense, defense=defense, expected=result))
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'build/critical-audit-20261102/native.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(dict(vectors=vectors, errors=0), separators=(',', ':')) + '\n', encoding='utf-8')
    print(json.dumps(dict(cases=len(vectors), offensiveProcs=sum(v['expected']['offensive_proc'] for v in vectors),
                          defensiveProcs=sum(v['expected']['defensive_proc'] for v in vectors), errors=0)))


if __name__ == '__main__':
    main()
