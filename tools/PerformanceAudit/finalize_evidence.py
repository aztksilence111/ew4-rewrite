"""Verify a finished local round and fingerprint its checks, build inputs and APK."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

from measure_stages import load, source_index


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    root, folder = args.root.resolve(), args.folder.resolve()
    if root / "build" not in folder.parents:
        raise ValueError("Evidence must be under repository build/")
    summary = load(folder / "stage-summary.json")
    latest = {f["path"]: f["sha256"] for f in load(folder / "tactics/source-manifest.json")["files"]}
    assert latest == source_index(root), "Runtime source set/content changed"
    for path, expected in load(folder / "tactics-group1/inputs.json")["content"].items():
        assert hashlib.sha256((root / path).read_bytes()).hexdigest() == expected, path
    core = load(folder / "core-checks.log")
    client = load(folder / "client-checks.log")
    performance = load(folder / "performance-checks.log")
    for report in (core, client, performance):
        assert report["errors"] == 0
    mod = (folder / "mod-checks.log").read_text(encoding="utf-8-sig")
    match = re.search(r"Mod checks: (\d+), errors: 0; ([^\r\n]+)", mod)
    assert match, "Mod checks did not finish successfully"
    far, pixi = [load(folder / p / "results.json") for p in ("browser-far", "browser-pixi")]
    assert far["errors"] == far["missing"] == pixi["errors"] == []
    assert "failure" not in far and "failure" not in pixi
    package = load(folder / "android-package-final.json")
    apk = root / package["apk"]
    assert package["errors"] == 0 and hashlib.sha256(apk.read_bytes()).hexdigest() == package["sha256"]
    with zipfile.ZipFile(apk) as archive:
        assert {n.split('/')[1] for n in archive.namelist() if n.startswith('lib/')} == {"arm64-v8a"}
    signature = (folder / "apk-signature-final.log").read_text(encoding="utf-8-sig")
    assert "82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1" in signature
    assert "Verified using v2 scheme (APK Signature Scheme v2): true" in signature
    assert "Verified using v3 scheme (APK Signature Scheme v3): true" in signature
    paths = set()
    for project in ("Ew4.Verify", "Ew4.ModChecks", "Ew4.Android"):
        paths.update(p for p in (root / "rewrite" / project).rglob("*")
                     if p.is_file() and p.suffix in {".cs", ".csproj"}
                     and not {"bin", "obj"}.intersection(p.relative_to(root / "rewrite" / project).parts))
    paths.update(p for p in (root / "tools/PerformanceAudit").iterdir() if p.suffix in {".py", ".cs"})
    paths.update(root / p for p in ("tools/verify_mobile_feedback.cjs", "tools/verify_pixi_renderer.cjs",
                                  "tools/check_android_frontend_package.py", "tools/frontend/build.cjs",
                                  "tools/frontend/package-lock.json", "rewrite/Frontend.Build.targets"))
    paths.update((folder / "android-final-artifacts/obj").rglob("project.assets.json"))
    paths.update((folder / "preview/wwwroot").glob("*.js"))
    paths.update((folder / "preview/wwwroot").glob("*.css"))
    paths.add(apk)
    for stage in ("before", "search", "tactics"):
        paths.update((folder / stage).rglob("*.dll"))
        paths.update((folder / stage).glob("*-manifest.json"))
    for name in ("run-order.json", "android-final-build.log", "stage-summary.json", "core-checks.log",
                 "client-checks.log", "performance-checks.log", "mod-checks.log", "android-package-final.json",
                 "apk-signature-final.log", "preview/wwwroot/compat/manifest.json"):
        paths.add(folder / name)
    fingerprints = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(paths)}
    android_dotnet = Path.home() / ".codex/tools/dotnet-android/dotnet.exe"
    result = {"errors": 0, "runtimeSources": summary["sourceFiles"], "sourceDrift": 0,
              "contentFiles": summary["contentFiles"], "coreChecks": sum(v for v in core.values() if isinstance(v, int)),
              "clientChecks": client["clientChecks"], "navalChecks": client["navalDeploymentChecks"],
              "performanceChecks": performance["performanceContracts"], "modChecks": int(match[1]), "modRun": match[2],
              "farChecks": far["checks"], "pixiCases": len(pixi["cases"]),
              "apk": {k: package[k] for k in ("apk", "bytes", "sha256")}, "packageChecks": len(package["files"]),
              "sdk": {"desktop": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                      "android": subprocess.check_output([str(android_dotnet), "--version"], text=True).strip()},
              "androidProperties": {"Configuration": "Release", "RuntimeIdentifiers": "android-arm64", "RunAOTCompilation": False,
                                    "PublishTrimmed": False, "ApplicationVersion": 22, "ApplicationDisplayVersion": "0.2.19"},
              "fingerprints": fingerprints, "devicePerformance": "pending", "published": False}
    (folder / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "fingerprints"}))


if __name__ == "__main__":
    main()
