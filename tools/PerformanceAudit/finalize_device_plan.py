"""Audit completed device-plan evidence; never substitutes for executing tests."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--dotnet", required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root/"build/device-plan-20261024"
    read = lambda name: json.loads((folder/name).read_text(encoding="utf-8"))
    summary = read("summary.json")
    pair = read("android-pair.json")
    assert summary["errors"] == pair["errors"] == 0
    sources = []
    for project in (root/"rewrite").iterdir():
        if not project.is_dir() or not project.name.startswith("Ew4."):
            continue
        for path in project.rglob("*"):
            if path.is_file() and not {"bin","obj","compat","node_modules"}.intersection(path.relative_to(project).parts) and path.suffix in {".cs",".csproj",".js",".css",".html",".targets",".props"}:
                sources.append(path)
    sources.extend([root/"rewrite/Frontend.Build.targets",root/"tools/frontend/build.cjs"])
    fingerprint = hashlib.sha256()
    live_source_files = {}
    for path in sorted(sources):
        relative = path.relative_to(root).as_posix()
        fingerprint.update(relative.encode()); fingerprint.update(b"\0"); fingerprint.update(path.read_bytes()); fingerprint.update(b"\0")
        live_source_files[relative] = sha(path)
    build = read("android-pair.json")["variants"]["aot"]["source"]
    live_build = {"source":fingerprint.hexdigest(),"inputs":len(sources)}
    from measure_stages import source_index
    frozen = {f["path"]:f["sha256"] for f in read("combined/source-manifest.json")["files"]}
    measurement_root = folder/"frozen-inputs" if (folder/"frozen-inputs.json").exists() else root
    assert source_index(measurement_root) == frozen, "Frozen runtime source differs"
    live_runtime = source_index(root)
    live_drift = sorted(path for path in live_runtime.keys()|frozen.keys() if live_runtime.get(path) != frozen.get(path))
    if measurement_root == root:
        assert live_build == build and not live_drift, "Live source differs without an isolated snapshot"
    else:
        receipt = read("frozen-inputs.json")
        for path, digest in receipt["content"].items():
            assert sha(measurement_root/path) == digest, path
        with zipfile.ZipFile(root/pair["variants"]["jit"]["apk"]) as package:
            assert json.loads(package.read("assets/www/build-info.json")) == build
            for name in package.namelist():
                if name.startswith("assets/www/") and not name.endswith("/") and not name.startswith("assets/www/content/"):
                    assert package.read(name) == (measurement_root/"rewrite/Ew4.Web/wwwroot"/name[len("assets/www/"):]).read_bytes(),name
    for stage in ("baseline","combined"):
        assert {f["path"]:f["sha256"] for f in read(stage+"-deep/source-manifest.json")["files"]} == {f["path"]:f["sha256"] for f in read(stage+"/source-manifest.json")["files"]}
    for entry in pair["variants"].values():
        assert sha(root/entry["apk"]) == entry["sha256"] and entry["runtimeMarkerVerified"]
    previous_sign = (root/"build/menu-viewport-20261024/apk-signature.log").read_text(encoding="utf-8-sig")
    assert pair["variants"]["aot"]["signer"] in previous_sign, "Signer differs from previous test APK"
    core = json.loads((folder/"core-checks.log").read_text(encoding="utf-8-sig").strip())
    performance = json.loads((folder/"performance-final.log").read_text(encoding="utf-8-sig").strip())
    client = json.loads((folder/"client-final.log").read_text(encoding="utf-8-sig").strip())
    live_performance = json.loads((folder/"live-integration.log").read_text(encoding="utf-8-sig").strip().splitlines()[-1])
    assert core["errors"] == performance["errors"] == client["errors"] == 0
    assert live_performance["errors"] == 0 and live_performance["performanceContracts"] == performance["performanceContracts"]
    assert "Mod checks: 106, errors: 0" in (folder/"mod-checks.log").read_text(encoding="utf-8-sig")
    browser = read("browser-final/result.json")
    pixi = read("pixi-regression/results.json")
    native = json.loads((folder/"native-frontend.log").read_text(encoding="utf-8-sig").strip())
    chrome = json.loads((folder/"chrome70-final.log").read_text(encoding="utf-8-sig").splitlines()[-1])
    entry = read("entry/results.json")
    assert not browser["errors"] and not browser["missing"] and len(browser["cases"]) == 4
    assert not pixi["errors"] and native["errors"] == 0 and not chrome["errors"] and chrome["checks"] == 94
    assert len(entry["runs"]) == 36 and not entry["errors"] and len(entry["summary"]) == 6
    tests = {"core":sum(value for key,value in core.items() if isinstance(value,int) and key != "errors"),
        "performance":performance["performanceContracts"],"livePerformance":live_performance["performanceContracts"],"client":client["clientChecks"],"naval":client["navalDeploymentChecks"],"mod":106,
        "browser":browser["checks"],"chrome70":chrome["checks"],"nativeDrawing":native["checks"],"pixiCases":len(pixi["cases"]),"entryRuns":len(entry["runs"])}
    old = {f["path"]:f["sha256"] for f in read("baseline/source-manifest.json")["files"]}
    changed = sorted(path for path in old.keys()|frozen.keys() if old.get(path) != frozen.get(path))
    receipts = ["summary.json","android-pair.json","browser-final/result.json","pixi-regression/results.json","entry/results.json",
        "core-checks.log","performance-final.log","client-final.log","mod-checks.log","native-frontend.log","chrome70-final.log","web-final.log",
        "frozen-inputs.json","frozen-web-build.log","live-integration.log"]
    tools = [path for path in (root/"tools/PerformanceAudit").iterdir() if path.suffix in {".py",".cs",".cjs"}]
    result = {"errors":0,"runtimeSourceFiles":len(frozen),"sourceChanges":changed,"frozenRuntimeSources":frozen,
        "liveSourceFiles":live_source_files,"liveBuild":live_build,"liveRuntimeDrift":live_drift,"build":build,"tests":tests,
        "receipts":{path:sha(folder/path) for path in receipts},"tools":{path.relative_to(root).as_posix():sha(path) for path in tools},
        "sdk":subprocess.check_output([args.dotnet,"--version"],text=True).strip(),
        "scope":"Frozen performance implementation and verified package pair; concurrent worktree migration is reported separately. No phone timings, device memory/JS/GPU retention result, installation, publication or player-service update"}
    (folder/"verification.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    (folder/"SHA256SUMS.txt").write_text("".join(entry["sha256"]+"  "+Path(entry["apk"]).name+"\n" for entry in pair["variants"].values()),encoding="ascii")
    print(json.dumps({key:result[key] for key in ("errors","runtimeSourceFiles","sourceChanges","tests","sdk")}))


if __name__ == "__main__":
    main()
