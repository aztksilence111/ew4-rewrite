"""Build and verify the current ARM64 JIT test APK without installation or upload."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_index(root):
    files = []
    for project in (root / "rewrite").glob("Ew4.*"):
        if not project.is_dir():
            continue
        files.extend(path for path in project.rglob("*") if path.is_file()
            and not {"bin", "obj", "compat", "node_modules"}.intersection(path.relative_to(project).parts)
            and path.suffix in {".cs", ".csproj", ".js", ".css", ".html", ".targets", ".props"})
    files.extend([root / "rewrite/Frontend.Build.targets", root / "tools/frontend/build.cjs"])
    return {path.relative_to(root).as_posix(): sha(path) for path in sorted(files)}


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--sdk", required=True)
    parser.add_argument("--java", required=True)
    parser.add_argument("--output", type=Path, default=Path("build/device-plan-test-20261024"))
    parser.add_argument("--name", default="ew4-performance-test-20261024-arm64.apk")
    parser.add_argument("--require-core-symbol", action="append", default=[])
    args = parser.parse_args()
    root = args.root.resolve()
    folder = root / args.output
    folder.mkdir(parents=True, exist_ok=True)
    apk = folder / args.name
    assert not apk.exists(), "Preserve an existing verified test package"
    env = dict(os.environ, JAVA_HOME=args.java)

    def run(command, name):
        with (folder / name).open("w", encoding="utf-8") as log:
            subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=21600)

    frontend = root / "rewrite/Ew4.Web/wwwroot"
    run(["node", "tools/frontend/build.cjs"], "frontend-build.log")
    build = json.loads((frontend / "build-info.json").read_text())
    sources = source_index(root)
    descriptor = root / "rewrite/data/runtime-content.json"
    descriptor_hash = sha(descriptor)
    (folder / "build-inputs.json").write_text(json.dumps({"build": build, "sources": sources,
        "contentManifest": descriptor_hash}, indent=2) + "\n", encoding="utf-8")
    content = json.loads(descriptor.read_text(encoding="utf-8"))["files"]
    for entry in content:
        path = root / "rewrite/data" / entry["path"]
        assert path.stat().st_size == entry["bytes"] and sha(path) == entry["sha256"], entry["path"]
    run([args.dotnet, "build", "rewrite/Ew4.Android", "-c", "Release", "-t:Rebuild;SignAndroidPackage",
        "-p:RuntimeIdentifier=android-arm64", "-p:RuntimeIdentifiers=android-arm64", "-p:AndroidSdkDirectory=" + args.sdk,
        "-p:JavaSdkDirectory=" + args.java, "-v", "minimal"], "android-build.log")
    intermediate = root / "rewrite/Ew4.Android/obj/Release/net10.0-android/android-arm64"
    shutil.copy2(root / "rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64/org.ew4.reconstruction-Signed.apk", apk)
    blob = folder / "assemblies.blob"
    shutil.copy2(intermediate / "app_shared_libraries/arm64-v8a/arm64-v8a/assemblies.arm64-v8a.blob.so", blob)
    run(["python", "tools/check_android_frontend_package.py", str(apk.relative_to(root)),
        "--managed", str(blob.relative_to(root)), "--report", str((folder / "package-content.json").relative_to(root))], "package-check.log")
    build_tools = sorted((Path(args.sdk) / "build-tools").iterdir())[-1]
    run([str(build_tools / "apksigner.bat"), "verify", "--verbose", "--print-certs", str(apk)], "signature.log")
    signature = (folder / "signature.log").read_text()
    signer = next(line for line in signature.splitlines() if "certificate SHA-256 digest:" in line)
    previous = json.loads((root / "build/device-plan-20261024/android-pair.json").read_text())
    assert signer == previous["variants"]["jit"]["signer"], "Signing identity changed"
    run([str(build_tools / "zipalign.exe"), "-c", "-P", "16", "4", str(apk)], "alignment.log")
    run([str(build_tools / "aapt2.exe"), "dump", "badging", str(apk)], "badging.log")
    project = ET.parse(root / "rewrite/Ew4.Android/Ew4.Android.csproj").getroot()
    version = project.findtext(".//ApplicationDisplayVersion")
    code = project.findtext(".//ApplicationVersion")
    badging = (folder / "badging.log").read_text()
    assert "name='org.ew4.reconstruction'" in badging and f"versionCode='{code}'" in badging and f"versionName='{version}'" in badging
    native = []
    with zipfile.ZipFile(apk) as archive:
        assert json.loads(archive.read("assets/www/build-info.json")) == build
        assert archive.read("assets/www/performance.js") == (frontend / "performance.js").read_bytes()
        assert json.loads(archive.read("assets/data/catalog.json"))["schema_version"] == 3
        for name in archive.namelist():
            if not name.startswith("lib/") or not name.endswith(".so"):
                continue
            assert name.startswith("lib/arm64-v8a/") and "/libaot-" not in name, name
            data = archive.read(name)
            header = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
            assert header[0][:6] == b"\x7fELF\x02\x01" and header[2] == 183, name
            segments = [struct.unpack_from("<IIQQQQQQ", data, header[5] + i * header[9]) for i in range(header[10])]
            assert all(segment[7] >= 16384 for segment in segments if segment[0] == 1), name
            native.append({"name": name, "sha256": hashlib.sha256(data).hexdigest()})
    marker = b"Ew4RuntimeAot"
    attribute = b"\x01\x00" + bytes([len(marker)]) + marker + b"\x05false\x00\x00"
    packaged_assembly = intermediate / "android/assets/arm64-v8a/Ew4.Android.dll"
    assert attribute in packaged_assembly.read_bytes(), "JIT runtime marker missing"
    assert b"LegacyCatalogFiles" in (intermediate / "android/assets/arm64-v8a/Ew4.Legacy.dll").read_bytes(), "Stale pre-split rules assembly"
    run([args.dotnet, "build", "rewrite/Ew4.ModChecks", "-c", "Release", "-v", "minimal"], "smoke-build.log")
    run(["python", "tools/probe_android_managed.py", str(apk), "--dotnet", args.dotnet,
        "--output", str(folder / "package-runtime")], "managed-startup.log")
    extracted_core = (folder / "package-runtime/Ew4.Core.dll").read_bytes()
    for symbol in args.require_core_symbol:
        assert symbol.encode("ascii") in extracted_core, "APK core implementation missing: " + symbol
    run(["node", "tools/frontend/build.cjs"], "frontend-final.log")
    assert source_index(root) == sources and json.loads((frontend / "build-info.json").read_text()) == build, "Source drift during build"
    assert sha(descriptor) == descriptor_hash, "Content manifest drift"
    for entry in content:
        assert sha(root / "rewrite/data" / entry["path"]) == entry["sha256"], entry["path"]
    checked = json.loads((folder / "package-content.json").read_text())
    result = {"errors": 0, "apk": str(apk.relative_to(root)), "bytes": apk.stat().st_size, "sha256": sha(apk),
        "version": version, "versionCode": code, "runtime": "android-jit", "build": build, "sources": sources,
        "contentFiles": len(content), "packageRecords": len(checked["files"]), "contentManifest": descriptor_hash,
        "signer": signer, "native": native, "androidAssembly": sha(packaged_assembly),
        "requiredCoreSymbols": args.require_core_symbol,
        "scope": "Current worktree, including schema3 configuration split; local APK only, no installation or upload"}
    (folder / "apk-verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    (folder / "SHA256SUMS.txt").write_text(result["sha256"] + "  " + apk.name + "\n", encoding="ascii")
    print(json.dumps({key: result[key] for key in ("errors", "apk", "bytes", "sha256", "build", "contentFiles", "packageRecords")}))


if __name__ == "__main__":
    main()
