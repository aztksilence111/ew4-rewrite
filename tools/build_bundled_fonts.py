"""Build bounded offline font subsets without reducing the complete Unicode coverage."""

import hashlib
import json
import logging
from pathlib import Path
import sys
import shutil
import struct
from zipfile import ZipFile

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / "rewrite/local/font-sources-20261020"
OUTPUT = ROOT / "rewrite/data/client/fonts"
FULL = SOURCES / "full-woff2"
MAX_DECODED_BYTES = 8 * 1024 * 1024
logging.getLogger("fontTools.ttLib.tables._h_e_a_d").setLevel(logging.ERROR)


def ranges(points):
    result = []
    for point in sorted(points):
        if result and point == result[-1][1] + 1:
            result[-1][1] = point
        else:
            result.append([point, point])
    return result


def bootstrap_points():
    points = set(range(32, 0x250)) | {0xFFFD, 0x3000}
    for directory in (ROOT / "rewrite/data/original", ROOT / "rewrite/Ew4.Web/wwwroot"):
        for file in directory.rglob("*"):
            if file.suffix.lower() not in (".xml", ".ini", ".json", ".html", ".js"):
                continue
            data = file.read_bytes()
            for encoding in ("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig", "gb18030"):
                try:
                    points.update(map(ord, data.decode(encoding)))
                    break
                except UnicodeError:
                    continue
    return points


def build_subset(source, destination, points):
    font = TTFont(source, fontNumber=0)
    options = subset.Options()
    options.recalc_timestamp = False
    options.drop_tables += ["FFTM"]
    cutter = subset.Subsetter(options=options)
    cutter.populate(unicodes=points)
    cutter.subset(font)
    # Shared glyph aliases can retain extra cmap entries outside this subset's range.
    for table in font["cmap"].tables:
        if table.isUnicode() and hasattr(table, "cmap"):
            table.cmap = {point: glyph for point, glyph in table.cmap.items() if point in points}
    if "fvar" in font:
        font = instantiateVariableFont(font, {"wght": 400}, inplace=True)
    if source.name == "ukai.ttc":
        notice = "2026-10-23: CN face extracted and converted to bounded Unicode WOFF2 subsets; glyph outlines unchanged."
        font["name"].setName(notice, 10, 3, 1, 0x409)
    if source.name.startswith("Liberation"):
        # OFL reserved family names must not be retained by modified subsets.
        combat = "Sans" in source.name
        family = "EW4 Combat" if combat else "EW4 UI Latin"
        postscript = "EW4Combat-BoldItalic" if combat else "EW4UILatin-Bold"
        for record in font["name"].names:
            replacement = {1: family, 3: postscript + ";20261023", 4: family, 6: postscript, 16: family}.get(record.nameID)
            if replacement:
                record.string = replacement.encode(record.getEncoding())
        font["name"].setName("2026-10-23: renamed and subset for offline EW4 display; glyph outlines unchanged.", 10, 3, 1, 0x409)
    font.flavor = "woff2"
    font.save(destination)
    font.close()


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    FULL.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "parts").mkdir(exist_ok=True)
    common = bootstrap_points()
    with ZipFile(SOURCES / "Jigmo-20250912.zip") as archive:
        for name in ("Jigmo.ttf", "Jigmo2.ttf", "Jigmo3.ttf", "LICENSE.txt"):
            matches = [n for n in archive.namelist() if Path(n).name == name]
            if len(matches) != 1:
                raise ValueError(f"Jigmo source is missing {name}")
            destination = OUTPUT / "Jigmo-LICENSE.txt" if name == "LICENSE.txt" else SOURCES / name
            destination.write_bytes(archive.read(matches[0]))

    recipes = [
        ("ukai.ttc", "ARPLUKaiCN.woff2", "EW4 UI Kai", "\u4e2d", "https://deb.debian.org/debian/pool/main/f/fonts-arphic-ukai/fonts-arphic-ukai_0.2.20080216.2-6_all.deb"),
        ("LiberationSerif-Bold.ttf", "LiberationSerif-Bold.woff2", "EW4 UI Latin", "A", "https://deb.debian.org/debian/pool/main/f/fonts-liberation/fonts-liberation_2.1.5-3_all.deb"),
        ("LiberationSans-BoldItalic.ttf", "LiberationSans-BoldItalic.woff2", "EW4 Combat", "0", "https://deb.debian.org/debian/pool/main/f/fonts-liberation/fonts-liberation_2.1.5-3_all.deb"),
        ("LXGWWenKai-Regular.ttf", "LXGWWenKai-Regular.woff2", "EW4 Kai", "A", "https://github.com/lxgw/LxgwWenKai/releases/tag/v1.530"),
        ("NotoSerifSC-Variable.ttf", "NotoSerifSC-Regular.woff2", "EW4 Serif", "A", "https://github.com/google/fonts/tree/main/ofl/notoserifsc"),
        ("Jigmo.ttf", "Jigmo.woff2", "EW4 Han", "\u4e00", "https://kamichikoichi.github.io/jigmo/Jigmo-20250912.zip"),
        ("Jigmo2.ttf", "Jigmo2.woff2", "EW4 Han Plane 2", "\U00020000", "https://kamichikoichi.github.io/jigmo/Jigmo-20250912.zip"),
        ("Jigmo3.ttf", "Jigmo3.woff2", "EW4 Han Plane 3", "\U00030000", "https://kamichikoichi.github.io/jigmo/Jigmo-20250912.zip"),
        ("unifont-17.0.05.otf", "Unifont.woff2", "EW4 Unicode", "A", "https://unifoundry.com/pub/unifont/unifont-17.0.05/"),
        ("unifont_upper-17.0.05.otf", "UnifontUpper.woff2", "EW4 Unicode Supplement", "\U00010000", "https://unifoundry.com/pub/unifont/unifont-17.0.05/"),
    ]
    (OUTPUT / "LXGWWenKai-OFL.txt").write_bytes((SOURCES / "OFL.txt").read_bytes())
    for license_name in ("UKai-LICENSE.txt", "Liberation-LICENSE.txt"):
        (OUTPUT / license_name).write_bytes((SOURCES / license_name).read_bytes())
    all_points = set()
    faces = []
    for source_name, filename, family, probe, upstream in recipes:
        source = SOURCES / source_name
        destination = FULL / filename
        previous = OUTPUT / filename
        if not destination.exists() and previous.exists():
            shutil.copyfile(previous, destination)
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        font = TTFont(source, fontNumber=0)
        points = {point for point, glyph in font.getBestCmap().items() if glyph != ".notdef"}
        if family == "EW4 UI Latin":
            points = {p for p in points if p < 0x250 or 0x1E00 <= p <= 0x1EFF or 0x2000 <= p <= 0x206F or 0x20A0 <= p <= 0x20CF}
        elif family == "EW4 Combat":
            points &= set(map(ord, "-0123456789"))
        assert ord(probe) in points, family
        all_points.update(points)
        font.close()
        core = points & common if family in ("EW4 UI Kai", "EW4 UI Latin", "EW4 Combat", "EW4 Kai", "EW4 Serif", "EW4 Unicode") else set()
        groups = [("core", core)] if core else []
        remaining = points - core
        for block in sorted({point // 2048 for point in remaining}):
            groups.append((f"{block:03x}", {point for point in remaining if point // 2048 == block}))
        for key, characters in groups:
            relative = f"parts/{Path(filename).stem}-{key}.woff2"
            target = OUTPUT / relative
            style_version = "ukai-cn-notice-v1" if source.name == "ukai.ttc" else "reserved-names-v1" if source.name.startswith("Liberation") else ""
            signature = hashlib.sha256((source_hash + style_version + json.dumps(sorted(characters))).encode()).hexdigest()
            cache_file = SOURCES / (target.name + ".subset.json")
            if not target.exists() or not cache_file.exists() or json.loads(cache_file.read_text())["sha256"] != signature:
                build_subset(source, target, characters)
                cache_file.write_text(json.dumps({"sha256": signature}) + "\n", encoding="utf-8")
            with TTFont(target) as check:
                actual = {point for point, glyph in check.getBestCmap().items() if glyph != ".notdef"}
            if actual != characters:
                build_subset(source, target, characters)
                with TTFont(target) as check:
                    actual = {point for point, glyph in check.getBestCmap().items() if glyph != ".notdef"}
            assert actual == characters, relative
            data = target.read_bytes()
            decoded = struct.unpack_from(">I", data, 16)[0]
            assert decoded <= MAX_DECODED_BYTES, f"Font subset exceeds decoded limit: {relative} ({decoded})"
            faces.append(dict(file=relative, family=family, probe=chr(min(characters)), glyphs=len(characters),
                              bootstrap=key == "core" and family not in ("EW4 UI Latin", "EW4 Combat"), ranges=ranges(characters), decodedBytes=decoded,
                              bytes=len(data), sha256=hashlib.sha256(data).hexdigest(), sourceSha256=source_hash, upstream=upstream))
        if previous.exists():
            assert previous.read_bytes() == destination.read_bytes(), "Preserve the previous full font before removing its runtime copy"
            previous.unlink()
        print(f"{family}: {len(points)} characters, {len(groups)} bounded subsets", flush=True)
    assert 0xFFFD in all_points
    manifest = dict(schema=1, loading="subsets-v1", unicodeVersion="17.0", faces=faces,
                    characters=len(all_points), ranges=ranges(all_points))
    (OUTPUT / "coverage.json").write_text(json.dumps(manifest, ensure_ascii=True, indent=2) + "\n", encoding="utf-8", newline="\n")
    index_file = ROOT / "rewrite/data/runtime-content.json"
    index = json.loads(index_file.read_text(encoding="utf-8"))
    font_position = next((i for i, entry in enumerate(index["files"]) if entry["path"].startswith("client/fonts/")), len(index["files"]))
    index["files"] = [entry for entry in index["files"] if not entry["path"].startswith("client/fonts/")]
    font_entries = []
    for file in sorted(OUTPUT.rglob("*")):
        if file.name == "README.md" or not file.is_file():
            continue
        font_entries.append(dict(path="client/fonts/" + file.relative_to(OUTPUT).as_posix(), bytes=file.stat().st_size,
                                   sha256=hashlib.sha256(file.read_bytes()).hexdigest()))
    index["files"][font_position:font_position] = font_entries
    index_file.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(dict(characters=len(all_points), bytes=sum(f["bytes"] for f in faces), faces=len(faces))), flush=True)


if __name__ == "__main__":
    main()
