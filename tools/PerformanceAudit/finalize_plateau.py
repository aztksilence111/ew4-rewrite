"""Verify completed plateau evidence and package/source identity, without gameplay."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

from measure_stages import source_index as runtime_index
from package_test import source_index as package_index


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-plateau-20261024"
    load = lambda name: json.loads((folder / name).read_text(encoding="utf-8-sig"))
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    package = load("package/apk-verification.json")
    assert package["errors"] == 0 and package_index(root) == package["sources"], "Package source drift"
    runtime = {f["path"]: f["sha256"] for f in load("after/source-manifest.json")["files"]}
    assert runtime_index(root) == runtime, "Measured runtime drift"
    for path, value in load("after-group1/inputs.json")["content"].items():
        assert sha(root / path) == value, path
    apk = root / package["apk"]
    assert sha(apk) == package["sha256"]
    with zipfile.ZipFile(apk) as archive:
        assert {name.split("/")[1] for name in archive.namelist() if name.startswith("lib/")} == {"arm64-v8a"}
    signature = (folder / "package/signature.log").read_text(encoding="utf-8-sig")
    assert "82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1" in signature
    for scheme in (2, 3):
        assert f"Verified using v{scheme} scheme (APK Signature Scheme v{scheme}): true" in signature
    assert '"androidPackageChecks":13,"errors":0' in (folder / "package/managed-startup.log").read_text()
    receipts = ["growth-summary.json", "followup.json", "long-summary.json", "core-checks.json", "core-checks-final.json", "mod-checks.json",
        "performance-checks.log", "client-checks.log", "pixi-motion/results.json", "actual70-motion/results.json",
        "lifecycle-final/result.json", "model-loading/results.json", "render-scaling-release/results.json",
        "progressive-motion/results.json", "render-equivalence-release/results.json", "cleanup-initial.json", "cleanup.json"]
    for name in receipts:
        value = load(name)
        assert value["errors"] in (0, []), name
        assert not value.get("failure") and not value.get("missing"), name
    assert load("cleanup.json")["state"] == "complete"
    assert load("long-summary.json")["rounds"] == 20
    extracted = (folder / "package/package-runtime/Ew4.Core.dll").read_bytes()
    for symbol in package["requiredCoreSymbols"]:
        assert symbol.encode("ascii") in extracted
    for case in load("render-scaling-release/results.json")["cases"]:
        assert all(row["differingPixels"] == 0 for row in case["rows"] if "differingPixels" in row)
    for case in load("progressive-motion/results.json")["cases"]:
        assert case["refined"]["previews"] == 0
        assert case["refined"]["differingPixels"] / case["refined"]["pixels"] < .002
        assert all(row["builds"] == row["previews"] for row in case["cold"])
        assert all(row["pixels"] == 0 for row in case["cold"])
        assert any(row["relative"] == "movement" and row["previews"] > 0 for row in case["cold"])
    paths = {folder / name for name in receipts}
    paths.update(folder / "package" / name for name in ("apk-verification.json", "build-inputs.json", "package-content.json",
        "signature.log", "alignment.log", "managed-startup.log", "extracted-assemblies.json", "android-build.log"))
    paths.add(apk)
    for stage in ("before", "search", "ownership", "queries", "after", "before-deep", "after-deep", "cause"):
        paths.update((folder / stage).glob("*-manifest.json"))
        paths.add(folder / stage / "Harness/bin/Release/net10.0/Harness.dll")
    for name in ("before-long", "after-long", "before-deep-long", "after-deep-long", "cause-long"):
        paths.update((folder / name).glob("*.json"))
    paths.update(p for p in (root / "tools/PerformanceAudit").iterdir() if p.suffix in {".py", ".cs", ".ps1"})
    paths.update(root / "tools" / name for name in ("verify_render_scaling.cjs", "verify_model_loading.cjs",
        "verify_progressive_terrain.cjs", "verify_plateau_render.cjs", "test_frontend_native.cjs", "verify_pixi_renderer.cjs"))
    result = {"errors": 0, "sourceDrift": 0, "runtimeSources": len(runtime), "packageSources": len(package["sources"]),
        "contentFiles": len(load("after-group1/inputs.json")["content"]), "apk": package["apk"], "sha256": package["sha256"],
        "checks": {"core": sum(v for v in load("core-checks-final.json").values() if isinstance(v, int)),
            "performance": load("performance-checks.log")["performanceContracts"], "mod": load("mod-checks.json")["checks"],
            "client": load("client-checks.log")["clientChecks"], "naval": load("client-checks.log")["navalDeploymentChecks"],
            "chrome70": load("actual70-motion/results.json")["checks"], "lifecycle": load("lifecycle-final/result.json")["checks"]},
        "fingerprints": {p.relative_to(root).as_posix(): sha(p) for p in sorted(paths)},
        "scope": "Read-only evidence verification. Desktop timings do not certify physical Android or arbitrary future map/model performance."}
    (folder / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "fingerprints"}))


if __name__ == "__main__":
    main()
