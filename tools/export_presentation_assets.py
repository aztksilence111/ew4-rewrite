"""Export verified action sounds and damaged-element layouts without changing the manifest."""
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "apk_work/original/assets"
OUTPUT = ROOT / "rewrite/data/client"
ACTION_SOUNDS = (
    "sfx_leg.wav", "sfx_cavalrymove.wav", "sfx_armourmove.wav", "sfx_naval.wav",
    "sfx_draft.wav", "sfx_occupy.wav", "sfx_supply.wav", "sfx_lvup.wav",
)
UI_SOUNDS = ("sfx_click.wav", "sfx_cancel.wav", "sfx_select.wav", "sfx_pop.wav", "sfx_lvup2.wav")


def export():
    effects = json.loads((OUTPUT / "effects.json").read_text(encoding="utf-8"))
    sounds = set(effects["sounds"])
    for name in (*ACTION_SOUNDS, *UI_SOUNDS):
        source = SOURCE / name
        if not source.is_file():
            raise FileNotFoundError(source)
        shutil.copyfile(source, OUTPUT / "sounds" / name)
        sounds.add(name)
    effects["sounds"] = sorted(sounds)
    effects["elements"] = {
        group.attrib["name"]: [dict(element.attrib) for element in group]
        for group in ET.parse(SOURCE / "def_elementanim.xml").getroot()
    }
    (OUTPUT / "presentation.json").write_text(
        json.dumps({"sounds": list((*ACTION_SOUNDS, *UI_SOUNDS)), "elements": effects["elements"]},
                   ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
    (OUTPUT / "effects.json").write_text(
        json.dumps(effects, ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
    index_path = OUTPUT.parent / "runtime-content.json"
    index = json.loads(index_path.read_text(encoding="utf-8"))
    entries = {entry["path"]: entry for entry in index["files"]}
    for path in [OUTPUT / "effects.json", OUTPUT / "presentation.json",
                 *(OUTPUT / "sounds" / name for name in (*ACTION_SOUNDS, *UI_SOUNDS))]:
        content = path.read_bytes()
        key = path.relative_to(OUTPUT.parent).as_posix()
        entries[key] = dict(path=key, bytes=len(content), sha256=hashlib.sha256(content).hexdigest())
    index["files"] = list(entries.values())
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {"actionSounds": len(ACTION_SOUNDS), "sounds": len(effects["sounds"]), "elementGroups": len(effects["elements"]),
            "manifestSha256": hashlib.sha256((OUTPUT / "manifest.json").read_bytes()).hexdigest()}


if __name__ == "__main__":
    print(json.dumps(export()))
