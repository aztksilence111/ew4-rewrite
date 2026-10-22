"""Export original effect timelines and emitter parameters, preserving source files."""
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
from export_presentation_assets import export as export_presentation

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "apk_work/original/assets"
OUTPUT = ROOT / "rewrite/data/client"


def export_effects():
    sources = {}
    def read(path):
        data = path.read_bytes()
        sources[path.name] = hashlib.sha256(data).hexdigest()
        return ET.fromstring(data.decode("utf-8-sig"))
    timelines = {}
    for timeline in read(SOURCE / "def_effectsanim.xml"):
        timelines[timeline.attrib["name"]] = [dict(type=group.attrib["type"], **entry.attrib) for group in timeline for entry in group]
    definitions = {}
    for path in SOURCE.glob("effect*.xml"):
        tree = read(path)
        if tree.tag != "effect":
            continue
        definitions[path.name] = [dict(life=emitter.attrib["life"], parameters={
            p.attrib["name"]: [dict(t.attrib) for t in p] if len(p) else {k: v for k, v in p.attrib.items() if k != "name"}
            for p in emitter}) for emitter in tree]
    motions = {u.attrib["name"]: dict(u.attrib, motions=[dict(m.attrib) for m in u]) for u in read(SOURCE / "def_motion.xml")}
    sounds = sorted({event.get("sound") for events in timelines.values() for event in events if event.get("sound") and (SOURCE / event["sound"]).exists()})
    sound_dir = OUTPUT / "sounds"
    sound_dir.mkdir(exist_ok=True)
    for sound in sounds:
        target = sound_dir / sound
        if not target.exists():
            shutil.copyfile(SOURCE / sound, target)
    for library in ("anim_fire", "anim_upgrade", "anim_smoke"):
        preferred = library + "_hd" if (ROOT / "rewrite/data/legacy/animations" / (library + "_hd.json")).exists() else library
        data = json.loads((ROOT / "rewrite/data/legacy/animations" / (preferred + ".json")).read_text(encoding="utf-8"))
        data["pixelRatio"] = 2 if preferred.endswith("_hd") else 1
        for texture in data["atlas"]["textures"]:
            texture["name"] = Path(texture["name"]).with_suffix(".png").name
            target = OUTPUT / "images" / texture["name"]
            if not target.exists():
                shutil.copyfile(SOURCE / texture["name"], target)
        (OUTPUT / "animations" / (library + ".json")).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    manifest = json.loads((OUTPUT / "manifest.json").read_text(encoding="utf-8"))
    bindings = {}
    for key, profile in manifest["profiles"].items():
        name, variant, grade = profile["kind"], key.split(".")[2], profile["grade"] + 1
        candidates = (f"{name} {variant} {grade}", f"{name} {grade}", name)
        bindings[key] = next(candidate for candidate in candidates if candidate in motions)
    data = dict(timelines=timelines, definitions=definitions, motions=motions, bindings=bindings, sounds=sounds)
    (OUTPUT / "effects.json").write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    presentation = export_presentation()
    report = dict(timelines=len(timelines), definitions=len(definitions), sounds=presentation["sounds"],
                  elementGroups=presentation["elementGroups"], sources=sources, errors=0)
    (ROOT / "rewrite/reports/frontend_effect_export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return {k: v for k, v in report.items() if k != "sources"}


if __name__ == "__main__":
    print(json.dumps(export_effects()))
