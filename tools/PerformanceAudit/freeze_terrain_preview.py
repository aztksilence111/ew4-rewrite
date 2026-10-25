"""Extract verified test-package web assets into an isolated preview directory."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile


root = Path(__file__).resolve().parents[2]
folder = root / "build/terrain-visual-fix-20261025"
package = json.loads((folder / "package/apk-verification.json").read_text())
apk = root / package["apk"]
assert hashlib.sha256(apk.read_bytes()).hexdigest() == package["sha256"]
destination = folder / "preview-frontend/wwwroot"
assert not destination.exists(), "Do not overwrite an existing preview"
files = {}
with zipfile.ZipFile(apk) as archive:
    for entry in archive.infolist():
        if not entry.filename.startswith("assets/www/") or entry.is_dir():
            continue
        relative = PurePosixPath(entry.filename.removeprefix("assets/www/"))
        assert not relative.is_absolute() and ".." not in relative.parts
        target = destination.joinpath(*relative.parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        content = archive.read(entry)
        target.write_bytes(content)
        files[relative.as_posix()] = hashlib.sha256(content).hexdigest()
receipt = {"errors": 0, "apkSha256": package["sha256"], "files": files}
(folder / "preview-frontend/receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
print(json.dumps({"errors": 0, "files": len(files), "path": str(destination)}))
