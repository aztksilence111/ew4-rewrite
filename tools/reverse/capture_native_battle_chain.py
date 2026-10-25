"""Capture real combat dispatch + death + fire + troop-XP as one ARM64 chain.

Commander progression is included with an injected eligibility gate; diplomacy,
medals, survival cascade, and rendering are outside this experiment.
"""

from collections import Counter
import json
import random
import sys

from validate_native_rules import NativeProbe, ROOT, SCRATCH, RNG_STATE
sys.path.insert(0, str(ROOT / "tools"))
from catalog_files import read_catalog


def main(catalog_mode=False):
    p = NativeProbe()
    battle, system = SCRATCH, SCRATCH + 0x8000
    tiles = [SCRATCH + 0x1000 + i * 0x100 for i in range(3)]
    units = [SCRATCH + 0x2000 + i * 0x200 for i in range(3)]
    definitions = [SCRATCH + 0x3000 + i * 0x100 for i in range(3)]
    factions = [SCRATCH + 0x4000 + i * 0x400 for i in range(2)]
    rows = [SCRATCH + 0x6000 + i * 0x20 for i in range(6)]
    buildings = [SCRATCH + 0x7000 + i * 0x100 for i in range(3)]
    commanders = [SCRATCH + 0x9000 + i * 0x100 for i in range(3)]
    commander_definitions = [SCRATCH + 0xa000 + i * 0x100 for i in range(3)]
    rank_costs = list(p.read(0x194da0, "14i"))
    title_costs = list(p.read(0x194e20, "9i"))
    rank_bonuses = list(p.read(0x195780, "15i"))
    title_healing = list(p.read(0x1957c0, "10i"))
    p.hook(0x85824, lambda _, cell, *args: tiles[cell] if cell < 3 else 0)
    p.hook(0x31844, lambda *args: system)
    p.hook(0x31c7c, lambda *args: 0)
    p.hook(0x80c18, lambda *args: 0)
    p.hook(0x2f0f0, lambda *args: 3)
    if not catalog_mode:
        p.hook(0x6ce7c, lambda _, level, *args: rows[level])
    p.hook(0x563e4, lambda *args: 1)
    p.hook(0x56cf0, lambda *args: -1)
    for address in (0x51c84, 0x52ac8, 0x5906c, 0x58c00, 0x58fb4, 0x5c618, 0x86b50,
                    0x5be90, 0x5c060, 0xbcf08, 0xbcfd8, 0xbcb4c, 0xc60d0, 0xc6328):
        p.hook(address, lambda *args: 0)
    generator = random.Random(1928)
    catalog = read_catalog(ROOT / "rewrite/data/legacy/catalog.json") if catalog_mode else None
    weapons = ("gun", "guns", "mgun", "cannon", "rocket", "cold")
    weapon_by_profile = {}
    if catalog_mode:
        for country in catalog["source_trees"]["def_army.xml"]["children"]:
            for army in country["children"]:
                a = army["attributes"]
                key = f'ew4.unit.{country["attributes"]["name"]}.{a["name"].lower().replace(" ", "_")}.{a["grade"]}'
                weapon_by_profile[key] = weapons.index(a["weapon"])
    counts, errors, fixtures = Counter(), [], []
    for case in range(800):
        for faction in factions:
            p.cpu.mem_write(faction, bytes(0x400))
        stages = [dict(cost=p.read(0x2211d0 + i * 16 + 12, "i")[0], heal=p.read(0x2211d0 + i * 16 + 8, "i")[0])
                  if catalog_mode else dict(cost=generator.randrange(30, 121), heal=generator.randrange(1, 31)) for i in range(len(rows))]
        for i, stage in enumerate(stages):
            p.write(rows[i] + 8, "2i", stage["heal"], stage["cost"])
        facts = []
        for i, (tile, unit, definition, building) in enumerate(zip(tiles, units, definitions, buildings)):
            fact = dict(health=generator.randrange(1, 151), category=generator.randrange(5), grade=generator.randrange(3),
                        weapon=generator.randrange(5), level=generator.randrange(6), experience=generator.randrange(120),
                        morale=generator.randrange(-3, 2), sea=bool(generator.randrange(2)), building=bool(generator.randrange(2)),
                        terrain=generator.randrange(3), transport=bool(generator.randrange(2)), environment=generator.randrange(4), burning=generator.randrange(4))
            fact["commander"] = None
            fact["maximum"] = 150
            if catalog_mode:
                profile = generator.choice(catalog["profiles"])
                fact.update(profile_id=profile["id"], category=["infantry", "cavalry", "artillery", "naval", "fort"].index(profile["category"]),
                            grade=int(profile["grade"]), weapon=weapon_by_profile[profile["id"]], base_health=profile["max_health"],
                            maximum=profile["max_health"], movement=profile["movement"])
                fact["health"] = generator.randrange(1, fact["maximum"] + 1)
            if case >= 400 and generator.randrange(3) != 0:
                rank, title = generator.randrange(5), generator.randrange(9)
                skills = generator.sample([2, 3, 19, 20, 21, 0], generator.randrange(5))
                fact["commander"] = dict(rank=rank, title=title, rank_xp=generator.randrange(rank_costs[rank]),
                                         title_xp=generator.randrange(title_costs[title]), skills=skills)
                fact["maximum"] += rank_bonuses[rank]
                fact["health"] = generator.randrange(1, fact["maximum"] + 1)
            facts.append(fact)
            p.cpu.mem_write(tile, bytes(0x100))
            p.cpu.mem_write(unit, bytes(0x200))
            p.cpu.mem_write(definition, bytes(0x100))
            faction = factions[0 if i == 0 else 1]
            p.write(tile + 8, "i", i)
            p.write(tile + 0x1c, "B", int(fact["sea"]))
            p.write(tile + 0x3c, "i", fact["terrain"])
            p.write(tile + 0x40, "Q", building if fact["building"] else 0)
            p.write(tile + 0x50, "i", fact["environment"])
            p.write(tile + 0x68, "2Q", faction, unit)
            p.write(tile + 0x78, "i", 1)
            p.write(unit + 8, "2Q", definition, faction)
            p.write(unit + 0x20, "3i", fact["health"], fact["maximum"], fact.get("movement", 6))
            p.write(unit + 0x34, "2i", fact["level"], fact["experience"])
            p.write(unit + 0x3c, "3i", fact["morale"], 0, fact["morale"])
            p.write(unit + 0x4c, "i", fact["burning"])
            p.write(unit + 0x59, "B", int(fact["transport"]))
            p.write(unit + 0x70, "i", -1)
            p.write(definition + 8, "3i", 10 + i, fact["category"], fact["grade"])
            p.write(definition + 0x28, "i", fact["weapon"])
            p.write(definition + 0x14, "i", fact.get("base_health", 150))
            if fact["commander"] is not None:
                general, general_definition, values = commanders[i], commander_definitions[i], fact["commander"]
                p.cpu.mem_write(general, bytes(0x100))
                p.cpu.mem_write(general_definition, bytes(0x100))
                p.write(unit + 0x18, "Q", general)
                p.write(general, "i", 100 + i)
                p.write(general + 8, "Q", general_definition)
                p.write(general + 0x10, "4i", values["rank"], values["title"], values["rank_xp"], values["title_xp"])
                p.write(general + 0x40, "3i", len(values["skills"]), -1, -1)
                p.write(general_definition + 0x34, "4i", *(values["skills"] + [-1] * (4 - len(values["skills"]))))
        counter = bool(generator.randrange(2))
        taken, dealt = generator.randrange(101) if counter else 0, generator.randrange(101)
        splash, seed = generator.randrange(61) if generator.randrange(2) else 0, generator.getrandbits(64)
        p.cpu.mem_write(battle, bytes(0x100))
        p.write(battle + 0x20, "2i", 0, 1)
        p.write(battle + 0x30, "2i", taken, dealt)
        p.write(battle + 0x3c, "2i", 2 if splash else -1, splash)
        p.write(battle + 0x44, "B", int(counter))
        p.write(RNG_STATE, "Q", seed)
        p.call(0x825d4, battle)
        output = []
        for index, (tile, unit) in enumerate(zip(tiles, units)):
            output.append(dict(health=p.read(unit + 0x20, "i")[0], occupied=bool(p.read(tile + 0x70, "Q")[0]),
                               maximum=p.read(unit + 0x24, "i")[0],
                               level=p.read(unit + 0x34, "i")[0], experience=p.read(unit + 0x38, "i")[0],
                               morale=list(p.read(unit + 0x3c, "3i")), movement=p.read(unit + 0x28, "i")[0],
                               acted=bool(p.read(unit + 0x5a, "B")[0]), environment=p.read(tile + 0x50, "i")[0],
                               burning=p.read(unit + 0x4c, "i")[0], owner=bool(p.read(tile + 0x68, "Q")[0]),
                               commander=list(p.read(commanders[index] + 0x10, "4i")) if facts[index]["commander"] is not None else None))
        counts["battle_chain"] += 1
        counts["units_destroyed"] += sum(not out["occupied"] for out in output)
        counts["troop_advances"] += sum(out["level"] > fact["level"] for out, fact in zip(output, facts))
        counts["ignition_changes"] += sum(out["environment"] != fact["environment"] or out["burning"] != fact["burning"] for out, fact in zip(output, facts))
        counts["commander_rank_advances"] += sum(out["commander"] is not None and out["commander"][0] > fact["commander"]["rank"] for out, fact in zip(output, facts))
        counts["commander_title_advances"] += sum(out["commander"] is not None and out["commander"][1] > fact["commander"]["title"] for out, fact in zip(output, facts))
        if any(out["health"] < 0 or out["health"] > out["maximum"] or not 0 <= out["level"] <= 5 or out["occupied"] != (out["health"] > 0) for out in output):
            errors.append(case)
        fixtures.append(dict(units=facts, stages=stages, counter=counter, taken=taken, dealt=dealt, splash=splash, seed=seed,
                             expected=dict(units=output, random_state=p.read(RNG_STATE, "Q")[0])))
    report = dict(counts=dict(counts), errors=errors,
                  scope="Original 825d4 -> damage/death, ignition, troop-XP/healing, commander rank/title-XP/health scaling and morale executed together. 400 cases without commanders + 400 allowing commanders; progression eligibility injected true, equipment lookup absent, map/troop rows and nondefeat/local refresh/rendering injected; mode3 medal/diplomacy bypass. Not a phone test.")
    if catalog_mode:
        report["scope"] = "Original combat commit chain with actual imported unit category/grade/weapon/base health and native veterancy rows; supplied damage, generated commander states, injected eligibility/map/survival/rendering; not full damage formula or device test."
    report_name = "native_catalog_battle_capture.json" if catalog_mode else "native_battle_chain_capture.json"
    vector_name = "catalog_battle_vectors.json" if catalog_mode else "battle_chain_vectors.json"
    (ROOT / "reverse/reports" / report_name).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not errors:
        (ROOT / "reverse/reports" / vector_name).write_text(json.dumps(dict(fixtures=fixtures, rank_costs=rank_costs,
            title_costs=title_costs, rank_bonuses=rank_bonuses, title_healing=title_healing), separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps(report))
    if errors:
        raise SystemExit(1)


if __name__ == "__main__":
    main(catalog_mode="--catalog" in sys.argv[1:])
