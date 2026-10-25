"""Verify long-curve identities and summarize work growth separately from timing."""
import argparse
import json
from pathlib import Path
from statistics import median

from measure_stages import source_index


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-plateau-20261024"
    load = lambda name: json.loads((folder / name).read_text(encoding="utf-8-sig"))
    manifest = {f["path"]: f["sha256"] for f in load("after/source-manifest.json")["files"]}
    assert source_index(root) == manifest, "Runtime source drift"
    fields = ("initialSha256", "steps", "Round", "RandomState", "AiRandomState", "finalSha256", "stopReason")
    identity = lambda r: {key: r[key] for key in fields}
    expected, content = {}, None
    diagnostics, curves, reloads = {}, {}, {}
    for stage in ("before", "after", "before-deep", "after-deep", "cause"):
        inputs = load(stage + "-long/inputs.json")
        if content is None:
            content = inputs["content"]
        assert content == inputs["content"], "Content identity differs"
        rows = load(stage + "-long/results.json")
        assert len(rows) == (40 if stage in ("before", "after") else 20)
        for row in rows:
            key = row["initialHistory"]["Round"]
            expected.setdefault(key, identity(row))
            assert expected[key] == identity(row), (stage, key)
        if stage in ("before", "after"):
            curves[stage] = [{"round": r["initialHistory"]["Round"], "ms": r["elapsedMs"],
                              "allocatedMiB": r["allocatedBytes"] / 1048576}
                             for r in rows if r["sample"] == 1]
        else:
            diagnostics[stage] = [{"round": r["initialHistory"]["Round"], "units": r["units"], "steps": r["steps"],
                "history": r["initialHistory"], "counters": r["counters"], "validation": r["worldValidation"],
                "phases": [{k: m[k] for k in ("Name", "Calls", "ExclusiveMs", "ExclusiveBytes")} for m in r["metrics"]]} for r in rows]
    assert len(expected) == 20
    for stage in ("before", "after"):
        reloads[stage] = []
        for number in (1, 10, 20):
            rows = load(f"{stage}-reload-{number}/results.json")
            assert len(rows) == 3 and all(identity(r) == expected[number] for r in rows)
            warm = rows[1:]
            reloads[stage].append({"round": number, "processFirstMs": rows[0]["elapsedMs"],
                "warmMs": [r["elapsedMs"] for r in warm], "warmMedianMs": median(r["elapsedMs"] for r in warm),
                "allocatedMiB": median(r["allocatedBytes"] / 1048576 for r in warm)})
    result = {"errors": 0, "rounds": 20, "identities": expected, "diagnostics": diagnostics,
        "curves": curves, "reloads": reloads,
        "scope": "Desktop batch/delta. Twenty-round curves overlap frontend development/validation and are workload diagnostics, not a controlled timing comparison. Fresh-process reloads are serial, two warm samples per fixed world. Phone plateau boundaries remain unmeasured."}
    (folder / "long-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"errors": 0, "rounds": 20, "reloads": reloads}))


if __name__ == "__main__":
    main()
