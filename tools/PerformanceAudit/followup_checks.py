"""Run focused client/extension checks and retain command receipts for this batch."""
import json
from pathlib import Path
import subprocess
import sys
import time

root = Path(sys.argv[1]).resolve()
folder = root / "build/turn-regression-followup-20261025/checks"
folder.mkdir(exist_ok=True)
began = time.monotonic()
report = {}
for mode in ("client", "settlement", "scenario", "local", "geography", "objectives", "pause-objectives", "provocations", "extension", "map-colors"):
    log = folder / (mode + ".log")
    if mode not in report:
        command = ["dotnet", "rewrite/Ew4.Verify/bin/Release/net10.0/Ew4.Verify.dll", str(root), "--" + mode + "-only"]
        result = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                timeout=max(1, 21600 - (time.monotonic() - began)))
        log.write_bytes(result.stdout)
        report[mode] = {"exitCode": result.returncode, "log": str(log.relative_to(root))}
        if result.returncode == 0:
            report[mode]["result"] = json.loads(result.stdout.decode("utf-8-sig").strip().splitlines()[-1])
        (folder / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        if result.returncode:
            raise SystemExit("Check failed: " + str(log))
        print(json.dumps({mode: report[mode]["result"]}), flush=True)
