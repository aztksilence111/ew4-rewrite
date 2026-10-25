"""Run the APK resource smoke test against staged Android IL on the desktop CLR."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import zipfile

parser = argparse.ArgumentParser(__doc__)
parser.add_argument("apk", type=Path)
parser.add_argument("--dotnet", required=True)
parser.add_argument("--output", type=Path, default=Path("build/android-startup-20261024/package-runtime"))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = (root / args.output).resolve()
assert root / "build" in output.parents, "Probe output must remain under repository build/"
output.mkdir(parents=True, exist_ok=True)
for source in (root / "rewrite/Ew4.ModChecks/bin/Release/net10.0").glob("*"):
    if source.is_file():
        shutil.copy2(source, output / source.name)
with zipfile.ZipFile(args.apk) as archive:
    elf = archive.read("lib/arm64-v8a/libassemblies.arm64-v8a.blob.so")
header = struct.unpack_from("<16sHHIQQQIHHHHHH", elf)
sections = [struct.unpack_from("<IIQQQQIIQQ", elf, header[6] + i * header[11]) for i in range(header[12])]
strings = sections[header[13]]
section_names = elf[strings[4]:strings[4] + strings[5]]
payload, = [s for s in sections if section_names[s[0]:].split(b"\0", 1)[0] == b"payload"]
blob = elf[payload[4]:payload[4] + payload[5]]
magic, version, count, index_count, index_bytes = struct.unpack_from("<5I", blob)
assert magic == 0x41424158 and version == 0x80010003 and index_bytes == index_count * 13, "Unsupported Android store"
descriptors = [struct.unpack_from("<7I", blob, 20 + index_bytes + i * 28) for i in range(count)]
offset = 20 + index_bytes + count * 28
names = []
for _ in range(count):
    size, = struct.unpack_from("<I", blob, offset)
    offset += 4
    names.append(blob[offset:offset + size].decode("utf-8"))
    offset += size
expected = {"Ew4." + name + ".dll" for name in ("Core", "Legacy", "Persistence", "Client", "Mods")}
for name, descriptor in zip(names, descriptors):
    if name not in expected:
        continue
    _, start, size, *_ = descriptor
    compressed = blob[start:start + size]
    assert compressed.startswith(b"XALZ"), name
    (output / name.replace(".dll", ".lz4")).write_bytes(compressed)
assert expected.issubset(names), "Missing gameplay assemblies"
codecs = sorted((Path(args.dotnet).parent / "packs/Microsoft.Android.Sdk.Windows").glob("*/tools/K4os.Compression.LZ4.dll"),
    key=lambda p: tuple(int(v) for v in p.parts[-3].split('.')))
subprocess.run(["pwsh", "-NoProfile", "-File", str(root / "tools/decode_android_assemblies.ps1"),
    "-Codec", str(codecs[-1]), "-Folder", str(output)], check=True)
hashes = {}
for name in sorted(expected):
    source = output / name
    data = bytearray(source.read_bytes())
    hashes[name] = hashlib.sha256(data).hexdigest()
    header = struct.unpack_from("<I", data, 60)[0]
    # These are IL assemblies. Only the disposable probe copy changes architecture.
    if struct.unpack_from("<H", data, header + 4)[0] == 0xAA64:
        struct.pack_into("<H", data, header + 4, 0x8664)
    source.write_bytes(data)
assert b"LegacyCatalogFiles" in (output / "Ew4.Legacy.dll").read_bytes(), "APK contains a pre-split rules assembly"
subprocess.run([args.dotnet, str(output / "Ew4.ModChecks.dll"), str(root),
    "--android-package-only", str(args.apk.resolve())], cwd=root, check=True)
(output.parent / "extracted-assemblies.json").write_text(json.dumps({"apk": str(args.apk),
    "sha256": hashlib.sha256(args.apk.read_bytes()).hexdigest(), "assemblies": hashes,
    "scope": "Exact APK store extraction; disposable PE architecture normalization for desktop IL execution; no device execution",
    "errors": 0}, indent=2) + "\n", encoding="utf-8")
