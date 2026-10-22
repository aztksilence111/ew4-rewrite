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


def export():
    effects = json.loads((OUTPUT / "effects.json").read_text(encoding="utf-8"))
    sounds = set(effects["sounds"])
    for name in ACTION_SOUNDS:
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
        json.dumps({"sounds": list(ACTION_SOUNDS), "elements": effects["elements"]},
                   ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
    (OUTPUT / "effects.json").write_text(
        json.dumps(effects, ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
    return {"actionSounds": len(ACTION_SOUNDS), "sounds": len(effects["sounds"]), "elementGroups": len(effects["elements"]),
            "manifestSha256": hashlib.sha256((OUTPUT / "manifest.json").read_bytes()).hexdigest()}


if __name__ == "__main__":
    print(json.dumps(export()))
