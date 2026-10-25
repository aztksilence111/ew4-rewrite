"""Verify saved evidence and the measured-source to packaged-source correspondence."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
folder = root / "build/turn-regression-followup-20261025"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


summary = read(folder / "summary.json")
assert summary["errors"] == 0 and summary["twenty"]["steps"] == 6135
checks = read(folder / "checks/verification.json")
assert len(checks) == 10 and all(c["exitCode"] == 0 and c["result"]["errors"] == 0 for c in checks.values())
branches = read(folder / "branch-summary.json")
assert branches["errors"] == 0 and set(branches["branches"]) == {"tasks", "script", "eastasia"}
script_calls = [m["Calls"] for m in branches["branches"]["script"]["after"][1]["metrics"] if m["Name"] == "ExtensionScriptRuntime.Execute"]
assert script_calls and script_calls[0] > 0
browser = read(folder / "browser-final/verification.json")
ordinary = read(folder / "ordinary/verification.json")
views = read(folder / "map-views/verification.json")
pixi = read(folder / "pixi/results.json")
pause = read(folder / "pause-final/verification.json")
for report in (browser, ordinary, views, pause):
    assert not report["errors"] and not report["missing"] and not report.get("failure")
assert len(browser["cases"]) == 6 and len(ordinary["ordinary"]) == 2 and not pixi["errors"]
assert all(c["turn"]["record"]["status"] == "returned" and not c["turn"]["counts"].get("pause-objectives") for c in browser["cases"])
assert all(c["matchesFull"] for c in ordinary["ordinary"])
package = read(folder / "package/apk-verification.json")
assert package["errors"] == 0 and sha(root / package["apk"]) == package["sha256"]
manifests = {s: {f["path"]: f["sha256"] for f in read(folder / s / "source-manifest.json")["files"]} for s in ("before", "after")}
changed = sorted(p for p in manifests["before"] if manifests["before"][p] != manifests["after"][p])
assert changed == ["Ew4.Client/ClientGame.cs", "Ew4.Client/ClientSaves.cs", "Ew4.Client/ClientSettlement.cs"], changed
for path, digest in manifests["after"].items():
    assert sha(root / "rewrite" / path) == digest and package["sources"]["rewrite/" + path] == digest, path
for name in ("map-views.js", "battlefield.js"):
    live = root / "rewrite/Ew4.Web/wwwroot" / name
    assert sha(live) == sha(folder / "web/wwwroot" / name)
    assert sha(live) == package["sources"]["rewrite/Ew4.Web/wwwroot/" + name]
for name in ("Ew4.Client", "Ew4.Core", "Ew4.Legacy"):
    raw = (folder / "package/package-runtime" / (name + ".dll")).read_bytes()
    assert b"PerfProbe" not in raw, "Packaged diagnostic probes"
assert b"RequestBattle" in (folder / "package/package-runtime/Ew4.Client.dll").read_bytes()
consumed = read(folder / "after-group0/inputs.json")["content"]
for name, digest in consumed.items():
    assert sha(root / name) == digest, name
diagnostics = {s: read(folder / (s + "-diagnostic/results.json"))[0] for s in ("before", "after")}
metrics = {s: {m["Name"]: m for m in r["metrics"]} for s, r in diagnostics.items()}
assert metrics["before"]["Save.flush"]["Calls"] == metrics["after"]["Save.flush"]["Calls"] == 19
report = {"errors": 0, "runtimeSourcesMatched": len(manifests["after"]), "consumedContentMatched": len(consumed),
          "changedRuntimeSources": changed, "timings": summary["timings"], "checks": checks,
          "browserChecks": {"final": browser["checks"], "ordinary": ordinary["checks"], "views": views["checks"], "pause": pause["checks"]},
          "scriptCalls": script_calls[0], "twentySteps": summary["twenty"]["steps"],
          "battleParsing": {s: metrics[s]["ClientContent.Battle"] for s in metrics},
          "package": {k: package[k] for k in ("apk", "sha256", "bytes", "build", "version", "versionCode", "contentFiles", "packageRecords")},
          "scope": "Local current-worktree implementation and validation. Historical phone-tested APK hashes remain unconfirmed; no attached physical device or phone timing."}
evidence = [folder / name for name in ("summary.json", "branch-summary.json", "checks/verification.json",
    "browser-final/verification.json", "ordinary/verification.json", "map-views/verification.json", "pixi/results.json",
    "pause-final/verification.json", "package/apk-verification.json")]
report["evidenceSha256"] = {p.relative_to(root).as_posix(): sha(p) for p in evidence}
cleanup_path = folder / "cleanup.json"
if cleanup_path.exists():
    cleanup = read(cleanup_path)
    assert cleanup["stage"] == "complete" and cleanup["errors"] == 0
    assert cleanup["bytes"] == sum(f["bytes"] for f in cleanup["files"])
    assert all(not Path(f["path"]).exists() for f in cleanup["files"])
    report["cleanup"] = {k: cleanup[k] for k in ("count", "bytes", "errors")}
    report["evidenceSha256"][cleanup_path.relative_to(root).as_posix()] = sha(cleanup_path)
(folder / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: report[k] for k in ("errors", "runtimeSourcesMatched", "consumedContentMatched", "twentySteps", "browserChecks", "package")}))
