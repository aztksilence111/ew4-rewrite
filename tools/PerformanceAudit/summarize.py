"""Validate fixed-session equivalence and summarize the saved diagnostic samples."""
import json
import statistics
import sys
from pathlib import Path

folder = Path(sys.argv[1])
datasets = {name: json.loads((folder / f"{name}-results/results.json").read_text()) for name in ("plain", "instrumented", "deep")}
expected = {}
errors = []
for dataset, rows in datasets.items():
    for row in rows:
        identity = (row["finalSha256"], row["RandomState"], row["AiRandomState"], row["steps"], row["stopReason"])
        expected.setdefault(row["scenario"], identity)
        if identity != expected[row["scenario"]]:
            errors.append(f"Result mismatch: {dataset}/{row['scenario']}/{row['mode']}/{row['sample']}")
manifests = {name: {row["path"]: row["sha256"] for row in json.loads((folder / name / "source-manifest.json").read_text())["files"]} for name in datasets}
if not all(value == manifests["plain"] for value in manifests.values()):
    errors.append("Source inputs differed between C# builds")
baseline = []
for scenario in expected:
    for mode in ("batch", "ordinary"):
        rows = [r for r in datasets["plain"] if r["scenario"] == scenario and r["mode"] == mode and not r["warmup"]]
        baseline.append({"scenario": scenario, "mode": mode, "samples": len(rows),
            "secondsRange": [min(r["elapsedMs"] for r in rows)/1000, max(r["elapsedMs"] for r in rows)/1000],
            "dispatchSecondsMedian": statistics.median(r["dispatchMs"] for r in rows)/1000,
            "allocatedGiBMedian": statistics.median(r["allocatedBytes"] for r in rows)/(1024**3),
            "responseMBMedian": statistics.median(r["responseBytes"] for r in rows)/1e6})
result = {"scope": "Desktop fixed inputs; no Android device claim. Plain includes harness response parsing; Dispatch excludes it. Nested inclusive times must not be summed.",
    "runs": sum(map(len, datasets.values())), "errors": errors, "baseline": baseline, "finalIdentities": expected}
(folder / "summary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
if errors:
    raise SystemExit(1)
