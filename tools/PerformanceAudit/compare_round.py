"""Compare an isolated before/after round, keeping trace separate from timings."""
import argparse
import hashlib
import json
from pathlib import Path
from statistics import mean


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def change(before, after):
    return {"before": before, "after": after, "reductionPercent": (1 - after / before) * 100}


def metrics(rows):
    warm = rows[1:]
    return {
        "firstMs": rows[0]["elapsedMs"],
        "warmMeanMs": mean(r["elapsedMs"] for r in warm),
        "warmRangeMs": [min(r["elapsedMs"] for r in warm), max(r["elapsedMs"] for r in warm)],
        "warmAllocatedMiB": mean(r["allocatedBytes"] / 1048576 for r in warm),
        "warmGc": [mean(r["gc"][i] for r in warm) for i in range(3)],
        "warmRequestMaxMs": [r["requestMaxMs"] for r in warm],
        "warmRequests": [r["requests"] for r in warm],
    }


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("round", type=Path)
    args = parser.parse_args()
    root, folder = args.root.resolve(), args.round.resolve()
    if root / "build" not in folder.parents:
        raise ValueError("Evidence must be under repository build/")
    plain = [load(folder / f"{stage}-results/results.json") for stage in ("before", "after")]
    deep = [load(folder / f"{stage}-deep-results/results.json") for stage in ("before", "after")]
    traces = [load(folder / f"{stage}-trace/results.json") for stage in ("before", "after")]
    identity_fields = ("scenario", "steps", "Round", "RandomState", "AiRandomState", "finalSha256", "stopReason")
    identity = lambda row: {field: row[field] for field in identity_fields}
    for rows in plain + deep:
        assert len(rows) == 3 and all(r["mode"] == "batch" and r["delta"] for r in rows)
        assert all(identity(r) == identity(plain[0][0]) for r in rows), "Batch identity changed"
        assert all(r["sequenceSha256"] is None for r in rows), "Trace contaminated timing"
    assert len(traces[0]) == len(traces[1]) == 2
    equivalence = []
    for before, after in zip(*traces):
        assert before["mode"] == after["mode"] == "ordinary"
        assert identity(before) == identity(after) and before["sequenceSha256"] == after["sequenceSha256"], "Step trace changed"
        equivalence.append({**identity(after), "sequenceSha256": after["sequenceSha256"]})
    manifests = [load(folder / f"{stage}/source-manifest.json")["files"] for stage in ("before", "after")]
    indexes = [{entry["path"]: entry["sha256"] for entry in entries} for entries in manifests]
    changed = sorted(path for path in indexes[0].keys() | indexes[1].keys() if indexes[0].get(path) != indexes[1].get(path))
    stale = [path for path, digest in indexes[1].items() if hashlib.sha256((root / "rewrite" / path).read_bytes()).hexdigest() != digest]
    assert not stale, "Source changed after capture: " + ", ".join(stale)
    for stage, index in zip(("before", "after"), indexes):
        observed = {e["path"]: e["sha256"] for e in load(folder / f"{stage}-deep/source-manifest.json")["files"]}
        assert observed == index, "Plain/instrumented inputs differ"
    timings = [metrics(rows) for rows in plain]
    world = {}
    for field in ("calls", "cellVisits", "unitVisits"):
        values = [mean(r["worldValidation"][field] for r in rows[1:]) for rows in deep]
        world[field] = change(*values)
    probes = {}
    for label in ("Combat.UnitProfile.Validate", "Combat.UnitProfile.ValidateCore", "Combat.Combatant.ValidateCore", "WorldState.Copy", "ClientGame.Save", "Save.flush"):
        values = []
        for rows in deep:
            observed = [next((m for m in row["metrics"] if m["Name"] == label), None) for row in rows[1:]]
            values.append({key: mean(m[key] for m in observed) for key in ("Calls", "InclusiveMs", "ExclusiveBytes")} if all(observed) else None)
        probes[label] = dict(zip(("before", "after"), values))
    result = {"errors": 0, "source": {"beforeFiles": len(indexes[0]), "afterFiles": len(indexes[1]), "changed": changed, "stale": stale},
              "equivalence": equivalence, "plain": dict(zip(("before", "after"), timings)),
              "warmTime": change(*[r["warmMeanMs"] for r in timings]),
              "warmAllocation": change(*[r["warmAllocatedMiB"] for r in timings]),
              "worldValidation": world, "probes": probes,
              "limitations": ["Desktop isolated batch timing, not Android measurements.", "Two warm samples per build; no p95 claim.", "Trace is a separate correctness run and excluded from timing."]}
    (folder / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"errors": 0, "sourceChanges": len(changed), "staleSources": len(stale), "equivalentScenarios": len(equivalence),
                      "warmTimeReductionPercent": result["warmTime"]["reductionPercent"],
                      "warmAllocationReductionPercent": result["warmAllocation"]["reductionPercent"]}))


if __name__ == "__main__":
    main()
