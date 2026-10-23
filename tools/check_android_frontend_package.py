"""Verify the signed package contains the current shared frontend and caches."""
import hashlib
import json
import argparse
import struct
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("apk", nargs="?", default="build/ew4-rewrite-refinement-20261019-arm64.apk")
parser.add_argument("--report", default="rewrite/reports/frontend_refinement_20261019/android-package.json")
parser.add_argument("--managed", help="Raw assembly-store path from this build")
args = parser.parse_args()
APK = ROOT / args.apk
files = {}
with zipfile.ZipFile(APK) as archive:
    assert archive.testzip() is None, "APK CRC failure"
    for name in ("victory-ui.js", "victory-ui.css", "local-multiplayer.js", "local-multiplayer.css"):
        relative = "assets/www/" + name
        source = ROOT / "rewrite/Ew4.Web/wwwroot" / name
        assert archive.read(relative) == source.read_bytes(), relative + " is stale"
        files[relative] = hashlib.sha256(source.read_bytes()).hexdigest()
    for name in ("startup.js", "app.js", "index.html", "original-ui.js", "menu-art.js", "effects.js", "native-layout.js", "commander-workflows.js", "menus.js", "battlefield.js", "music.js", "original-ui.css", "style.css", "city-names.json", "disband.png", "unit-navigation.js", "unit-search.png", "mods.js", "mods.css", "skill-ui.js"):
        relative = "assets/www/" + name
        source = ROOT / "rewrite/Ew4.Web/wwwroot" / name
        assert archive.read(relative) == source.read_bytes(), relative + " is stale"
        files[relative] = hashlib.sha256(source.read_bytes()).hexdigest()
    for name in ("effects.json", "manifest.json", "images/font_city_hd.png", "images/font_city_labels_hd.png", "animations/anim_fire.json", "animations/anim_upgrade.json"):
        relative = "assets/www/content/" + name
        source = ROOT / "rewrite/data/client" / name
        assert archive.read(relative) == source.read_bytes(), relative + " is stale"
        files[relative] = hashlib.sha256(source.read_bytes()).hexdigest()
    assert "lib/arm64-v8a/libmonodroid.so" in archive.namelist(), "ARM64 runtime missing"
    coverage = json.loads(archive.read("assets/www/content/fonts/coverage.json"))
    if coverage.get("loading") == "subsets-v1":
        expected_fonts = {"assets/www/content/fonts/" + face["file"] for face in coverage["faces"]}
        packaged_fonts = {name for name in archive.namelist() if name.startswith("assets/www/content/fonts/") and name.endswith(".woff2")}
        assert expected_fonts == packaged_fonts, "Packaged fonts are missing or include stale full fonts"
        for face in coverage["faces"]:
            data = archive.read("assets/www/content/fonts/" + face["file"])
            assert hashlib.sha256(data).hexdigest() == face["sha256"], face["file"]
            assert struct.unpack_from(">I", data, 16)[0] == face["decodedBytes"] <= 8 * 1024 * 1024, face["file"]
    catalog = json.loads((ROOT / "rewrite/data/runtime-content.json").read_text(encoding="utf-8"))
    for entry in catalog["files"]:
        path = entry["path"]
        relative = "assets/" + ("www/content/" + path[7:] if path.startswith("client/") else
                                "data/" + Path(path).name if path.startswith("legacy/") else path)
        assert archive.read(relative) == (ROOT / "rewrite/data" / path).read_bytes(), relative + " is stale"
        files[relative] = entry["sha256"]
    managed = ROOT / (args.managed or "rewrite/Ew4.Android/obj/Release/net10.0-android/android-arm64/app_shared_libraries/arm64-v8a/arm64-v8a/assemblies.arm64-v8a.blob.so")
    relative = "lib/arm64-v8a/libassemblies.arm64-v8a.blob.so"
    wrapped = archive.read(relative)
    # Android wraps the raw assembly store in an ELF payload section.
    header = struct.unpack_from("<16sHHIQQQIHHHHHH", wrapped)
    assert header[0][:6] == b"\x7fELF\x02\x01" and header[2] == 183, "Expected ARM64 ELF assembly store"
    sections = [struct.unpack_from("<IIQQQQIIQQ", wrapped, header[6] + i * header[11]) for i in range(header[12])]
    strings = sections[header[13]]
    names = wrapped[strings[4]:strings[4] + strings[5]]
    payload, = [s for s in sections if names[s[0]:].split(b"\0", 1)[0] == b"payload"]
    assert wrapped[payload[4]:payload[4] + payload[5]] == managed.read_bytes(), "Packaged gameplay assemblies are stale"
    files[relative] = hashlib.sha256(wrapped).hexdigest()
report = dict(apk=str(APK.relative_to(ROOT)), bytes=APK.stat().st_size,
              sha256=hashlib.sha256(APK.read_bytes()).hexdigest(), files=files, errors=0,
              scope="CRC, ARM64 runtime, exact frontend/cache and gameplay assembly blob hashes; no device execution")
(ROOT / args.report).write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "files"}))
