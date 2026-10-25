"""Run isolated configured-task, active Lua, and East Asia correctness/performance probes."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time


def main():
    root = Path(sys.argv[1]).resolve()
    folder = root / "build/turn-regression-followup-20261025"
    frozen = root / "build/turn-regression-20261025/content"
    began = time.monotonic()

    def execute(command, log, env=None):
        with log.open("w", encoding="utf-8") as stream:
            subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT,
                           check=True, timeout=max(1, 21600 - (time.monotonic() - began)))

    def write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    inputs = json.loads((folder / "before-group0/inputs.json").read_text())["content"]
    for branch in ("tasks", "script", "eastasia"):
        content = folder / (branch + "-content")
        if not content.exists():
            for name in inputs:
                target = content / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(frozen / name, target)
            for i in range(1, 7):
                shutil.copy2(frozen / f"rewrite/data/original/conquest{i}.btl", content / f"rewrite/data/original/conquest{i}.btl")
            data = content / "rewrite/data"
            manifest = json.loads((data / "rewrite/extension.json").read_text())
            if branch == "tasks":
                manifest["metagame"] = "rewrite/audit-tasks.json"
                write(data / manifest["metagame"], {"schema": 1, "objectives": [
                    {"id": "audit.capture", "kind": "task", "name": "Capture", "description": "Fixture", "mode": 2,
                     "conditions": [{"type": "citiesCaptured", "count": 2}], "reward": {"medals": 10}},
                    {"id": "audit.kills", "kind": "achievement", "name": "Kills", "description": "Fixture",
                     "conditions": [{"type": "unitsDestroyed", "count": 5, "scope": "lifetime"}], "reward": {"badges": 1}}]})
            elif branch == "script":
                manifest.update(skills=["rewrite/audit-skills.json"], unitBindings="rewrite/audit-bindings.json",
                                scripts=["rewrite/audit.lua"])
                write(data / "rewrite/audit-skills.json", [{"id": "audit.supply", "effects": [
                    {"event": "turn.start", "operation": "heal", "script": "rewrite/audit.lua", "function": "supply"}]}])
                profiles = json.loads((data / "legacy/units.json").read_text())
                # Bind every runtime profile so the callback is exercised by real turns.
                def ids(value):
                    if isinstance(value, dict):
                        if isinstance(value.get("id"), str) and value["id"].startswith("ew4.unit."):
                            yield value["id"]
                        for child in value.values():
                            yield from ids(child)
                    elif isinstance(value, list):
                        for child in value:
                            yield from ids(child)
                targets = sorted(set(ids(profiles)))
                assert targets, "No profile bindings"
                write(data / "rewrite/audit-bindings.json", [{"target": target, "mode": "append",
                    "skills": [{"id": "audit.supply", "instanceId": "audit.supply"}]} for target in targets])
                (data / "rewrite/audit.lua").write_text("function supply(ctx) return 1 end\n", encoding="ascii")
            else:
                for name in ("def_battlelist.xml", "conquest7.btl", "conquest7.names.xml"):
                    shutil.copy2(root / "rewrite/content/eastasia/original" / name, data / "original" / name)
            write(data / "rewrite/extension.json", manifest)
            write(content / "frozen-inputs.json", {p.relative_to(content).as_posix(): sha(p)
                  for p in content.rglob("*") if p.is_file()})
    for stage in ("before", "after"):
        target = folder / (stage + "-branch")
        if not (target / "Harness/bin/Release/net10.0/Harness.dll").exists():
            assert not target.exists(), "Inspect partial branch build"
            source = folder / "source-before" if stage == "before" else root
            execute([sys.executable, "tools/PerformanceAudit/prepare.py", str(root), str(target),
                     "--source-root", str(source), "--instrument"], folder / (stage + "-branch-prepare.log"))
            execute(["dotnet", "build", str(target / "Harness"), "-c", "Release", "-v", "quiet"], folder / (stage + "-branch-build.log"))
    report = {"errors": 0, "scope": "Diagnostic desktop branches; two samples (first/warm), not a stable timing distribution", "branches": {}}
    for branch in ("tasks", "script", "eastasia"):
        results = {}
        for stage in ("before", "after"):
            output = folder / (stage + "-" + branch)
            if not (output / "inputs.json").exists():
                assert not output.exists(), "Inspect incomplete branch run"
                env = dict(os.environ, EW4_AUDIT_SCENARIOS="conquest7" if branch == "eastasia" else "conquest1",
                           EW4_AUDIT_MODES="ordinary", EW4_AUDIT_TRACE="1", EW4_AUDIT_DELTA="1", EW4_AUDIT_SAMPLES="2", EW4_AUDIT_ROUNDS="1")
                for key in ("EW4_AUDIT_START_SLOT", "EW4_AUDIT_DUAL_VALIDATION", "EW4_AUDIT_RETAINED_MEMORY"):
                    env.pop(key, None)
                execute(["dotnet", str(folder / (stage + "-branch/Harness/bin/Release/net10.0/Harness.dll")),
                         str(folder / (branch + "-content")), str(output)], folder / (stage + "-" + branch + ".log"), env)
            results[stage] = json.loads((output / "results.json").read_text())
            receipt = json.loads((output / "inputs.json").read_text())
            expected = {p.replace("\\", "/"): digest for p, digest in
                        json.loads((folder / (branch + "-content/frozen-inputs.json")).read_text()).items()}
            assert all(expected.get(p) == digest for p, digest in receipt["content"].items())
        for old, new in zip(results["before"], results["after"], strict=True):
            for key in ("initialSha256", "finalSha256", "RandomState", "AiRandomState", "sequenceSha256", "steps", "stopReason"):
                assert old[key] == new[key], (branch, key)
        report["branches"][branch] = results
        write(folder / "branch-summary.json", report)
        print(json.dumps({"branch": branch, "errors": 0}), flush=True)


if __name__ == "__main__":
    main()
