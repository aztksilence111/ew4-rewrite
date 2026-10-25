"""Measure a long fixed-world curve and reload selected worlds in fresh processes."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/turn-plateau-20261024"
    began = time.monotonic()

    def run(stage, suffix, rounds, samples, slot=None):
        output = folder / (stage + "-" + suffix)
        if (output / "inputs.json").exists():
            return
        if output.exists():
            raise RuntimeError("Inspect incomplete run: " + str(output))
        env = {k: v for k, v in os.environ.items() if not k.startswith("EW4_AUDIT_")}
        env.update(EW4_AUDIT_SCENARIOS="conquest1", EW4_AUDIT_MODES="batch", EW4_AUDIT_DELTA="1",
                   EW4_AUDIT_ROUNDS=str(rounds), EW4_AUDIT_SAMPLES=str(samples))
        if slot:
            env["EW4_AUDIT_START_SLOT"] = str(slot)
        subprocess.run([args.dotnet, str(folder / stage / "Harness/bin/Release/net10.0/Harness.dll"),
                        str(root), str(output)], cwd=root, env=env, check=True,
                       timeout=max(1, 21600 - (time.monotonic() - began)))

    for stage in ("before", "after"):
        run(stage, "long", 20, 2)
    for stage in ("before-deep", "after-deep"):
        run(stage, "long", 20, 1)
    for stage in ("after", "before"):
        for round_number in (1, 10, 20):
            slot = folder / "before-long/conquest1-batch-0" / f"round-{round_number}-start.json"
            run(stage, f"reload-{round_number}", 1, 3, slot)
    print("Long curve and fresh-process reloads complete")


if __name__ == "__main__":
    main()
