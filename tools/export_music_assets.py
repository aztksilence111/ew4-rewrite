"""Import the five original music tracks and update their runtime hashes."""
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]


def main():
    data = ROOT / "rewrite/data"
    configuration = data / "client/music.json"
    music = json.loads(configuration.read_text(encoding="utf-8"))
    tracks = sorted({music["menu"], music["defeat"], *music["battle"]})
    output = data / "client/music"
    output.mkdir(exist_ok=True)
    for name in tracks:
        source = ROOT / "apk_work/original/assets" / name
        target = output / name
        if target.exists() and target.read_bytes() != source.read_bytes():
            raise RuntimeError(f"Existing music differs: {target}")
        if not target.exists():
            shutil.copyfile(source, target)
    manifest_path = data / "runtime-content.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entries = {entry["path"]: entry for entry in manifest["files"]}
    for path in [configuration, *(output / name for name in tracks)]:
        content = path.read_bytes()
        key = path.relative_to(data).as_posix()
        entries[key] = dict(path=key, bytes=len(content), sha256=hashlib.sha256(content).hexdigest())
    manifest["files"] = list(entries.values())
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(dict(tracks=len(tracks), bytes=sum((output / name).stat().st_size for name in tracks), errors=0)))


if __name__ == "__main__":
    main()
