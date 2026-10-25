"""Capture original tutorial frame adjustments and pointer constants."""
import json
import hashlib
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from validate_native_rules import NativeProbe, ROOT, SCRATCH, RETURN
from unicorn import arm64_const as registers
from PIL import Image


def main():
    probe = NativeProbe()
    controller, frame, table, rect = [SCRATCH + i * 0x1000 for i in range(4)]
    probe.write(controller + 0x30, "Q", frame)
    probe.write(frame, "Q", table)
    probe.write(table + 0x18, "Q", RETURN + 0x10000)
    probe.write(table + 0x20, "Q", RETURN + 0x10004)
    probe.hook(0xb0330, lambda *_: 0)
    probe.hook(RETURN + 0x10000, lambda *_: 0)
    probe.hook(RETURN + 0x10004, lambda *_: 0)
    vectors = []
    for scale in (0.5, 1, 1.5, 2, 3):
        for bounds, correction in (([100, 100, 208, 23], [-90, 0]),
                                   ([0, 0, 37, 37], [-24, -22]),
                                   ([1, 2, 40, 30], [0, 0]),
                                   ([80, 80, 33, 33], [-4, -4])):
            probe.write(rect, "4f", *bounds)
            for index, value in enumerate([*correction, scale]):
                probe.write(SCRATCH + 0x5000, "f", value)
                bits = probe.read(SCRATCH + 0x5000, "I")[0]
                probe.cpu.reg_write(getattr(registers, f"UC_ARM64_REG_S{index}"), bits)
            probe.call(0xa52e4, controller, rect)
            vectors.append(dict(scale=scale, rect=bounds, correction=correction,
                                expected=list(probe.read(frame + 0x4c, "4f"))))
    report = dict(vectors=vectors, pointer={
        "width": probe.read(0xa66c0, "f")[0], "height": probe.read(0xa66c4, "f")[0],
        "travel": 15, "speed": probe.read(0xa5b18, "f")[0],
        "tipOffset": probe.read(0xa5b1c, "f")[0]},
        scope="Original ARM64 frame function and pointer literals; not a device render")
    source = json.loads((ROOT / "apk_work/source.json").read_text(encoding="utf-8"))
    entries = {entry["name"]: entry for entry in source["entries"]}
    assets = []
    for atlas in ("image_ui", "image_ui_hd"):
        folder = ROOT / "apk_work/original/assets"
        for suffix in ("png", "xml"):
            file = folder / f"{atlas}.{suffix}"
            assert hashlib.sha256(file.read_bytes()).hexdigest() == entries[f"assets/{file.name}"]["sha256"]
        tree = ET.fromstring("<atlas>" + (folder / f"{atlas}.xml").read_text(encoding="utf-8-sig") + "</atlas>")
        node = tree.find(".//Image[@name='tutorials_point.png']")
        x, y, w, h = [int(node.get(key)) for key in ("x", "y", "w", "h")]
        original = Image.open(folder / f"{atlas}.png").convert("RGBA").crop((x, y, x + w, y + h))
        file = ROOT / f"rewrite/data/client/ui/{atlas}/tutorials_point.png"
        cached = Image.open(file).convert("RGBA")
        assert original.size == cached.size and original.tobytes() == cached.tobytes()
        assets.append(dict(atlas=atlas, crop=[x, y, w, h], pixelDifference=0,
                           pngSha256=hashlib.sha256(file.read_bytes()).hexdigest()))
    report["provenance"] = dict(source=source["source"], apkSha256=source["sha256"], assets=assets,
                                scope="Unmodified supplied input APK; official publisher authenticity not established")
    map_arrows = []
    for atlas in ("buildings", "buildings_hd"):
        for suffix in ("png", "xml"):
            file = folder / f"{atlas}.{suffix}"
            assert hashlib.sha256(file.read_bytes()).hexdigest() == entries[f"assets/{file.name}"]["sha256"]
        tree = ET.fromstring("<atlas>" + (folder / f"{atlas}.xml").read_text(encoding="utf-8-sig") + "</atlas>")
        original = Image.open(folder / f"{atlas}.png").convert("RGBA")
        cached = Image.open(ROOT / f"rewrite/data/client/images/{atlas}.png").convert("RGBA")
        for name in ("arrow_blue.png", "arrow_red.png", "arrow_shadow.png"):
            node = tree.find(f".//Image[@name='{name}']")
            x, y, w, h = [int(node.get(key)) for key in ("x", "y", "w", "h")]
            bounds = (x, y, x + w, y + h)
            assert original.crop(bounds).tobytes() == cached.crop(bounds).tobytes()
            map_arrows.append(dict(atlas=atlas, name=name, crop=[x, y, w, h], pixelDifference=0))
    report["mapArrows"] = dict(assets=map_arrows, shadowScale=probe.read(0x8518c, "f")[0],
                                scope="Native wait-area map draw 184e30, asset bindings 165b14; not the UI pointer")
    motion = SCRATCH + 0x6000
    probe.write(motion + 0x1a4, "i", -1)
    probe.write(motion + 0x1ac, "i", -1)
    probe.hook(0x80374, lambda *_: 0)
    speed = probe.read(0x83ee8, "f")[0]
    motion_vectors = []
    for offset in (0, -10, -20):
        for velocity in (speed, -speed):
            for seconds in (0, 1 / 120, 1 / 60, 0.1, 0.5):
                probe.write(motion + 0xe4, "2f", offset, velocity)
                probe.write(SCRATCH + 0x7000, "f", seconds)
                probe.cpu.reg_write(registers.UC_ARM64_REG_S0, probe.read(SCRATCH + 0x7000, "I")[0])
                probe.call(0x83c3c, motion, 0)
                motion_vectors.append(dict(offset=offset, speed=velocity, seconds=seconds,
                                           expected=list(probe.read(motion + 0xe4, "2f"))))
    report["mapArrows"]["motion"] = dict(function="183c3c", initialSpeed=speed,
                                          minimum=-20, maximum=0, vectors=motion_vectors)
    output = Path(sys.argv[1])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(vectors=len(vectors), mapMotionVectors=len(motion_vectors), errors=0)))


if __name__ == "__main__":
    main()
