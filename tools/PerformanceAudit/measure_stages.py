"""Serial, alternating stage measurements with fixed input and separate traces."""
import argparse
import hashlib
import json
import os
from pathlib import Path
from statistics import mean, median
import subprocess
import time


PROJECTS = ("Ew4.Core", "Ew4.Persistence", "Ew4.Legacy", "Ew4.Client")


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_index(root):
    return {f"{project}/{path.relative_to(root / 'rewrite' / project).as_posix()}": digest(path)
            for project in PROJECTS for path in (root / "rewrite" / project).rglob("*")
            if path.is_file() and path.suffix in {".cs", ".csproj"}
            and not {"bin", "obj"}.intersection(path.relative_to(root / "rewrite" / project).parts)}


def summarize(root, folder, stages):
    manifests = {s: {f["path"]: f["sha256"] for f in load(folder / s / "source-manifest.json")["files"]} for s in stages}
    assert source_index(root) == manifests[stages[-1]], "Current source set/content drifted"
    differences = {}
    for previous, stage in zip(stages, stages[1:]):
        a, b = manifests[previous], manifests[stage]
        differences[stage] = sorted(p for p in a.keys() | b.keys() if a.get(p) != b.get(p))
    for stage in stages:
        assert manifests[stage] == {f["path"]: f["sha256"] for f in load(folder / (stage + "-deep") / "source-manifest.json")["files"]}
    identities, sequences, inputs, plain, deep = {}, {}, None, {}, {}
    identity_fields = ("initialSha256", "steps", "Round", "RandomState", "AiRandomState", "finalSha256", "stopReason")
    for stage in stages:
        rows = []
        for group in range(2):
            rows.extend(load(folder / f"{stage}-group{group}" / "results.json"))
        plain[stage] = {}
        diagnostics = load(folder / f"{stage}-deep-final" / "results.json")
        traces = load(folder / f"{stage}-trace" / "results.json")
        deep[stage] = diagnostics
        for scenario in ("conquest1", "conquest4"):
            observed = [r for r in rows if r["scenario"] == scenario]
            warm = [r for r in observed if not r["warmup"]]
            assert len(warm) >= 6
            plain[stage][scenario] = {
                "firstMs": [r["elapsedMs"] for r in observed if r["warmup"]],
                "warmSamples": len(warm), "warmMedianMs": median(r["elapsedMs"] for r in warm),
                "warmRangeMs": [min(r["elapsedMs"] for r in warm), max(r["elapsedMs"] for r in warm)],
                "warmAllocatedMiB": median(r["allocatedBytes"] / 1048576 for r in warm),
                "warmGc": [median(r["gc"][i] for r in warm) for i in range(3)],
                "requests": [r["requests"] for r in warm], "maxRequestMs": max(r["requestMaxMs"] for r in warm)}
        for row in rows + diagnostics + traces:
            scenario = row["scenario"]
            identity = {k: row[k] for k in identity_fields}
            identities.setdefault(scenario, identity)
            assert identities[scenario] == identity, f"State/RNG/stop changed: {stage}/{scenario}"
            if row["sequenceSha256"] is not None:
                assert row["mode"] == "ordinary"
                sequences.setdefault(scenario, row["sequenceSha256"])
                assert sequences[scenario] == row["sequenceSha256"], "Step sequence changed"
            else:
                assert row["mode"] == "batch"
        for name in [f"{stage}-group0", f"{stage}-group1", f"{stage}-trace", f"{stage}-deep-final"]:
            current = load(folder / name / "inputs.json")["content"]
            if inputs is None:
                inputs = current
            assert inputs == current, "Content set/content changed between stages"
    assert all(digest(root / p) == h for p, h in inputs.items()), "Measured content drifted"
    comparison = {}
    for stage in stages[1:]:
        comparison[stage] = {scenario: {field: (1 - plain[stage][scenario][field] / plain[stages[0]][scenario][field]) * 100
            for field in ("warmMedianMs", "warmAllocatedMiB")} for scenario in ("conquest1", "conquest4")}
    result = {"errors": 0, "sourceChanges": differences, "sourceFiles": len(manifests[stages[-1]]),
              "contentFiles": len(inputs), "identities": identities, "stepSequences": sequences,
              "plain": plain, "reductionPercentFromBefore": comparison, "diagnostics": deep,
              "scope": "Desktop Release, six warm samples per scenario/stage, two process groups in alternating order; Android pending"}
    (folder / "stage-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k not in {"diagnostics", "identities", "stepSequences"}}))


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--stages", default="before,search,tactics")
    parser.add_argument("--run", action="store_true")
    args = parser.parse_args()
    root, folder = args.root.resolve(), args.folder.resolve()
    if root / "build" not in folder.parents:
        raise ValueError("Evidence must be under repository build/")
    stages = args.stages.split(",")
    if args.run:
        started = time.monotonic()
        order = []
        environment = dict(os.environ, EW4_AUDIT_DELTA="1", EW4_AUDIT_SCENARIOS="conquest1,conquest4")
        def run(stage, output, modes, samples, trace=False, deep=False):
            if time.monotonic() - started > 6 * 3600:
                raise RuntimeError("Six-hour run limit")
            env = dict(environment, EW4_AUDIT_MODES=modes, EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_TRACE="1" if trace else "0")
            binary = folder / (stage + ("-deep" if deep else "")) / "Harness/bin/Release/net10.0/Harness.dll"
            command = ["dotnet", str(binary), str(root), str(folder / output)]
            order.append({"stage": stage, "output": output, "command": command})
            (folder / "run-order.json").write_text(json.dumps(order, indent=2), encoding="utf-8")
            subprocess.run(command, env=env, cwd=root, check=True, timeout=6 * 3600 - (time.monotonic() - started))
        for group in range(2):
            for stage in stages if group == 0 else reversed(stages):
                run(stage, f"{stage}-group{group}", "batch", 4)
        for stage in stages:
            run(stage, f"{stage}-trace", "ordinary", 1, trace=True)
            run(stage, f"{stage}-deep-final", "batch", 2, deep=True)
    summarize(root, folder, stages)


if __name__ == "__main__":
    main()
