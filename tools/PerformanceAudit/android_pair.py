"""Build a same-source Android JIT/AOT pair without installing or publishing."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--sdk", required=True)
    parser.add_argument("--java", required=True)
    parser.add_argument("--resume", action="store_true", help="Reuse verified variants only when source and APK hashes still match")
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / "build/device-plan-20261024"
    frontend = root / "rewrite/Ew4.Web/wwwroot"
    intermediate = root / "rewrite/Ew4.Android/obj/Release/net10.0-android/android-arm64"
    packages = root / "rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64"
    tools = sorted((Path(args.sdk) / "build-tools").iterdir())[-1]
    env = dict(os.environ, JAVA_HOME=args.java)
    result = {"scope": "Local ARM64 test pair, copy-mode linker in both, full AOT versus JIT; no device execution or installation", "variants": {}}
    subprocess.run(["node", "tools/frontend/build.cjs"], cwd=root, check=True, timeout=120)
    progress = folder / "android-pair-progress.json"
    if args.resume and progress.exists():
        previous = json.loads(progress.read_text())
        for variant, entry in previous["variants"].items():
            if entry["source"] == json.loads((frontend/"build-info.json").read_text()) and digest(root/entry["apk"]) == entry["sha256"]:
                result["variants"][variant] = entry
    for variant in ("aot", "jit"):
        if variant in result["variants"]:
            continue
        with (folder / ("android-pair-" + variant + ".log")).open("w", encoding="utf-8") as log:
            subprocess.run([args.dotnet, "build", "rewrite/Ew4.Android", "-c", "Release", "-t:SignAndroidPackage",
                "-p:RuntimeIdentifier=android-arm64", "-p:RuntimeIdentifiers=android-arm64", "-p:AndroidSdkDirectory="+args.sdk, "-p:JavaSdkDirectory="+args.java,
                "-p:Ew4AotExperiment="+variant, "-v", "minimal"], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=21600)
        apk = folder / ("ew4-device-plan-"+variant+"-arm64.apk")
        shutil.copy2(packages / "org.ew4.reconstruction-Signed.apk", apk)
        blob = intermediate / "app_shared_libraries/arm64-v8a/arm64-v8a/assemblies.arm64-v8a.blob.so"
        saved_blob = folder / (variant+"-assemblies.blob")
        shutil.copy2(blob, saved_blob)
        subprocess.run(["python", "tools/check_android_frontend_package.py", str(apk.relative_to(root)), "--managed", str(saved_blob.relative_to(root)),
            "--report", str((folder/(variant+"-package.json")).relative_to(root))], cwd=root, check=True, timeout=300)
        managed = {p.name: digest(p) for p in (intermediate / "linked").glob("Ew4.*.dll") if p.name != "Ew4.Android.dll"}
        assert len(managed) == 5, managed
        marker = b"Ew4RuntimeAot"
        value = b"true" if variant == "aot" else b"false"
        attribute = b"\x01\x00" + bytes([len(marker)]) + marker + bytes([len(value)]) + value + b"\x00\x00"
        assert attribute in (intermediate/"linked/Ew4.Android.dll").read_bytes(), "Runtime compilation marker missing"
        with zipfile.ZipFile(apk) as archive:
            info = json.loads(archive.read("assets/www/build-info.json"))
            assert archive.read("assets/www/performance.js") == (frontend / "performance.js").read_bytes()
            assert info == json.loads((frontend / "build-info.json").read_text())
            libraries = [n for n in archive.namelist() if "/libaot-" in n and n.endswith(".so")]
            if variant == "aot":
                assert all(any("libaot-"+name+".so" in n for n in libraries) for name in managed)
                assert len(libraries) >= 5
            else:
                assert not libraries, "JIT APK contains stale AOT images"
            native = []
            for name in archive.namelist():
                if not name.startswith("lib/") or not name.endswith(".so"):
                    continue
                data = archive.read(name)
                header = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
                assert header[0][:6] == b"\x7fELF\x02\x01" and header[2] == 183, name
                segments = [struct.unpack_from("<IIQQQQQQ", data, header[5]+i*header[9]) for i in range(header[10])]
                assert all(s[7] >= 16384 for s in segments if s[0] == 1), name+" load alignment"
                if name in libraries:
                    assert b"mono_aot_file_info" in data, name+" lacks AOT registration"
                native.append({"name": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        sign = subprocess.run([str(tools/"apksigner.bat"), "verify", "--verbose", "--print-certs", str(apk)], env=env, check=True, capture_output=True, text=True, timeout=120)
        (folder/(variant+"-signature.txt")).write_text(sign.stdout, encoding="utf-8")
        subprocess.run([str(tools/"zipalign.exe"), "-c", "-P", "16", "4", str(apk)], check=True, capture_output=True, timeout=120)
        result["variants"][variant] = {"apk": str(apk.relative_to(root)), "bytes": apk.stat().st_size, "sha256": digest(apk),
            "source": info, "managed": managed, "aotLibraries": len(libraries), "native": native,
            "runtimeMarkerVerified": True, "signer": next(line for line in sign.stdout.splitlines() if "certificate SHA-256 digest:" in line)}
        (folder/"android-pair-progress.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    aot, jit = result["variants"]["aot"], result["variants"]["jit"]
    subprocess.run(["node", "tools/frontend/build.cjs"], cwd=root, check=True, timeout=120)
    assert aot["source"] == json.loads((frontend/"build-info.json").read_text()), "Live source drift after packaging"
    assert aot["source"] == jit["source"], "Source drift between variants"
    assert aot["managed"] == jit["managed"], "AOT experiment changed managed gameplay IL"
    assert aot["signer"] == jit["signer"], "Signing identity differs"
    result["errors"] = 0
    (folder/"android-pair.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"errors":0,"aotBytes":aot["bytes"],"jitBytes":jit["bytes"],"aotLibraries":aot["aotLibraries"],"managedEqual":True}))


if __name__ == "__main__":
    main()
