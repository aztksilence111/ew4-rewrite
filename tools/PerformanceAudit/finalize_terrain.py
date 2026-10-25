"""Verify native-terrain regression evidence and the delivered APK without gameplay."""
import argparse
import hashlib
import json
from pathlib import Path

from measure_stages import PROJECTS, source_index as runtime_index
from package_test import source_index as package_index


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/terrain-visual-fix-20261025"
    load = lambda name: json.loads((folder / name).read_text(encoding="utf-8-sig"))
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    package = load("package/apk-verification.json")
    assert package["errors"] == 0
    assert load("package/build-inputs.json")["sources"] == package["sources"], "Package build inputs differ"
    current = package_index(root)
    drift = [{"path": path, "packaged": package["sources"].get(path), "current": current.get(path)}
             for path in sorted(current.keys() | package["sources"].keys())
             if current.get(path) != package["sources"].get(path)]
    assert sha(root / package["apk"]) == package["sha256"]
    runtime = json.loads((root / "build/turn-plateau-20261024/after/source-manifest.json").read_text())
    measured = {row["path"]: row["sha256"] for row in runtime["files"]}
    packaged_backend = {path.removeprefix("rewrite/"): value for path, value in package["sources"].items()
                        if path.startswith(tuple("rewrite/" + project + "/" for project in PROJECTS))}
    assert packaged_backend == measured, "Packaged backend differs from measured backend"
    live_backend = runtime_index(root)
    receipts = ["final-visual/results.json", "scaling/results.json", "pixi-final/results.json",
                "actual70/results.json", "model-loading/results.json", "native-drawing.json", "cleanup.json"]
    for name in receipts:
        assert load(name)["errors"] in (0, []), name
    assert len(load("final-visual/results.json")["cases"]) == 4
    for case in load("final-visual/results.json")["cases"]:
        assert case["interactionDifferences"] == case["movementDifferences"] == case["chunkPixels"] == 0
        assert case["preservedSizes"] and case["nativeTiles"] > 0 and not case["overview"]
        assert case["meanDifference"] < 4 and case["differingFraction"] < .05
    for case in load("scaling/results.json")["cases"]:
        assert {row["repeat"] for row in case["rows"]} == {1, 2, 4}
        for row in case["rows"]:
            if "meanDifference" in row:
                assert row["meanDifference"] < 4
            elif row["mode"] == "optimized":
                assert row["chunkPixels"] == 0
    assert load("cleanup.json")["state"] == "complete"
    assert load("actual70/results.json")["checks"] == 94
    assert load("native-drawing.json")["checks"] == 14308
    assert '"androidPackageChecks":13,"errors":0' in (folder / "package/managed-startup.log").read_text()
    assert set(package["requiredCoreSymbols"]) == {"MovementOwnership", "PlanningQueries", "DistancesForAdvance", "IsMovementProjection"}
    extracted = (folder / "package/package-runtime/Ew4.Core.dll").read_bytes()
    assert all(symbol.encode("ascii") in extracted for symbol in package["requiredCoreSymbols"])
    paths = [folder / name for name in receipts]
    paths += list((folder / "package").glob("*.json")) + list((folder / "package").glob("*.log"))
    paths += list((folder / "before").glob("*.js"))
    paths += [root / "tools/verify_terrain_visuals.cjs", root / "tools/verify_render_scaling.cjs", Path(__file__).resolve()]
    result = {"errors": 0, "packagedBackendDrift": 0,
              "workspaceFrontendDrift": sum(row["path"].startswith("rewrite/Ew4.Web/wwwroot/") for row in drift),
              "workspaceDrift": drift, "workspaceBackendMatches": live_backend == measured,
              "backendSources": len(runtime["files"]), "packageSources": len(package["sources"]),
              "apk": package["apk"], "sha256": package["sha256"], "bytes": package["bytes"],
              "scope": "Evidence applies to the verified APK build. Live workspace drift is listed separately; not physical phone frame certification.",
              "fingerprints": {path.relative_to(root).as_posix(): sha(path) for path in sorted(set(paths))}}
    (folder / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key not in {"fingerprints", "workspaceDrift"}}
                     | {"workspaceDriftCount": len(drift)}))


if __name__ == "__main__":
    main()
