"""Export original campaign-upgrade artwork at native pixels, with provenance."""
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path.home() / ".codex/tools/ew4-inspect-deps"))
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "apk_work/original/assets"
OUTPUT = ROOT / "reverse/references/campaign-technology-original-20261026"
PREVIEWS = "militia line light grenadier guard machinegun lightcavalry heavycavalry musketcavalry armoredcar lightartillery heavyartillery fortressartillery rocket cruiser frigate battleship ironclads".split()
NAMES = {"recruit_" + name + suffix + ".png" for name in PREVIEWS for suffix in ("", "_ipad")}
NAMES.update("build_" + name + suffix + ".png" for name in ("smallfortress", "mediumfortress", "largefortress", "coastalartillery") for suffix in ("", "_ipad"))
NAMES.update("form_title.png form_title_shadow.png button_close.png whiteline_for_button_close.png common_pattern.png common_lineframe_bold.png general_nameboard.png button_fortress_soldiers.png stage_star.png arrow.png btn_common_blue.png btn_common_org.png btn_pattern.png button_upgrade.png pattern_1.png pattern_2.png".split())


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    assets, layouts, originals, differences = [], [], {}, []
    comparisons=0
    for path in sorted(SOURCE.glob("layout*.xml")):
        root = ET.parse(path).getroot()
        matches = [node for node in root.iter() if node.get("id") in ("form_upgrade", "form_compaignlist")]
        if not matches:
            continue
        target = OUTPUT / "layouts" / path.name
        target.parent.mkdir(exist_ok=True)
        target.write_text("<OriginalLayoutExtract>\n" + "\n".join(ET.tostring(node, encoding="unicode") for node in matches) + "\n</OriginalLayoutExtract>\n", encoding="utf-8")
        originals[path.name] = digest(path)
        for node in matches:
            layouts.append(dict(source=path.name, attributes=node.attrib,
                                controls=[child.attrib for child in node.iter() if child is not node]))
    for name in ("def_warzonetech.xml", "effect_unlocktech.xml", "effect_unlocktech_hd.xml", "sfx_lvup.wav"):
        path = SOURCE / name
        if path.exists():
            target = OUTPUT / "source" / name
            target.parent.mkdir(exist_ok=True)
            shutil.copyfile(path, target)
            originals[name] = digest(path)
            if name.startswith("effect_"):
                NAMES.update(node.get("file") for node in ET.parse(path).iter() if node.get("name") == "image" and node.get("file"))
    for path in sorted(SOURCE.glob("*.xml")):
        with path.open("r", encoding="utf-8-sig") as stream:
            if not stream.read(120).lstrip().startswith("<Texture"):
                continue
        atlas = ET.fromstring("<atlas>" + path.read_text(encoding="utf-8-sig") + "</atlas>")
        selected = [node for node in atlas.findall("Images/Image") if node.get("name") in NAMES or re.match(r"(?:button_upgrade_|upgrade_|campaign_)", node.get("name", ""))]
        if not selected:
            continue
        texture = SOURCE / atlas.find("Texture").get("name")
        if texture.suffix.lower() != ".png" or not texture.exists():
            continue
        image = Image.open(texture).convert("RGBA")
        originals[path.name], originals[texture.name] = digest(path), digest(texture)
        for node in selected:
            a = node.attrib
            x, y, w, h = (int(a[key]) for key in ("x", "y", "w", "h"))
            cropped = image.crop((x, y, x + w, y + h))
            target = OUTPUT / "sprites" / path.stem / a["name"]
            target.parent.mkdir(parents=True, exist_ok=True)
            cropped.save(target)
            cached = ROOT / "rewrite/data/client/ui" / path.stem / a["name"]
            if cached.exists():
                comparisons+=1
                if Image.open(cached).convert("RGBA").tobytes() != cropped.tobytes():
                    differences.append(str(cached.relative_to(ROOT)))
            category = "兵种预览" if a["name"].startswith(("recruit_", "build_")) else "界面与标识"
            assets.append(dict(name=a["name"], category=category, atlas=path.stem, sourceTexture=texture.name,
                               rectangle=[x,y,w,h], ref=[int(a.get("refx",0)),int(a.get("refy",0))],
                               logicalSize=[w/2,h/2] if "_hd" in path.stem else [w,h],
                               file=target.relative_to(OUTPUT).as_posix(), sha256=digest(target)))
    for path in sorted(SOURCE.glob("font*")):
        if re.match(r"font_(?:char_[13]|text_[234]_cn|title)(?:_hd)?\.", path.name):
            originals[path.name] = digest(path)
            target=OUTPUT/"source"/path.name
            target.parent.mkdir(exist_ok=True)
            shutil.copyfile(path,target)
    manifest = dict(scope="Original campaign upgrade UI and dependencies; original pixel crops, no rescaling", assets=assets,
                    layouts=layouts, sourceHashes=originals, cachedPixelComparisons=comparisons,cachedPixelDifferences=differences,
                    visibleCategories=["步兵", "骑兵", "炮兵", "海军", "要塞"],
                    hiddenCodeOnly=dict(slots=[22,23,24,25], meanings=["粮食收入", "金钱收入", "工业收入", "新招部队运输"],
                                        tabInOriginalLayout=False, matchingUpgradeSprites=False,
                                        transportDefaultLevels=[int(node.get("level")) for node in ET.parse(SOURCE / "def_warzonetech.xml").iter("tech") if node.get("id") == "25"],
                                        ordinaryRecruitmentGetsTransport=False))
    (OUTPUT / "inventory.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
    small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 12)
    preferred = [a for a in assets if a["atlas"] in ("image_ui_hd", "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3")]
    media_height=max(150,max(a["rectangle"][3] for a in preferred))
    tile_w, tile_h, columns = 230, media_height+48, 6
    sheet = Image.new("RGB", (columns*tile_w, 64 + ((len(preferred)+columns-1)//columns)*tile_h), "#eeeeec")
    draw = ImageDraw.Draw(sheet)
    draw.text((16,12), "原版战役升级资源 / 原始像素尺寸，无缩放", font=font, fill="#222222")
    for i, asset in enumerate(preferred):
        x, y = i%columns*tile_w, 64+i//columns*tile_h
        sprite = Image.open(OUTPUT/asset["file"]).convert("RGBA")
        for cy in range(y+2,y+media_height,10):
            for cx in range(x+2,x+tile_w-2,10):
                draw.rectangle((cx,cy,min(cx+9,x+tile_w-3),min(cy+9,y+media_height-1)), fill="#d3d3d0" if (cx//10+cy//10)%2 else "#fafafa")
        sheet.paste(sprite, (x+(tile_w-sprite.width)//2,y+2+(media_height-sprite.height)//2), sprite)
        draw.text((x+6,y+media_height+5),asset["name"],font=small,fill="#222222")
        draw.text((x+6,y+media_height+23),f'{sprite.width} x {sprite.height} px / {asset["atlas"]}',font=small,fill="#555555")
    sheet.save(OUTPUT/"original-assets.png")
    cards = []
    for asset in assets:
        w,h=asset["rectangle"][2:]
        cards.append('<article data-atlas="'+html.escape(asset["atlas"])+'"><div class="sprite"><img src="'+html.escape(asset["file"])+'" width="'+str(w)+'" height="'+str(h)+'" alt=""></div><a href="'+html.escape(asset["file"])+'">'+html.escape(asset["name"])+'</a><p>'+html.escape(asset["atlas"])+f' / {w} x {h} px</p></article>')
    page = '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>原版战役升级资源</title><style>body{font:14px "Microsoft YaHei",sans-serif;margin:24px;color:#252525;background:#f3f3f1}h1{font-size:24px}p{line-height:1.7}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:12px}article{border:1px solid #bbb;padding:10px;overflow:auto}article a{overflow-wrap:anywhere}.sprite{height:180px;display:flex;align-items:center;justify-content:center;background:repeating-conic-gradient(#ddd 0% 25%,#fff 0% 50%) 0 0/20px 20px}.sprite img{flex:none;max-width:none;object-fit:none}a{color:#205c86}nav{margin:18px 0;display:flex;flex-wrap:wrap;gap:16px}</style><h1>原版战役升级资源</h1><p>直接从保留原包图集按 XML 坐标无损裁切，按原始像素展示。包括普通、HD 与 iPad 变体；每张图的坐标、参考点和摘要见清单。原版窗口只配置步兵、骑兵、炮兵、海军、要塞五个分类。</p><nav><a href="inventory.json">完整清单与坐标</a><a href="original-assets.png">HD 资源总览</a><a href="original-user-reference.jpg">用户提供的原版截图</a><a href="layouts/layout.xml">原版升级页布局</a><a href="source/def_warzonetech.xml">六战区原科技表</a><a href="source/effect_unlocktech_hd.xml">原升级特效</a></nav><div class="grid">'+''.join(cards)+'</div></html>'
    (OUTPUT/"index.html").write_text(page,encoding="utf-8")
    print(json.dumps(dict(assets=len(assets),atlases=len(set(a["atlas"] for a in assets)),layouts=len(layouts),cachedPixelComparisons=comparisons,cachedPixelDifferences=len(differences)),ensure_ascii=False))


if __name__ == "__main__":
    main()
