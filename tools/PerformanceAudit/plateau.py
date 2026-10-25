"""Prepare and verify the plateau optimization using frozen stage sources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
from statistics import median
import subprocess
import sys
import time

from measure_growth import summarize


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--followup", action="store_true")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-plateau-20261024"
    if args.prepare:
        for stage in ("before", "search", "ownership", "queries", "after"):
            origin = folder / stage
            manifest = json.loads((origin / "source-manifest.json").read_text())
            frozen = folder / ("source-" + stage)
            for entry in manifest["files"]:
                text = (origin / entry["path"]).read_text(encoding="utf-8-sig")
                variants = [text.encode(), text.replace("\n", "\r\n").encode()]
                variants += [b"\xef\xbb\xbf" + value for value in variants]
                raw = next(value for value in variants if hashlib.sha256(value).hexdigest() == entry["sha256"])
                target = frozen / "rewrite" / entry["path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(raw)
            if stage in ("before", "after"):
                subprocess.run([sys.executable, str(root / "tools/PerformanceAudit/prepare.py"), str(root),
                    str(folder / (stage + "-deep")), "--source-root", str(frozen), "--instrument"], check=True)
            for name in [stage] + ([stage + "-deep"] if stage in ("before", "after") else []):
                subprocess.run([args.dotnet, "build", str(folder / name / "Harness"), "-c", "Release", "-v", "quiet"], check=True)
    elif args.followup:
        began = time.monotonic()
        def run(stage, suffix, samples=1, rounds=1, **settings):
            output = folder / (stage + "-" + suffix)
            if (output / "inputs.json").exists():
                return
            if output.exists():
                raise RuntimeError("Inspect incomplete run: " + str(output))
            env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_MODES="batch", EW4_AUDIT_DELTA="1",
                EW4_AUDIT_TRACE="0", EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS=str(rounds))
            for name in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION", "EW4_AUDIT_RETAINED_MEMORY"):
                env.pop(name, None)
            env.update(settings)
            subprocess.run([args.dotnet, str(folder / stage / "Harness/bin/Release/net10.0/Harness.dll"), str(root), str(output)],
                env=env, check=True, timeout=max(1, 21600 - (time.monotonic() - began)))
        for stage in ("search", "ownership", "queries"):
            run(stage, "late", samples=3, EW4_AUDIT_START_SLOT=str(folder / "before-group0/conquest1-batch-0/round-6-start.json"))
        for stage in ("before", "after"):
            run(stage, "conquest4-trace", EW4_AUDIT_SCENARIOS="conquest4", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
        run("after", "dual", rounds=6, EW4_AUDIT_DUAL_VALIDATION="1", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
        def load(name):
            return json.loads((folder / name / "results.json").read_text())
        fields = ("initialSha256", "steps", "Round", "RandomState", "AiRandomState", "finalSha256", "stopReason")
        identity = lambda row: {key: row[key] for key in fields}
        late = {}
        expected = identity(load("before-late")[0])
        for stage in ("before", "search", "ownership", "queries", "after"):
            rows = load(stage + "-late")
            assert all(identity(row) == expected for row in rows), "Ablation state/RNG differs: " + stage
            warm = [row for row in rows if not row["warmup"]]
            late[stage] = {"firstMs": rows[0]["elapsedMs"], "warmMedianMs": median(row["elapsedMs"] for row in warm),
                "allocatedMiB": median(row["allocatedBytes"] / 1048576 for row in warm), "warmSamples": len(warm)}
        first, second = load("before-conquest4-trace")[0], load("after-conquest4-trace")[0]
        assert identity(first) == identity(second) and first["sequenceSha256"] == second["sequenceSha256"], "Conquest4 per-step differs"
        dual = load("after-dual")
        trace = load("before-trace")
        assert len(dual) == len(trace) == 6
        assert all(identity(a) == identity(b) and a["sequenceSha256"] == b["sequenceSha256"] for a, b in zip(dual, trace)), "Full validator dual-run differs"
        diagnostics = {}
        for stage in ("before", "after"):
            rows = load(stage + "-deep-results")
            diagnostics[stage] = [{"round": row["roundIndex"] + 1, "validation": row["worldValidation"],
                "searchVisits": row["counters"].get("search.visit", 0), "ownershipVisits": row["counters"].get("candidate.ownershipVisits", 0),
                "copies": next(m["Calls"] for m in row["metrics"] if m["Name"] == "WorldState.Copy"),
                "flushes": next(m["Calls"] for m in row["metrics"] if m["Name"] == "Save.flush")} for row in rows]
        result = {"errors": 0, "late": late, "diagnostics": diagnostics, "conquest4Steps": first["steps"],
            "dualSteps": sum(row["steps"] for row in dual), "scope": "Stage ablation: same sixth-round save, fresh process per stage, two warm samples. Main six-round timings remain separate."}
        (folder / "followup.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result))
    else:
        summarize(root, folder)


if __name__ == "__main__":
    main()
