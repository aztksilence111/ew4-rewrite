"""Build lossless render caches from the preserved original assets, without replacing them."""

import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
from PIL import Image
import texture2ddecoder
from ew4_formats import BitmapFont, Pvr4444, TerrainMap, TextureIndex

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "apk_work/original/assets"
OUTPUT = ROOT / "rewrite/data/client"


def fragment(path):
    return ET.fromstring("<atlas>" + path.read_text(encoding="utf-8-sig") + "</atlas>")


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    images = OUTPUT / "images"
    images.mkdir(exist_ok=True)
    inputs = {}
    atlases = {}
    counts = {"png": 0, "pkm": 0, "maps": 0, "map_tiles": 0, "animations": 0}

    def track(path):
        inputs[path.relative_to(SOURCE).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()

    for path in SOURCE.glob("*.png"):
        if ("_hd" in path.stem or "@2x" in path.stem or "_ipad" in path.stem.lower()) and not (
            path.stem in ("image_ui_hd", "image_menu_hd", "image_flag_hd", "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3", "image_newgame_hd", "image_item_hd", "buildings_hd", "tiles_hd", "commandermark_hd", "strategicmap_hd", "choose_hd", "font_text_1_cn_hd", "font_text_2_cn_hd", "font_text_3_cn_hd", "font_text_4_cn_hd", "font_char_1_hd", "font_char_2_hd", "font_char_3_hd", "font_char_4_hd", "font_ascii")
            or path.stem in ("image_boundary@2x", "princesses_sketch_hd")
            or path.stem.startswith("tex_conquest_") and path.stem.endswith("@2x")):
            continue
        track(path)
        shutil.copyfile(path, images / path.name)
        counts["png"] += 1
    for path in SOURCE.glob("*.pkm"):
        if ("_hd" in path.stem or "@2x" in path.stem or "_ipad" in path.stem.lower()) and path.stem not in (
            "tex_mainmenu_back_ip5@2x", "tex_campaign_back@2x", "map_pt@2x", "commanders1_hd", "commanders2_hd", "princesses_hd"):
            continue
        track(path)
        data = path.read_bytes()
        if data[:6] != b"PKM 10" or struct.unpack_from(">H", data, 6)[0] != 0:
            raise ValueError("Unsupported original PKM encoding: " + path.name)
        encoded_w, encoded_h, width, height = struct.unpack_from(">4H", data, 8)
        if len(data) != 16 + encoded_w * encoded_h // 2 or width > encoded_w or height > encoded_h:
            raise ValueError("Invalid original PKM dimensions: " + path.name)
        decoded = texture2ddecoder.decode_etc1(data[16:], encoded_w, encoded_h)
        Image.frombytes("RGBA", (encoded_w, encoded_h), decoded, "raw", "BGRA").crop((0, 0, width, height)).save(images / (path.stem + ".png"))
        counts["pkm"] += 1
    for path in SOURCE.glob("*.xml"):
        text = path.read_text(encoding="utf-8-sig")
        if not text.lstrip().startswith("<Texture") or not (images / (path.stem + ".png")).exists():
            continue
        track(path)
        atlas = fragment(path)
        atlases[path.stem] = {s.attrib["name"]: {k: float(v) for k, v in s.attrib.items() if k != "name"} for s in atlas.findall("Images/Image")}

    terrain_defs = ET.parse(SOURCE / "def_mapterrain.xml").getroot()
    maps = {}
    for definition in ET.parse(SOURCE / "def_map.xml").getroot():
        name = definition.attrib["name"]
        index_path = SOURCE / definition.attrib["tile"]
        hd_index = index_path.with_stem(index_path.stem + "_hd")
        ratio = 2 if hd_index.exists() else 1
        if ratio == 2:
            index_path = hd_index
        terrain_path = SOURCE / definition.attrib["file"]
        track(index_path)
        track(terrain_path)
        index = TextureIndex.from_bytes(index_path.read_bytes(), ratio)

        def load_atlas(number):
            path = SOURCE / "map" / (definition.attrib["tile"].removesuffix(".bin") + "_" + str(number) + ("@2x" if ratio == 2 else "") + ".pvr")
            track(path)
            return Pvr4444.from_bytes(path.read_bytes()).to_image()

        rendered = index.render(load_atlas)
        folder = OUTPUT / "maps" / name
        folder.mkdir(parents=True, exist_ok=True)
        tiles = []
        for top in range(0, index.height, 512 * ratio):
            for left in range(0, index.width, 512 * ratio):
                tile = rendered.crop((left, top, min(left + 512 * ratio, index.width), min(top + 512 * ratio, index.height)))
                filename = f"{left // ratio}_{top // ratio}.png"
                tile.save(folder / filename)
                tiles.append({"x": left / ratio, "y": top / ratio, "width": tile.width / ratio, "height": tile.height / ratio, "file": f"maps/{name}/{filename}"})
        terrain = TerrainMap.from_bytes(terrain_path.read_bytes())
        # Cell records contain original sprite indices and per-layer placement offsets.
        overlays = []
        for cell, record in enumerate(terrain.cells):
            row, column = divmod(cell, terrain.width)
            for layer, (kind, sprite, dx, dy) in enumerate(((record.background_kind, record.background_index, record.background_dx, record.background_dy),
                                         (record.foreground_kind, record.foreground_index, record.foreground_dx, record.foreground_dy))):
                # 505b8 draws terrain before the map mask; 5067c draws sea foreground later.
                if kind in (0, 11) or (layer == 0 and kind == 1) or sprite == 255:
                    continue
                definitions = list(terrain_defs)[kind]
                entry = next(e for e in definitions if int(e.attrib["idx"]) == sprite)
                overlays.append({"cell": cell, "layer": layer, "seaForeground": kind == 1, "image": entry.attrib["image"], "x": column * 64 + (row % 2) * 32 + dx, "y": row * 54 - 18 + dy})
        text_path = SOURCE / definition.attrib["textpos"]
        track(text_path)
        map_labels = [dict(e.attrib) for e in ET.parse(text_path).getroot()]
        maps[name] = {"width": index.width / ratio, "height": index.height / ratio, "pixelRatio": ratio, "tiles": tiles, "overlays": overlays, "labels": map_labels}
        counts["maps"] += 1
        counts["map_tiles"] += len(tiles)

    motions = {unit.attrib["name"]: unit for unit in ET.parse(SOURCE / "def_motion.xml").getroot()}
    strings = {}
    for line in (SOURCE / "stringtable_cn.ini").read_text(encoding="utf-8-sig").splitlines():
        if "=" in line and not line.lstrip().startswith(";"):
            key, value = line.split("=", 1)
            strings[key.strip()] = value.strip()

    # Restore presentation from source sprites and glyph metrics, not system-font approximations.
    ui_folder = OUTPUT / "ui"
    ui_folder.mkdir(exist_ok=True)
    for atlas_name in ("image_ui", "image_ui_hd", "image_menu_hd", "image_flag_hd", "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3", "image_newgame_hd", "image_item_hd", "commanders1_hd", "commanders2_hd", "princesses_hd", "princesses_sketch_hd"):
        texture = Image.open(images / (atlas_name + ".png")).convert("RGBA")
        folder = ui_folder / atlas_name
        folder.mkdir(exist_ok=True)
        for name, s in atlases[atlas_name].items():
            texture.crop(tuple(int(s[k]) for k in ("x", "y")) + (int(s["x"] + s["w"]), int(s["y"] + s["h"]))).save(folder / name)
    title = SOURCE / "zh_CN.lproj/title_hd.png"
    track(title)
    Image.open(title).crop((0, 0, 759, 192)).save(ui_folder / "title.png")
    for source, name, height in (("tex_mainmenu_back_ip5@2x", "main", 776), ("tex_campaign_back@2x", "campaign", 1440)):
        background = Image.open(images / (source + ".png"))
        background.crop((0, 0, background.width, height)).save(ui_folder / (name + ".png"))
    background = Image.open(images / "tex_campaign_back@2x.png")
    background.crop((4, 184, 2044, 1256)).save(ui_folder / "conquest_background.png")
    background.crop((0, 120, 2048, 1328)).save(ui_folder / "campaign_background.png")
    fonts = {}
    for name in ("font_text_1_cn_hd", "font_text_2_cn_hd", "font_text_3_cn_hd", "font_text_4_cn_hd", "font_char_1_hd", "font_char_2_hd", "font_char_3_hd", "font_char_4_hd", "font_ascii", "font_city", "font_city_hd", "font_fight_num", "font_fight_num_hd"):
        path = SOURCE / (name + ".fnt")
        track(path)
        font = BitmapFont.from_bytes(path.read_bytes())
        fonts[name] = {"height": font.line_height, "glyphs": {str(r[0]): list(r[1:]) for r in font.records}}
        if name in ("font_city_hd", "font_fight_num", "font_fight_num_hd"):
            track(SOURCE / (name + ".png"))
            shutil.copyfile(SOURCE / (name + ".png"), images / (name + ".png"))
    fight_fonts = {name: fonts.pop(name) for name in ("font_fight_num", "font_fight_num_hd")}
    (OUTPUT / "fight-font.json").write_text(json.dumps(fight_fonts, separators=(",", ":")), encoding="utf-8")
    labels = {}

    def label(key, text, font_name="font_text_4_cn_hd", dark=False):
        font = fonts[font_name]
        records = []
        for char in text:
            chosen = next((name for name in (font_name, "font_text_1_cn_hd", "font_text_2_cn_hd", "font_text_3_cn_hd", "font_text_4_cn_hd") if str(ord(char)) in fonts[name]["glyphs"]), None)
            if chosen is None:
                raise ValueError("Missing original UI glyph: " + text)
            records.append((chosen, fonts[chosen]["glyphs"][str(ord(char))]))
        result = Image.new("RGBA", (sum(r[6] for _, r in records) + 32, font["height"] + 32))
        cursor = 16
        for chosen, (x, y, w, h, dx, dy, advance) in records:
            sheet = Image.open(images / (chosen + ".png")).convert("RGBA")
            # Native text calls sprite Draw(x,y), which subtracts the glyph reference point.
            result.alpha_composite(sheet.crop((x, y, x + w, y + h)), (cursor - dx, 8 - dy))
            cursor += advance
        box = result.getbbox()
        result = result.crop(box)
        if dark:
            alpha = result.getchannel("A")
            result.paste((55, 55, 53, 255), (0, 0, result.width, result.height))
            result.putalpha(alpha)
        result.save(ui_folder / (key + ".png"))
        labels[key] = {"file": "ui/" + key + ".png", "width": result.width / 2, "height": result.height / 2}

    for key, text in {"campaign_label": "战 役", "conquest_label": "征 服", "tutorial_label": "教 程", "headquarters_label": "指挥部", "settings_label": "设 定", "country_label": "选择国家", "pause_label": "暂 停", "resume_label": "继 续", "save_label": "保存", "load_label": "载入", "main_label": "退出", "restart_label": "重新开始", "info_label": "信 息", "recruit_label": "雇 佣", "defense_label": "防 御"}.items():
        label(key, text, "font_text_3_cn_hd" if key in ("save_label", "main_label", "restart_label") else "font_text_4_cn_hd")
    for key, text in strings.items():
        if key.startswith("name_") and key[5:] + "1.png" in atlases["image_flag_hd"]:
            label("country_" + key[5:], text, "font_text_2_cn_hd", True)
    for key in ("name_music", "name_sound", "name_gamespeed", "name_showgrid", "name_hdversion", "name_screen_extra", "name_screen_option"):
        label(key, strings[key], "font_text_2_cn_hd", True)
    profiles = {}
    libraries = {"choose", "strategicmap", "maptext"}
    unit_kinds = list(dict.fromkeys(a.attrib["name"] for c in ET.parse(SOURCE / "def_army.xml").getroot() for a in c.findall("army")))
    card_names = ["militia", "line", "light", "grenadier", "guard", "machinegun", "lightcavalry", "heavycavalry", "musketcavalry", "armoredcar", "lightartillery", "heavyartillery", "fortressartillery", "rocket", "cruiser", "frigate", "battleship", "ironclads", "smallfortress", "mediumfortress", "largefortress", "coastalartillery"]
    for country in ET.parse(SOURCE / "def_army.xml").getroot():
        variant = country.attrib["name"]
        for army in country.findall("army"):
            name, grade = army.attrib["name"], int(army.attrib["grade"])
            key = f"ew4.unit.{variant}.{name.lower().replace(' ', '_')}.{grade}"
            motion = motions.get(f"{name} {variant} {grade + 1}")
            if motion is None:
                motion = motions.get(f"{name} {grade + 1}")
            if motion is None:
                motion = motions.get(name)
            if motion is None:
                raise ValueError("Missing original unit motion: " + key)
            attributes = motion.attrib
            unit_index = unit_kinds.index(name)
            card_name = card_names[unit_index]
            card = ("build_" if unit_index >= 18 else "recruit_") + card_name + ".png"
            marker = ("buildmarker_" if unit_index >= 18 else "recruitmarker_") + card_name + ".png"
            if not any(card in atlas for atlas in atlases.values()) or not any(marker in atlas for atlas in atlases.values()):
                raise ValueError("Missing original unit card/marker: " + name)
            profiles[key] = {"name": strings.get("name_" + name, name), "kind": name, "unitIndex": unit_index, "grade": grade, "scale": float(attributes.get("scale", 1)), "card": card, "cardMarker": marker, "stats": dict(army.attrib), "description": strings.get("desc_" + name, ""), "library": attributes["res"], "x": float(attributes.get("x", 0)), "y": float(attributes.get("y", 0)),
                             "motions": {m.attrib["type"]: m.attrib["name"] for m in motion if m.attrib.get("dir", "all") in ("all", "1", "right")}}
            libraries.add(attributes["res"])
    animation_folder = OUTPUT / "animations"
    animation_folder.mkdir(exist_ok=True)
    for library in sorted(libraries):
        preferred = library + "_hd" if (ROOT / "rewrite/data/legacy/animations" / (library + "_hd.json")).exists() else library
        source = ROOT / "rewrite/data/legacy/animations" / (preferred + ".json")
        if not source.exists():
            raise ValueError("Missing parsed animation library: " + library)
        data = json.loads(source.read_text(encoding="utf-8"))
        for texture in data["atlas"]["textures"]:
            texture["name"] = str(Path(texture["name"]).with_suffix(".png"))
            if not (images / texture["name"]).is_file():
                raise ValueError("Missing browser animation texture: " + texture["name"])
        data["pixelRatio"] = 2 if preferred.endswith("_hd") else 1
        (animation_folder / (library + ".json")).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        counts["animations"] += 1
    commander_names = {e.attrib["id"]: e.attrib["name"] for e in ET.parse(SOURCE / "def_commander.xml").getroot()}
    building_defs = ET.parse(SOURCE / "def_construction.xml").getroot()
    buildings = {e.attrib["type"]: [dict(level.attrib) for level in e] for e in building_defs}
    capitals = {int(e.attrib["id"]): dict(e.attrib) for e in ET.parse(SOURCE / "def_capital.xml").getroot()}
    layouts = ET.parse(SOURCE / "layout-568h.xml").getroot()
    # Keep device-specific stock references for transferring legacy Mod layout deltas.
    import_layouts = {p.name: p.read_text(encoding="utf-8-sig") for p in sorted(SOURCE.glob("layout*.xml"))}
    (OUTPUT / "import-layouts.json").write_text(json.dumps(import_layouts, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    def layout_record(e):
        return {"attributes": dict(e.attrib), "children": [layout_record(c) for c in e]}
    strategic = {e.attrib["file"].removesuffix(".btl"): dict(e.attrib) for e in ET.parse(SOURCE / "def_battlelist.xml").getroot()}
    effects = {}
    for name in ("recruit", "moving1", "moving2", "moving3", "moving4"):
        path = SOURCE / ("effect_" + name + ".xml")
        track(path)
        effects[name] = [{"life": float(emitter.attrib["life"]), "params": {p.attrib["name"]: {"values": {k:v for k,v in p.attrib.items() if k != "name"}, "tracks": [dict(t.attrib) for t in p]} for p in emitter}} for emitter in ET.parse(path).getroot()]
    tutorials = {}
    for path in sorted(SOURCE.glob("tutorials_script*.xml")):
        track(path)
        tutorials[path.stem.replace("_script", "")] = {"sha256": inputs[path.name], "commands": [dict(e.attrib) for e in ET.parse(path).getroot()]}
    manifest = {"schema": 1, "atlases": atlases, "maps": maps, "profiles": profiles, "commanders": commander_names, "fonts": fonts, "effects": effects, "buildings": buildings, "capitals": capitals, "strings": strings, "strategic": strategic, "tutorials": tutorials, "layouts": {e.attrib["id"]: layout_record(e) for e in layouts}, "ui": {"labels": labels}}
    vectors = json.loads((ROOT / "reverse/reports/frontend_draw_vectors.json").read_text(encoding="utf-8"))
    manifest["mapDrawing"] = {"sourceSha256": vectors["sourceSha256"], "borderTexture": "images/image_boundary@2x.png",
        "borderStorage": vectors["borderStorage"],
        "borders": [[[ [v[0] / 2, v[1] / 2, *v[2:]] for v in quad] for quad in case] for case in vectors["borders"]["2"]]}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    report = {"counts": counts, "sourceSha256": inputs, "errors": 0,
              "scope": "Derived lossless PNG/animation caches; no original file replaced, no new artwork or gameplay data."}
    (ROOT / "rewrite/reports/client_assets.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"counts": counts, "inputs": len(inputs), "errors": 0}))
    from export_frontend_effects import export_effects
    export_effects()


if __name__ == "__main__":
    main()
