"""Verify and fingerprint the completed local consecutive-turn optimization batch."""
import argparse
import json
from pathlib import Path
import re

from measure_growth import summarize
from measure_stages import digest, load


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    root, folder = args.root.resolve(), args.folder.resolve()
    if root / "build" not in folder.parents:
        raise ValueError("Evidence must be under repository build/")
    summarize(root, folder)
    core = load(folder / "core-checks.json")
    performance = load(folder / "performance-checks.json")
    client = load(folder / "client-checks.log")
    assert core["errors"] == performance["errors"] == client["errors"] == 0
    mod = re.search(r"Mod checks: (\d+), errors: 0; ([^\r\n]+)", (folder / "mod-checks.log").read_text(encoding="utf-8-sig"))
    assert mod, "Mod checks did not finish successfully"
    for name in ("web-build.log", "android-compile-final.log"):
        text = (folder / name).read_text(encoding="utf-8-sig")
        assert "0 个错误" in text and "0 个警告" in text, "Build did not pass: " + name
    before, after = [load(folder / f"{stage}-deep-results/results.json") for stage in ("before", "after")]
    assert len(before) == len(after) == 6
    for a, b in zip(before, after):
        assert a["worldValidation"] == b["worldValidation"], "Full-validation workload changed"
        assert a["counters"] == b["counters"], "Search/candidate workload changed"
        for name in ("WorldState.Copy", "Save.flush", "HexMap.DistancesTo"):
            calls = [next(m["Calls"] for m in r["metrics"] if m["Name"] == name) for r in (a, b)]
            assert calls[0] == calls[1], "Operation count changed: " + name
    oracle = None
    for stage in ("before", "after"):
        rows = load(folder / f"{stage}-conquest4-trace/results.json")
        assert len(rows) == 1 and rows[0]["steps"] == 368 and rows[0]["mode"] == "ordinary"
        row = rows[0]
        identity = {k: row[k] for k in ("initialSha256", "sequenceSha256", "finalSha256", "steps", "RandomState", "AiRandomState", "stopReason")}
        assert identity["sequenceSha256"]
        if oracle is None:
            oracle = identity
        assert oracle == identity, "Conquest4 sequence differs"
        for path, expected in load(folder / f"{stage}-conquest4-trace/inputs.json")["content"].items():
            assert digest(root / path) == expected, "Conquest4 content drifted"
    paths = set()
    for project in ("Ew4.Core", "Ew4.Persistence", "Ew4.Legacy", "Ew4.Client", "Ew4.Verify", "Ew4.ModChecks"):
        paths.update(p for p in (root / "rewrite" / project).rglob("*") if p.is_file() and p.suffix in (".cs", ".csproj")
                     and not {"bin", "obj"}.intersection(p.relative_to(root / "rewrite" / project).parts))
    paths.update(p for p in Path(__file__).parent.iterdir() if p.suffix in (".py", ".cs"))
    paths.update(p for p in folder.rglob("*") if p.is_file() and p.name in
                 ("source-manifest.json", "driver-manifest.json", "inputs.json", "results.json"))
    for stage in ("before", "after", "before-deep", "after-deep"):
        paths.add(folder / stage / "Harness/Program.cs")
        paths.update((folder / stage / "Harness/bin/Release/net10.0").glob("*.dll"))
    paths.update(folder / name for name in ("core-checks.json", "performance-checks.json", "client-checks.log", "mod-checks.log",
                                          "web-build.log", "android-compile-final.log", "growth-summary.json"))
    result = {"errors": 0, "sourceDrift": 0, "coreChecks": sum(v for k, v in core.items() if k != "errors"),
              "performanceChecks": performance["performanceContracts"], "clientChecks": client,
              "modChecks": int(mod[1]), "conquest4": oracle, "webBuild": "passed", "androidCompile": "passed",
              "apkBuilt": False, "published": False, "devicePerformance": "pending",
              "fingerprints": {p.relative_to(root).as_posix(): digest(p) for p in sorted(paths)}}
    (folder / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "fingerprints"}))


if __name__ == "__main__":
    main()
