"""Measure scoped jurisdiction indexes against the already-frozen P0 result."""
import argparse
import hashlib
import json
import os
from pathlib import Path
from statistics import median
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--extended", action="store_true")
    parser.add_argument("--baseline-long", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    parent = root / "build/turn-regression-20261025"
    folder = parent / "jurisdiction"
    folder.mkdir(exist_ok=True)
    began = time.monotonic()

    def execute(command, log, env=None):
        with log.open("w", encoding="utf-8") as stream:
            result = subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT,
                timeout=max(1, 21600 - (time.monotonic() - began)))
        if result.returncode:
            raise RuntimeError(f"Exit {result.returncode}: {log}")

    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    if args.prepare:
        if (folder / "after").exists():
            raise RuntimeError("Frozen input already exists")
        execute([sys.executable, str(root / "tools/PerformanceAudit/prepare.py"), str(root), str(folder / "after")], folder / "freeze.log")
        old = {f["path"]: f["sha256"] for f in json.loads((parent / "combined/source-manifest.json").read_text())["files"]}
        new = {f["path"]: f["sha256"] for f in json.loads((folder / "after/source-manifest.json").read_text())["files"]}
        assert sorted(p for p in old.keys() | new.keys() if old.get(p) != new.get(p)) == ["Ew4.Core/PlanningQueries.cs", "Ew4.Core/TerritorialJurisdictions.cs"]
        # Freeze an exact source tree before adding diagnostic probes.
        source = folder / "source-after/rewrite"
        for entry in json.loads((folder / "after/source-manifest.json").read_text())["files"]:
            text = (folder / "after" / entry["path"]).read_text(encoding="utf-8-sig")
            variants = [text.encode(), text.replace("\n", "\r\n").encode()]
            variants += [b"\xef\xbb\xbf" + raw for raw in variants]
            raw = next(raw for raw in variants if hashlib.sha256(raw).hexdigest() == entry["sha256"])
            target = source / entry["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        for name, origin in (("before-deep", parent / "source-combined"), ("after-deep", folder / "source-after")):
            execute([sys.executable, str(root / "tools/PerformanceAudit/prepare.py"), str(root), str(folder / name), "--source-root", str(origin), "--instrument"], folder / (name + "-prepare.log"))
        for name in ("after", "before-deep", "after-deep"):
            execute(["dotnet", "build", str(folder / name / "Harness"), "-c", "Release", "-v", "quiet"], folder / (name + "-build.log"))
        print(json.dumps({"prepared": 3}), flush=True)

    def run(stage, label, samples=1, rounds=1, **settings):
        output = folder / label
        if (output / "inputs.json").exists():
            return
        if output.exists():
            raise RuntimeError("Inspect incomplete run: " + str(output))
        binary = parent / "baseline" if stage == "original" else parent / "combined" if stage == "before" else folder / stage
        env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_MODES="batch", EW4_AUDIT_DELTA="1",
            EW4_AUDIT_TRACE="0", EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS=str(rounds))
        for key in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION", "EW4_AUDIT_RETAINED_MEMORY"):
            env.pop(key, None)
        env.update(settings)
        execute(["dotnet", str(binary / "Harness/bin/Release/net10.0/Harness.dll"), str(parent / "content"), str(output)], folder / (label + ".log"), env)
        print(json.dumps({"completed": label}), flush=True)

    if args.run:
        for group, order in enumerate((("before", "after"), ("after", "before"))):
            for stage in order:
                run(stage, f"{stage}-group{group}", samples=4)
        for stage in ("before", "after"):
            run(stage + "-deep", stage + "-diagnostic")
        run("after", "after-six", rounds=6)
        run("after", "after-trace", rounds=6, EW4_AUDIT_TRACE="1", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_DUAL_VALIDATION="1")
        run("after", "after-conquest4", EW4_AUDIT_SCENARIOS="conquest4", EW4_AUDIT_TRACE="1", EW4_AUDIT_MODES="ordinary")
        run("after", "after-late", samples=4, EW4_AUDIT_START_SLOT=str(parent / "baseline-six/conquest1-batch-0/round-6-start.json"))
    if args.extended:
        run("before", "before-twenty", rounds=20)
        run("after", "after-twenty", rounds=20)
        run("after", "after-round20", EW4_AUDIT_START_SLOT=str(folder / "before-twenty/conquest1-batch-0/round-20-start.json"))
    if args.baseline_long:
        run("original", "original-twenty", rounds=20)

    def rows(path):
        return json.loads((path / "results.json").read_text())

    fields = ("initialSha256", "finalSha256", "RandomState", "AiRandomState", "steps", "stopReason", "Round")

    def compare(paths, trace=False):
        reference = {r["roundIndex"]: r for r in rows(paths[0])}
        for path in paths[1:]:
            candidate = rows(path)
            assert set(r["roundIndex"] for r in candidate) == set(reference), path
            for row in candidate:
                for field in fields + (("sequenceSha256",) if trace else ()):
                    assert row[field] == reference[row["roundIndex"]][field], (path, field, row["roundIndex"])

    report = {"errors": 0, "scope": "P1 vs P0, same frozen current content, desktop client RPC and saves; no phone timing"}
    frozen = {f["path"]: f["sha256"] for f in json.loads((parent / "content-manifest.json").read_text())["files"]}
    for path in folder.glob("*/inputs.json"):
        assert all(frozen.get(p) == value for p, value in json.loads(path.read_text())["content"].items()), path
    if (folder / "before-group1/inputs.json").exists():
        compare([folder / f"{s}-group{g}" for s in ("before", "after") for g in range(2)])
        report["timings"] = {}
        for stage in ("before", "after"):
            data = rows(folder / (stage + "-group0")) + rows(folder / (stage + "-group1"))
            warm = [r for r in data if not r["warmup"]]
            report["timings"][stage] = {"warmSamples": len(warm), "medianMs": median(r["elapsedMs"] for r in warm),
                "minMs": min(r["elapsedMs"] for r in warm), "maxMs": max(r["elapsedMs"] for r in warm),
                "coldMs": [r["elapsedMs"] for r in data if r["warmup"]], "allocatedBytes": median(r["allocatedBytes"] for r in warm)}
    if (folder / "after-diagnostic/inputs.json").exists():
        compare([folder / (s + "-diagnostic") for s in ("before", "after")])
        report["diagnostics"] = {s: {k: rows(folder / (s + "-diagnostic"))[0][k] for k in ("metrics", "counters", "worldValidation")} for s in ("before", "after")}
    for suffix in ("six", "trace", "conquest4", "late"):
        if (folder / ("after-" + suffix) / "inputs.json").exists():
            compare([parent / ("combined-" + suffix), folder / ("after-" + suffix)], suffix in ("trace", "conquest4"))
            report[suffix] = [{k: r[k] for k in ("roundIndex", "elapsedMs", "allocatedBytes", "steps", "stopReason")} for r in rows(folder / ("after-" + suffix))]
    if (folder / "after-twenty/inputs.json").exists():
        compare([folder / (s + "-twenty") for s in ("before", "after")])
        report["twenty"] = {s: [{k: r[k] for k in ("roundIndex", "elapsedMs", "allocatedBytes", "steps", "stopReason")}
            for r in rows(folder / (s + "-twenty"))] for s in ("before", "after")}
    if (folder / "after-round20/inputs.json").exists():
        expected, actual = rows(folder / "before-twenty")[-1], rows(folder / "after-round20")[0]
        for field in fields:
            assert actual[field] == expected[field], ("round20 reload", field)
        report["round20Reload"] = {"errors": 0, "elapsedMs": actual["elapsedMs"], "steps": actual["steps"]}
    if (folder / "original-twenty/inputs.json").exists():
        compare([folder / name for name in ("original-twenty", "before-twenty", "after-twenty")])
        report["originalTwenty"] = [{k: r[k] for k in ("roundIndex", "elapsedMs", "allocatedBytes", "steps", "stopReason")}
            for r in rows(folder / "original-twenty")]
        report["originalTwentySteps"] = sum(r["steps"] for r in rows(folder / "original-twenty"))
    report["sourceManifests"] = {s: sha((parent / "combined" if s == "before" else folder / "after") / "source-manifest.json") for s in ("before", "after")}
    (folder / "summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k not in ("diagnostics", "twenty", "originalTwenty")}), flush=True)


if __name__ == "__main__":
    main()
