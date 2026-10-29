"""Compose the early native/Web startup image from original loading-page assets."""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "rewrite/data/client"


def build():
    manifest = json.loads((CLIENT / "manifest.json").read_text(encoding="utf-8"))
    result = Image.new("RGBA", (1136, 640), "black")
    title = Image.open(CLIENT / "ui/title.png").convert("RGBA")
    result.alpha_composite(title.resize((380, 96), Image.Resampling.LANCZOS), (20, 16))

    def text(value, font_name, left, top, max_width=1054):
        font = manifest["fonts"][font_name]
        sheet = Image.open(CLIENT / "images" / (font_name + ".png")).convert("RGBA")
        cursor, line = left, top
        for char in value:
            x, y, w, h, dx, dy, advance = font["glyphs"][str(ord(char))]
            if cursor + advance > left + max_width:
                cursor, line = left, line + font["height"]
            result.alpha_composite(sheet.crop((x, y, x + w, y + h)), (cursor - dx, line - dy))
            cursor += advance

    text(manifest["strings"]["text_tips_1"], "font_text_2_cn_hd", 40, 300)
    text("LOADING...", "font_char_3_hd", 960, 600)
    # Burn the notice into the bitmap so it is also visible before WebView exists.
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 24)
    ImageDraw.Draw(result).text((40, 550), "Rewrite 版 · 不用于商用", font=font, fill="#c4c4c4")
    target = ROOT / "rewrite/Ew4.Web/wwwroot/startup-loading.png"
    result.convert("RGB").save(target, optimize=True)
    return {"image": str(target.relative_to(ROOT)), "width": 1136, "height": 640}


if __name__ == "__main__":
    print(json.dumps(build()))
