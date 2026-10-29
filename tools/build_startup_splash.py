"""Compose the native/Web initialization splash from the original APK logo."""
import hashlib
import io
import json
from pathlib import Path
from zipfile import ZipFile

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def build():
    asset = "assets/logo@2x.png"
    with ZipFile(ROOT / "欧陆战争4_1.6.6.apk") as package:
        original = package.read(asset)
    result = Image.new("RGBA", (1136, 640), "white")
    logo = Image.open(io.BytesIO(original)).convert("RGBA")
    result.alpha_composite(logo.resize((460, 460), Image.Resampling.LANCZOS), (338, 70))
    # Burn the notice into the bitmap so it is also visible before WebView exists.
    draw = ImageDraw.Draw(result)
    for text, size, top in [("Rewrite版", 28, 516), ("非商用", 20, 562)]:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", size)
        draw.text((568, top), text, anchor="mt", font=font, fill="#444444")
    target = ROOT / "rewrite/Ew4.Web/wwwroot/startup-loading.png"
    result.convert("RGB").save(target, optimize=True)
    return {"image": str(target.relative_to(ROOT)), "width": 1136, "height": 640,
            "source": asset, "sourceSha256": hashlib.sha256(original).hexdigest()}


if __name__ == "__main__":
    print(json.dumps(build()))
