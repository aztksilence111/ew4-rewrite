"""Measure request-local battle reuse against frozen P0/P1, with real RPC/save work."""
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
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-regression-followup-20261025"
    content = root / "build/turn-regression-20261025/content"
    began = time.monotonic()

    def execute(command, log, env=None):
        log.parent.mkdir(parents=True, exist_ok=True)
        with log.open("w", encoding="utf-8") as stream:
            subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT,
                           check=True, timeout=max(1, 21600 - (time.monotonic() - began)))

    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    if args.prepare:
        assert not (folder / "after").exists(), "Preserve frozen inputs"
        old = json.loads((folder / "before/source-manifest.json").read_text())["files"]
        for entry in old:
            text = (folder / "before" / entry["path"]).read_text(encoding="utf-8-sig")
            variants = [text.encode(), text.replace("\n", "\r\n").encode()]
            variants += [b"\xef\xbb\xbf" + raw for raw in variants]
            raw = next(raw for raw in variants if hashlib.sha256(raw).hexdigest() == entry["sha256"])
            target = folder / "source-before/rewrite" / entry["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        for stage in ("before-deep", "after", "after-deep"):
            command = [sys.executable, "tools/PerformanceAudit/prepare.py", str(root), str(folder / stage)]
            if stage.startswith("before"):
                command += ["--source-root", str(folder / "source-before")]
            if stage.endswith("deep"):
                command += ["--instrument"]
            execute(command, folder / (stage + "-prepare.log"))
        for stage in ("before", "before-deep", "after", "after-deep"):
            execute(["dotnet", "build", str(folder / stage / "Harness"), "-c", "Release", "-v", "quiet"],
                    folder / (stage + "-build.log"))
        print(json.dumps({"prepared": 4}), flush=True)

    def run(stage, label, samples=1, rounds=1, **settings):
        output = folder / label
        if (output / "inputs.json").exists():
            return
        assert not output.exists(), "Inspect incomplete run: " + str(output)
        env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_MODES="batch", EW4_AUDIT_DELTA="1",
                   EW4_AUDIT_TRACE="0", EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS=str(rounds))
        for key in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION", "EW4_AUDIT_RETAINED_MEMORY"):
            env.pop(key, None)
        env.update(settings)
        execute(["dotnet", str(folder / stage / "Harness/bin/Release/net10.0/Harness.dll"), str(content), str(output)],
                folder / (label + ".log"), env)
        print(json.dumps({"completed": label}), flush=True)

    if args.run:
        for group, stages in enumerate((("before", "after"), ("after", "before"))):
            for stage in stages:
                run(stage, stage + "-group" + str(group), samples=4)
        for stage in ("before", "after"):
            run(stage + "-deep", stage + "-diagnostic")
            run(stage, stage + "-six", rounds=6)
            run(stage, stage + "-trace", rounds=6, EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
            run(stage, stage + "-conquest4", EW4_AUDIT_SCENARIOS="conquest4", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
            run(stage, stage + "-late", samples=4,
                EW4_AUDIT_START_SLOT=str(folder / "before-six/conquest1-batch-0/round-6-start.json"))
        run("after", "after-twenty", rounds=20)
        run("after", "after-round20", EW4_AUDIT_START_SLOT=str(root /
            "build/turn-regression-20261025/jurisdiction/before-twenty/conquest1-batch-0/round-20-start.json"))

    def rows(path):
        return json.loads((path / "results.json").read_text())

    fields = ("initialSha256", "finalSha256", "RandomState", "AiRandomState", "steps", "stopReason", "Round")
    frozen = {f["path"]: f["sha256"] for f in json.loads((content.parent / "content-manifest.json").read_text())["files"]}
    for path in folder.glob("*/inputs.json"):
        assert all(frozen.get(p) == value for p, value in json.loads(path.read_text())["content"].items()), path
    report = {"errors": 0, "scope": "Desktop client RPC and actual saves, frozen C content; no phone or browser timing", "timings": {}}
    for suffix in ("group0", "group1", "diagnostic", "six", "trace", "conquest4", "late"):
        if not (folder / ("after-" + suffix) / "inputs.json").exists():
            continue
        before = {r["roundIndex"]: r for r in rows(folder / ("before-" + suffix))}
        for row in rows(folder / ("after-" + suffix)):
            for key in fields + (("sequenceSha256",) if suffix in ("trace", "conquest4") else ()):
                assert row[key] == before[row["roundIndex"]][key], (suffix, key, row["roundIndex"])
        report[suffix] = {"errors": 0}
    if (folder / "after-group1/inputs.json").exists():
        for stage in ("before", "after"):
            data = rows(folder / (stage + "-group0")) + rows(folder / (stage + "-group1"))
            warm = [r for r in data if not r["warmup"]]
            report["timings"][stage] = {"warmSamples": len(warm), "medianMs": median(r["elapsedMs"] for r in warm),
                "rangeMs": [min(r["elapsedMs"] for r in warm), max(r["elapsedMs"] for r in warm)],
                "coldMs": [r["elapsedMs"] for r in data if r["warmup"]], "allocatedBytes": median(r["allocatedBytes"] for r in warm)}
    for suffix, reference in (("twenty", root / "build/turn-regression-20261025/jurisdiction/after-twenty"),
                              ("round20", root / "build/turn-regression-20261025/jurisdiction/after-round20")):
        if (folder / ("after-" + suffix) / "inputs.json").exists():
            baseline = rows(reference)
            for row, old in zip(rows(folder / ("after-" + suffix)), baseline, strict=True):
                for key in fields:
                    assert row[key] == old[key], (suffix, key)
            report[suffix] = {"errors": 0, "steps": sum(r["steps"] for r in baseline)}
    report["sourceManifests"] = {s: sha(folder / s / "source-manifest.json") for s in ("before", "after") if (folder / s / "source-manifest.json").exists()}
    (folder / "summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
