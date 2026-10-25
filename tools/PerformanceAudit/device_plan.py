"""Freeze independent save/movement stages and compare serial fixed six-round runs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
from statistics import median
import subprocess
import sys
import time


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--run", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/device-plan-20261024"
    measurement_root = folder / "frozen-inputs" if (folder / "frozen-inputs.json").exists() else root
    tool = root / "tools/PerformanceAudit/prepare.py"
    started = time.monotonic()

    def execute(command, env=None):
        subprocess.run(command, cwd=root, env=env, check=True, timeout=max(1, 21600-(time.monotonic()-started)))

    if args.prepare:
        current = folder / "current-final"
        if not current.exists():
            execute([sys.executable, str(tool), str(root), str(current)])
        old = folder / "before"
        original = json.loads((old / "source-manifest.json").read_text())
        latest = json.loads((current / "source-manifest.json").read_text())
        for stage in ("baseline", "save", "movement", "combined"):
            source = folder / "stage-inputs" / stage
            selected = {item["path"]: (old, item) for item in original["files"]}
            for item in latest["files"]:
                path = item["path"]
                if stage == "combined" or stage == "save" and path.startswith("Ew4.Persistence/") or stage == "movement" and path.startswith("Ew4.Core/"):
                    selected[path] = (current, item)
            for path, (origin, item) in selected.items():
                target = source / "rewrite" / path
                target.parent.mkdir(parents=True, exist_ok=True)
                # prepare.py emits platform newlines; recover only a byte variant
                # authenticated by its original input digest.
                text = (origin / path).read_text(encoding="utf-8-sig")
                candidates = [text.encode(), text.replace("\n", "\r\n").encode()]
                candidates += [b"\xef\xbb\xbf" + value for value in candidates]
                raw = next((value for value in candidates if hashlib.sha256(value).hexdigest() == item["sha256"]), None)
                if raw is None:
                    raise RuntimeError("Snapshot differs beyond newline/BOM normalization: " + path)
                target.write_bytes(raw)
            names = [stage] + ([stage + "-deep"] if stage in ("baseline", "combined") else [])
            for name in names:
                destination = folder / name
                manifest_path = destination / "source-manifest.json"
                recorded = {f["path"]: f["sha256"] for f in json.loads(manifest_path.read_text())["files"]} if manifest_path.exists() else {}
                expected = {path: item["sha256"] for path, (_, item) in selected.items()}
                if recorded != expected:
                    execute([sys.executable, str(tool), str(root), str(destination), "--source-root", str(source)] + (["--instrument"] if name.endswith("-deep") else []))
                execute([args.dotnet, "build", str(destination / "Harness"), "-c", "Release", "-v", "quiet"])

    if args.run:
        env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_DELTA="1", EW4_AUDIT_MODES="batch")
        for key in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION"):
            env.pop(key, None)

        def run(stage, suffix, samples=2, rounds=6, **options):
            output = folder / (stage + "-" + suffix)
            if (output / "inputs.json").exists():
                return
            if output.exists():
                raise RuntimeError("Inspect incomplete output before retry: " + str(output))
            settings = dict(env, EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS=str(rounds), EW4_AUDIT_TRACE="0")
            settings.update(options)
            execute([args.dotnet, str(folder / stage / "Harness/bin/Release/net10.0/Harness.dll"), str(measurement_root), str(output)], settings)

        for stage in ("baseline", "save", "movement", "combined"):
            run(stage, "group0")
        for stage in ("combined", "movement", "save", "baseline"):
            run(stage, "group1", samples=3)
        for stage in ("baseline", "combined"):
            run(stage, "trace", samples=1, EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
            run(stage + "-deep", "results", samples=1, EW4_AUDIT_RETAINED_MEMORY="1")
            run(stage, "late", samples=3, rounds=1, EW4_AUDIT_START_SLOT=str(folder / "baseline-group0/conquest1-batch-0/round-6-start.json"))
            run(stage, "conquest4-trace", samples=1, rounds=1, EW4_AUDIT_SCENARIOS="conquest4", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")
        run("combined", "dual", samples=1, EW4_AUDIT_DUAL_VALIDATION="1", EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1")

    if not (folder / "baseline-group0/results.json").exists():
        return
    identities = {}
    traces = {}
    summaries = {}
    content = {}
    from measure_stages import source_index
    final_sources = {f["path"]: f["sha256"] for f in json.loads((folder / "combined/source-manifest.json").read_text())["files"]}
    assert source_index(measurement_root) == final_sources, "Measured runtime source drift"
    live = source_index(root)
    live_drift = sorted(path for path in live.keys() | final_sources.keys() if live.get(path) != final_sources.get(path))
    for stage in ("baseline", "save", "movement", "combined"):
        values = []
        for group in (0, 1):
            reports = json.loads((folder / f"{stage}-group{group}/results.json").read_text())
            for report in reports:
                index = report["roundIndex"]
                identity = (report["initialSha256"], report["finalSha256"], report["RandomState"], report["AiRandomState"], report["stopReason"], report["steps"])
                assert identities.setdefault(index, identity) == identity, (stage, index)
            values.extend(reports)
            inputs = json.loads((folder / f"{stage}-group{group}/inputs.json").read_text())
            for path, digest in inputs["content"].items():
                assert content.setdefault(path, digest) == digest
        summaries[stage] = []
        for index in range(6):
            warm = [r for r in values if r["roundIndex"] == index and not r["warmup"]]
            assert len(warm) == 3
            summaries[stage].append({"round": index+1, "medianMs": median(r["elapsedMs"] for r in warm),
                "allocatedMiB": median(r["allocatedBytes"]/1048576 for r in warm), "steps": warm[0]["steps"],
                "gcMedian":[median(r["gc"][generation] for r in warm) for generation in range(3)]})
    for stage in ("baseline", "combined", "combined-dual"):
        source = folder / (stage + "-trace" if stage != "combined-dual" else stage)
        for report in json.loads((source / "results.json").read_text()):
            index = report["roundIndex"]
            assert traces.setdefault(index, report["sequenceSha256"]) == report["sequenceSha256"]
    for stage in ("baseline", "combined"):
        for suffix in ("trace", "deep-results", "late"):
            for report in json.loads((folder / (stage+"-"+suffix) / "results.json").read_text()):
                index = 5 if suffix == "late" else report["roundIndex"]
                identity = (report["initialSha256"], report["finalSha256"], report["RandomState"], report["AiRandomState"], report["stopReason"], report["steps"])
                assert identities[index] == identity, (stage, suffix, index)
    cross = []
    for stage in ("baseline", "combined"):
        reports = json.loads((folder / (stage+"-conquest4-trace") / "results.json").read_text())
        cross.append([(r["initialSha256"],r["finalSha256"],r["sequenceSha256"],r["steps"],r["RandomState"],r["AiRandomState"],r["stopReason"]) for r in reports])
    assert cross[0] == cross[1], "Conquest4 trace differs"
    assert all(sha(measurement_root / path) == digest for path, digest in content.items())
    diagnostics = {}
    late = {}
    for stage in ("baseline", "combined"):
        diagnostics[stage] = []
        for report in json.loads((folder/(stage+"-deep-results/results.json")).read_text()):
            counts = report["counters"]
            controlled = counts.get("candidate.controlledMovement", 0)
            diagnostics[stage].append({"round":report["roundIndex"]+1,"worldValidation":report["worldValidation"],
                "controlledMovements":controlled,"controlledOwnerScanVisits":controlled*report["cells"],
                "candidateFullValidations":counts.get("validation.candidate.full",0),
                "distance":{key:value for key,value in counts.items() if key.startswith("distance.") or key.startswith("cache.")},
                "searchVisits":counts.get("search.visit",0),"retainedManagedBytes":report["retainedManagedBytes"],
                "gc":report["gc"],"requests":report["requests"],"responseBytes":report["responseBytes"],
                "workload":{m["Name"]:{key:m[key] for key in ("Calls","InclusiveBytes")} for m in report["metrics"]
                    if m["Name"] in ("Save.flush","WorldState.Copy","SessionSave.Encode")}})
        reports = json.loads((folder/(stage+"-late/results.json")).read_text())
        late[stage] = {"coldProcessMs":reports[0]["elapsedMs"],
            "warmSamples":sum(not r["warmup"] for r in reports),
            "warmMedianMs":median(r["elapsedMs"] for r in reports if not r["warmup"]),
            "allocatedMiB":median(r["allocatedBytes"]/1048576 for r in reports)}
    for before, after in zip(diagnostics["baseline"], diagnostics["combined"]):
        assert before["searchVisits"] == after["searchVisits"]
        for operation in ("Save.flush","WorldState.Copy","SessionSave.Encode"):
            assert before["workload"][operation]["Calls"] == after["workload"][operation]["Calls"]
    result = {"errors": 0, "timing": summaries, "identities": identities, "traces": traces, "contentFiles": len(content),
        "conquest4": cross[0], "runtimeSourceFiles": len(final_sources), "measurementRoot": str(measurement_root), "liveRuntimeDrift": live_drift,
        "diagnostics":diagnostics,"late":late,
        "diagnosticScope":"Full-validator visits exclude the controlled ownership scan, listed separately. Retained memory follows forced GC in diagnostic copies; not device/JS/GPU memory.",
        "scope": "Desktop Release; serial reversed process groups, three warm samples per round; ablations independent; device timings pending"}
    (folder / "summary.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"errors":0,"timing":summaries}))


if __name__ == "__main__":
    main()
