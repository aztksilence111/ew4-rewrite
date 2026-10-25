"""Freeze current content and run serial P0 ablations without changing live sources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
from statistics import median
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--smoke", action="store_true")
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--extended", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-regression-20261025"
    stages = ("baseline", "runtime", "dispatch", "combined")
    began = time.monotonic()

    def execute(command, log, env=None):
        log.parent.mkdir(parents=True, exist_ok=True)
        with log.open("w", encoding="utf-8") as stream:
            result = subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT,
                timeout=max(1, 21600 - (time.monotonic() - began)))
        if result.returncode:
            raise RuntimeError(f"Exit {result.returncode}: inspect {log}")

    def write(path, value):
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def digest(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def restore(origin, entry):
        text = (origin / entry["path"]).read_text(encoding="utf-8-sig")
        variants = [text.encode(), text.replace("\n", "\r\n").encode()]
        variants += [b"\xef\xbb\xbf" + raw for raw in variants]
        return next(raw for raw in variants if hashlib.sha256(raw).hexdigest() == entry["sha256"])

    if args.prepare:
        if not (folder / "baseline/source-manifest.json").exists():
            raise RuntimeError("Freeze baseline with prepare.py before editing runtime code")
        if (folder / "combined").exists():
            raise RuntimeError("Prepared inputs already exist; use a new evidence folder for changed sources")
        execute([sys.executable, str(root / "tools/PerformanceAudit/prepare.py"), str(root), str(folder / "combined")], folder / "freeze.log")
        manifests = {stage: json.loads((folder / stage / "source-manifest.json").read_text())["files"]
            for stage in ("baseline", "combined")}
        old, new = ({f["path"]: f["sha256"] for f in manifests[stage]} for stage in ("baseline", "combined"))
        changed = sorted(p for p in old.keys() | new.keys() if old.get(p) != new.get(p))
        expected = ["Ew4.Client/ClientGame.cs", "Ew4.Core/RuleExtensions.cs", "Ew4.Legacy/ExtensionLoader.cs"]
        if changed != expected:
            raise RuntimeError("Unexpected source drift: " + repr(changed))
        content = folder / "content/rewrite/data"
        shutil.copytree(root / "rewrite/data", content)
        inputs = [{"path": p.relative_to(folder / "content").as_posix(), "bytes": p.stat().st_size, "sha256": digest(p)}
            for p in sorted(content.rglob("*")) if p.is_file()]
        write(folder / "content-manifest.json", {"files": inputs, "changedSources": changed,
            "scope": "Current working tree C; not identified with either user-timed APK O or N",
            "gitHead": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()})
        for stage in stages:
            source = folder / ("source-" + stage)
            for entry in manifests["baseline"]:
                path = entry["path"]
                use_new = stage == "combined" or stage == "runtime" and path.endswith("/ExtensionLoader.cs") \
                    or stage == "dispatch" and path in expected[:2]
                origin = "combined" if use_new else "baseline"
                selected = next(f for f in manifests[origin] if f["path"] == path)
                target = source / "rewrite" / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(restore(folder / origin, selected))
            for suffix in ("", "-deep"):
                name = stage + suffix
                # baseline and combined already contain the uninstrumented source snapshot.
                if suffix or stage not in ("baseline", "combined"):
                    execute([sys.executable, str(root / "tools/PerformanceAudit/prepare.py"), str(root), str(folder / name),
                        "--source-root", str(source)] + (["--instrument"] if suffix else []), folder / (name + "-prepare.log"))
                execute(["dotnet", "build", str(folder / name / "Harness"), "-c", "Release", "-v", "quiet"], folder / (name + "-build.log"))
        print(json.dumps({"prepared": 8, "contentFiles": len(inputs), "sourceChanges": changed}), flush=True)

    def run(stage, label, samples=1, rounds=1, **settings):
        output = folder / label
        if (output / "inputs.json").exists():
            return
        if output.exists():
            raise RuntimeError("Inspect incomplete run before retry: " + str(output))
        env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_MODES="batch", EW4_AUDIT_DELTA="1",
            EW4_AUDIT_TRACE="0", EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS=str(rounds))
        for key in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION", "EW4_AUDIT_RETAINED_MEMORY"):
            env.pop(key, None)
        env.update(settings)
        execute(["dotnet", str(folder / stage / "Harness/bin/Release/net10.0/Harness.dll"),
            str(folder / "content"), str(output)], folder / (label + ".log"), env)
        print(json.dumps({"completed": label}), flush=True)

    if args.smoke:
        for stage in stages:
            run(stage + "-deep", stage + "-diagnostic")
    if args.run:
        for group, order in enumerate((stages, tuple(reversed(stages)))):
            for stage in order:
                run(stage, f"{stage}-group{group}", samples=4)
        for stage in ("baseline", "combined"):
            run(stage, stage + "-six", rounds=6)
            run(stage, stage + "-trace", rounds=6, EW4_AUDIT_TRACE="1", EW4_AUDIT_MODES="ordinary")
            run(stage, stage + "-conquest4", EW4_AUDIT_SCENARIOS="conquest4", EW4_AUDIT_TRACE="1", EW4_AUDIT_MODES="ordinary")
            run(stage, stage + "-late", samples=4,
                EW4_AUDIT_START_SLOT=str(folder / "baseline-six/conquest1-batch-0/round-6-start.json"))
        run("combined", "combined-dual", rounds=6, EW4_AUDIT_DUAL_VALIDATION="1", EW4_AUDIT_TRACE="1", EW4_AUDIT_MODES="ordinary")
    if args.extended:
        for stage in ("baseline", "combined"):
            run(stage, stage + "-twenty", rounds=20)
        run("combined", "combined-round20", EW4_AUDIT_START_SLOT=str(folder / "baseline-twenty/conquest1-batch-0/round-20-start.json"))

    def rows(label):
        return json.loads((folder / label / "results.json").read_text())

    fields = ("initialSha256", "finalSha256", "RandomState", "AiRandomState", "steps", "stopReason", "Round")

    def compare(labels, trace=False):
        reference = {r["roundIndex"]: r for r in rows(labels[0])}
        for label in labels[1:]:
            candidate = rows(label)
            assert set(r["roundIndex"] for r in candidate) == set(reference), label
            for row in candidate:
                expected = reference[row["roundIndex"]]
                for field in fields + (("sequenceSha256",) if trace else ()):
                    assert row[field] == expected[field], (label, row["roundIndex"], field)

    report = {"errors": 0, "scope": "Frozen current content; desktop full client RPC/save work, excludes browser frames and phone. Deep probe times excluded from absolute timings."}
    frozen_content = {f["path"]: f["sha256"] for f in json.loads((folder / "content-manifest.json").read_text())["files"]}
    consumed = None
    completed = sorted(folder.glob("*/inputs.json"))
    for path in completed:
        actual = json.loads(path.read_text())["content"]
        assert all(frozen_content.get(p) == value for p, value in actual.items()), path
        # Scenario-specific runs load different subsets of the same frozen files.
        if path.parent.name.endswith(("group0", "group1", "diagnostic")):
            if consumed is None:
                consumed = actual
            assert actual == consumed, path
    report["completedRuns"] = len(completed)
    report["identities"] = {s: {"sourcesSha256": digest(folder / s / "source-manifest.json"),
        "assemblies": {p.name: digest(p) for p in sorted((folder / s / "Harness/bin/Release/net10.0").glob("Ew4.*.dll"))}}
        for s in stages}
    if all((folder / (s + "-diagnostic/inputs.json")).exists() for s in stages):
        compare([s + "-diagnostic" for s in stages])
        report["diagnostics"] = {s: {k: rows(s + "-diagnostic")[0][k] for k in ("worldValidation", "counters", "metrics")} for s in stages}
    if all((folder / (s + "-group1/inputs.json")).exists() for s in stages):
        compare([f"{s}-group{g}" for s in stages for g in range(2)])
        report["timings"] = {}
        for stage in stages:
            all_rows = rows(stage + "-group0") + rows(stage + "-group1")
            warm = [r for r in all_rows if not r["warmup"]]
            times = [r["elapsedMs"] for r in warm]
            report["timings"][stage] = {"warmSamples": len(warm), "medianMs": median(times), "minMs": min(times), "maxMs": max(times),
                "coldMs": [r["elapsedMs"] for r in all_rows if r["warmup"]], "medianAllocatedBytes": median(r["allocatedBytes"] for r in warm)}
    for suffix in ("six", "trace", "conquest4", "late", "twenty"):
        labels = [s + "-" + suffix for s in ("baseline", "combined")]
        if all((folder / label / "inputs.json").exists() for label in labels):
            compare(labels, suffix in ("trace", "conquest4"))
            report[suffix] = {s: [{k: r[k] for k in ("roundIndex", "elapsedMs", "allocatedBytes", "steps", "stopReason")}
                for r in rows(s + "-" + suffix)] for s in ("baseline", "combined")}
    if (folder / "combined-dual/inputs.json").exists():
        compare(["baseline-trace", "combined-trace", "combined-dual"], True)
        compare(["baseline-six", "combined-trace"])
        report["dualValidationSteps"] = sum(r["steps"] for r in rows("combined-dual"))
    if (folder / "combined-round20/inputs.json").exists():
        expected = rows("baseline-twenty")[-1]
        actual = rows("combined-round20")[0]
        for field in ("finalSha256", "RandomState", "AiRandomState", "steps", "stopReason", "Round"):
            assert actual[field] == expected[field], ("round20 reload", field)
        report["round20Reload"] = {"errors": 0, "elapsedMs": actual["elapsedMs"], "steps": actual["steps"]}
    write(folder / "summary.json", report)
    print(json.dumps({k: v for k, v in report.items() if k not in ("identities", "diagnostics", "six", "trace", "twenty")}), flush=True)


if __name__ == "__main__":
    main()
