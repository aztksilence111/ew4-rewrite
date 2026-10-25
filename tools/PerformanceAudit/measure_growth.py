"""Compare six fixed consecutive turns, keeping timing, tracing and probes separate."""
import argparse
import json
import os
from pathlib import Path
from statistics import median
import subprocess
import time

from measure_stages import digest, load, source_index


def summarize(root, folder):
    stages = ("before", "after")
    manifests = {s: {f["path"]: f["sha256"] for f in load(folder / s / "source-manifest.json")["files"]} for s in stages}
    assert source_index(root) == manifests["after"], "Current source drifted"
    for stage in stages:
        assert manifests[stage] == {f["path"]: f["sha256"] for f in load(folder / f"{stage}-deep/source-manifest.json")["files"]}, "Probe source differs"
    identities, traces, content, timing, runtime = {}, {}, None, {}, None
    fields = ("initialSha256", "steps", "Round", "RandomState", "AiRandomState", "finalSha256", "stopReason")
    for stage in stages:
        rows = []
        for suffix in ("group0", "group1", "trace", "deep-results", "late"):
            path = folder / f"{stage}-{suffix}"
            inputs = load(path / "inputs.json")
            current = inputs["content"]
            identity_runtime = {k: inputs[k] for k in ("runtime", "platform", "architecture", "configuration")}
            if runtime is None:
                runtime = identity_runtime
            assert runtime == identity_runtime, "Measured runtime differs"
            if content is None:
                content = current
            assert current == content, "Measured content differs"
            observed = load(path / "results.json")
            for r in observed:
                key = r["initialHistory"]["Round"]
                identity = {f: r[f] for f in fields}
                identities.setdefault(key, identity)
                assert identities[key] == identity, f"Turn state/RNG differs: {stage}/{suffix}/{key}"
                if suffix == "trace":
                    assert r["mode"] == "ordinary" and r["sequenceSha256"]
                    traces.setdefault(key, r["sequenceSha256"])
                    assert traces[key] == r["sequenceSha256"], "Per-step world/notices differ"
            if suffix.startswith("group"):
                assert len(observed) == (12 if suffix == "group0" else 18)
                rows.extend(observed)
            elif suffix == "trace":
                assert len(observed) == 6
            elif suffix == "late":
                assert len(observed) == 3 and all(r["initialHistory"]["Round"] == 6 for r in observed)
        timing[stage] = []
        for round_index in range(6):
            same = [r for r in rows if r["roundIndex"] == round_index]
            warm = [r for r in same if not r["warmup"]]
            assert len(warm) == 3
            timing[stage].append({"round": round_index + 1, "samples": len(warm),
                "processFirstCurveMs": [r["elapsedMs"] for r in same if r["warmup"]],
                "medianMs": median(r["elapsedMs"] for r in warm),
                "rangeMs": [min(r["elapsedMs"] for r in warm), max(r["elapsedMs"] for r in warm)],
                "allocatedMiB": median(r["allocatedBytes"] / 1048576 for r in warm),
                "gc": [median(r["gc"][i] for r in warm) for i in range(3)],
                "initialHistory": warm[0]["initialHistory"], "finalHistory": warm[0]["finalHistory"],
                "steps": warm[0]["steps"]})
    assert len(identities) == len(traces) == 6
    assert all(digest(root / p) == h for p, h in content.items()), "Content drifted"
    reductions = [{"round": a["round"], "timePercent": (1 - b["medianMs"] / a["medianMs"]) * 100,
                   "allocationPercent": (1 - b["allocatedMiB"] / a["allocatedMiB"]) * 100}
                  for a, b in zip(timing["before"], timing["after"])]
    result = {"errors": 0, "sourceFiles": len(manifests["after"]), "contentFiles": len(content),
              "sourceChanges": sorted(p for p in manifests["before"].keys() | manifests["after"].keys()
                                      if manifests["before"].get(p) != manifests["after"].get(p)),
              "identities": identities, "stepSequences": traces, "timing": timing, "reductions": reductions, "runtime": runtime,
              "gcBoundary": {stage: "measured turn" if "gc = turnGc" in (folder / stage / "Harness/Program.cs").read_text(encoding="utf-8")
                             else "through final session encoding; do not compare GC counts across driver boundaries" for stage in stages},
              "scope": "Desktop Release, batch/delta, two serial process groups in reversed order, three warm curves per stage; Android pending"}
    (folder / "growth-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("errors", "sourceFiles", "contentFiles", "sourceChanges", "reductions")}))


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    root, folder = args.root.resolve(), args.folder.resolve()
    if root / "build" not in folder.parents:
        raise ValueError("Evidence must be under repository build/")
    if args.run:
        started = time.monotonic()
        environment = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_DELTA="1")
        environment.pop("EW4_AUDIT_START_SLOT", None)

        def run(stage, suffix, samples, *, deep=False, trace=False, late=False):
            destination = folder / f"{stage}-{suffix}"
            if (destination / "inputs.json").exists():
                return
            if destination.exists():
                raise RuntimeError(f"Incomplete output requires inspection before retry: {destination}")
            remaining = 6 * 3600 - (time.monotonic() - started)
            if remaining <= 0:
                raise RuntimeError("Six-hour run limit")
            env = dict(environment, EW4_AUDIT_SAMPLES=str(samples), EW4_AUDIT_ROUNDS="1" if late else "6",
                       EW4_AUDIT_MODES="ordinary" if trace else "batch", EW4_AUDIT_TRACE="1" if trace else "0",
                       EW4_AUDIT_RETAINED_MEMORY="1" if deep else "0")
            if late:
                env["EW4_AUDIT_START_SLOT"] = str(folder / "before-group0/conquest1-batch-0/round-6-start.json")
            binary = folder / (stage + ("-deep" if deep else "")) / "Harness/bin/Release/net10.0/Harness.dll"
            subprocess.run([args.dotnet, str(binary), str(root), str(destination)], env=env, cwd=root, check=True, timeout=remaining)

        for stage in ("before", "after"):
            run(stage, "group0", 2)
        for stage in ("after", "before"):
            run(stage, "group1", 3)
        for stage in ("before", "after"):
            run(stage, "trace", 1, trace=True)
            run(stage, "deep-results", 1, deep=True)
            run(stage, "late", 3, late=True)
    summarize(root, folder)


if __name__ == "__main__":
    main()
