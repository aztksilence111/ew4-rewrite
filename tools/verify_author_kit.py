"""Exercise the distributed ZIP through Windows PowerShell 5 and its actual CMD launchers."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


archive_path, scratch, report_path = map(lambda value: Path(value).resolve(), sys.argv[1:4])
signing_apk = Path(sys.argv[4]).resolve() if len(sys.argv) > 4 else None
if scratch.exists():
    raise SystemExit("Preserve existing scratch; choose a new directory")
scratch.mkdir(parents=True)
checks = 0
results = []


def check(condition, label):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(label)


def command(arguments, label, expected=0, environment=None):
    completed = subprocess.run(arguments, capture_output=True, timeout=480, env=environment)
    (scratch / (label + ".stdout.txt")).write_bytes(completed.stdout)
    (scratch / (label + ".stderr.txt")).write_bytes(completed.stderr)
    check(completed.returncode == expected, label + " exit=" + str(completed.returncode))
    results.append({"case": label, "exit": completed.returncode})
    print(label + ": " + str(completed.returncode), flush=True)
    return completed


def cmd_launcher(kit, name, project, label, expected=0):
    arguments = '""' + str(kit / name) + '" '
    if project is not None:
        arguments += '"' + str(project) + '" '
    else:
        arguments += '"" '
    arguments += '--no-pause"'
    return command('cmd.exe /d /s /c ' + arguments, label, expected)


try:
    with zipfile.ZipFile(archive_path) as archive:
        check(archive.testzip() is None, "ZIP CRC")
        names = archive.namelist()
        top = names[0].split("/")[0]
        check(all(name.startswith(top + "/") for name in names), "One archive root")
        immutable = json.loads(archive.read(top + "/files.sha256.json"))["files"]
        for entry in immutable:
            data = archive.read(top + "/" + entry["path"])
            check(len(data) == entry["bytes"] and sha(data) == entry["sha256"], entry["path"])
        extraction = scratch / "中文 作者工具 空格"
        extraction.mkdir()
        for name in names:
            target = (extraction / name).resolve()
            check(target.is_relative_to(extraction), "ZIP containment")
        archive.extractall(extraction)
    kit = extraction / top
    info = json.loads((kit / "author-kit.json").read_text("utf-8-sig"))
    check(bool(info["gameVersion"]) and info["selfContained"], "Version and runtime")
    runtime = json.loads((kit / "Ew4.ModTool.runtimeconfig.json").read_text("utf-8-sig"))
    check("framework" not in runtime["runtimeOptions"] and "frameworks" not in runtime["runtimeOptions"], "No installed .NET framework dependency")
    check((kit / "coreclr.dll").is_file() and (kit / "hostfxr.dll").is_file(), "Bundled native runtime")
    check((kit / "author.ps1").read_bytes().startswith(b"\xef\xbb\xbf"), "PowerShell 5 UTF8 BOM")
    for item in info["runtimeLibraries"]:
        check(sha((kit / item["file"]).read_bytes()) == item["sha256"], "Released library " + item["file"])
    command('cmd.exe /d /s /c ""' + str(kit / "00-check-toolkit.cmd") + '" --no-pause"', "toolkit-integrity")

    bad_root = kit / "examples/needs-repair/optimized-2.0.2"
    bad_project = bad_root / "project"
    if bad_root.exists():
        original_hash = sha((bad_root / "original.ew4mod").read_bytes())
        before_bad = {p.relative_to(bad_project).as_posix(): sha(p.read_bytes()) for p in bad_project.rglob("*") if p.is_file()}
        cmd_launcher(kit, "01-check-project.cmd", bad_project, "reject-27-errors", expected=1)
        problem_logs = list((kit / "_work/logs").glob("*/validation-errors.txt"))
        check(len(problem_logs) == 1, "Invalid project log")
        problem_text = problem_logs[0].read_text("utf-8-sig")
        check(len(problem_text.splitlines()) == 27, "All 27 errors reported")
        check("ew4.unit.fra.light_infantry.0" in problem_text and "ew4.unit.others.light_infantry.2" in problem_text, "Error IDs")
        check(not (bad_project / "mod.json").exists() and not list((kit / "exports").glob("*.ew4mod")), "Invalid project does not export or generate manifest")
        after_bad = {p.relative_to(bad_project).as_posix(): sha(p.read_bytes()) for p in bad_project.rglob("*") if p.is_file()}
        check(before_bad == after_bad and original_hash == sha((bad_root / "original.ew4mod").read_bytes()), "Original and faulty author inputs unchanged")
    else:
        bad_project = scratch / "错误 工程"
        shutil.copytree(kit / "project", bad_project)
        (bad_project / "legacy").mkdir()
        (bad_project / "legacy/units.json").write_text(json.dumps({"units": [{"id": "invalid-unit", "damage_multipliers": [1, 2]}]}), encoding="utf-8")
        cmd_launcher(kit, "01-check-project.cmd", bad_project, "reject-invalid-project", expected=1)
        check(not (bad_project / "mod.json").exists() and not list((kit / "exports").glob("*.ew4mod")), "Invalid project blocked")

    cmd_launcher(kit, "01-check-project.cmd", None, "default-project-check")
    default_manifest = json.loads((kit / "project/mod.json").read_text("utf-8-sig"))
    check(default_manifest["baseRevision"] == info["baseRevision"], "Default project uses matching base")
    check(not list((kit / "exports").glob("*.ew4mod")), "Check action does not export")

    custom = scratch / "自定义 工程"
    shutil.copytree(kit / "project", custom)
    metadata = {"id": "author-kit-check", "name": "作者 测试", "version": "1.0.1"}
    (custom / "author-project.json").write_text(json.dumps(metadata, ensure_ascii=False), encoding="utf-8")
    previous_manifest = (custom / "mod.json").read_bytes()
    source_hash = sha((custom / "rewrite/extension.json").read_bytes())
    cmd_launcher(kit, "02-build-mod.cmd", custom, "custom-project-build")
    outputs = list((kit / "exports").glob("*.ew4mod"))
    check(len(outputs) == 1, "Single successful export")
    exported = outputs[0]
    receipt = json.loads(Path(str(exported) + ".json").read_text("utf-8-sig"))
    check(receipt["ok"] and receipt["sha256"] == sha(exported.read_bytes()), "Export receipt")
    check(sha((custom / "rewrite/extension.json").read_bytes()) == source_hash, "Author resource bytes unchanged")
    backups = list((kit / "_work/logs").glob("*/previous-mod.json"))
    check(any(p.read_bytes() == previous_manifest for p in backups), "Old manifest retained")
    with zipfile.ZipFile(exported) as built:
        manifest = json.loads(built.read("mod.json"))
        check(all(manifest[k] == metadata[k] for k in metadata), "Metadata incl. Chinese name")
        check(manifest["baseRevision"] == info["baseRevision"], "Export base")
        check("author-project.json" not in built.namelist(), "Author-only configuration excluded")
        check(sha(built.read("rewrite/extension.json")) == source_hash, "Export preserves source bytes")
    mods = scratch / "重新导入 mods"
    saves = scratch / "隔离 saves"
    common = ["--mods=" + str(mods), "--saves=" + str(saves)]
    imported = command([str(kit / "Ew4.ModTool.exe"), "import", str(exported)] + common, "reimport-export")
    key = json.loads(imported.stdout.decode("utf-8-sig"))["key"]
    command([str(kit / "Ew4.ModTool.exe"), "activate", key] + common, "activate-export")
    command([str(kit / "Ew4.ModTool.exe"), "activate", "builtin"] + common, "restore-builtin")
    active = json.loads((mods / "active.json").read_text("utf-8-sig"))
    check(active is None, "Returned to builtin")

    command('cmd.exe /d /s /c ""' + str(kit / "00-check-toolkit.cmd") + '" --no-pause"', "toolkit-integrity-after-edit")
    immutable_file = kit / "author-kit.json"
    original_bytes = immutable_file.read_bytes()
    immutable_file.write_bytes(original_bytes + b"\n")
    command('cmd.exe /d /s /c ""' + str(kit / "00-check-toolkit.cmd") + '" --no-pause"', "detect-toolkit-change", expected=1)
    immutable_file.write_bytes(original_bytes)
    signing_verified = False
    if info.get("androidSigning", {}).get("bundled"):
        check(signing_apk is not None, "Bundled signing requires an APK fixture")
        check((kit / "sign-apk.ps1").read_bytes().startswith(b"\xef\xbb\xbf"), "Signing script PowerShell 5 BOM")
        settings = json.loads((kit / "signing/signing-info.json").read_text("utf-8-sig"))
        check(sha((kit / "signing/ew4-release-debug.keystore").read_bytes()) == settings["keystoreSha256"], "Original signing key bytes")
        check(settings["certificateSha256"] == "82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1", "Original signing certificate")
        for filename in ("JSON_PATHS.zh-CN.md", "ANDROID_SIGNING.zh-CN.md"):
            guide = (kit / filename).read_text("utf-8-sig")
            check("assets/rewrite/" in guide or "JSON_PATHS.zh-CN.md" in guide, "APK path documentation " + filename)
        input_hash = sha(signing_apk.read_bytes())
        changed_apk = scratch / "修改 数值 中文.apk"
        parameters = {"schema": 1, "skills": {"ew4.skill.4": {"multiplier": 2}, "ew4.skill.31": {"percent": 25}}}
        skill_project = scratch / "技能 JSON 路径 工程"
        shutil.copytree(kit / "project", skill_project)
        (skill_project / "author-project.json").write_text(json.dumps({"id": "skill-json-paths", "name": "技能参数", "version": "1.0.0"}, ensure_ascii=False), encoding="utf-8")
        (skill_project / "rewrite/definitions").mkdir()
        (skill_project / "rewrite/definitions/legacy-skill-parameters.json").write_text(json.dumps(parameters), encoding="utf-8")
        (skill_project / "rewrite/extension.json").write_text(json.dumps({"schema": 1, "apiVersion": 3, "legacySkillParameters": "rewrite/definitions/legacy-skill-parameters.json"}), encoding="utf-8")
        cmd_launcher(kit, "02-build-mod.cmd", skill_project, "build-documented-skill-json-project")
        skill_exports = [p for p in (kit / "exports").glob("*.ew4mod") if p != exported]
        check(len(skill_exports) == 1, "One skill parameter Mod export")
        with zipfile.ZipFile(skill_exports[0]) as skill_mod:
            check(json.loads(skill_mod.read("rewrite/definitions/legacy-skill-parameters.json")) == parameters, "Documented Mod parameter path")
        with zipfile.ZipFile(signing_apk) as original, zipfile.ZipFile(changed_apk, "w") as modified:
            extension = json.loads(original.read("assets/rewrite/extension.json"))
            extension["legacySkillParameters"] = "rewrite/definitions/legacy-skill-parameters.json"
            for item in original.infolist():
                if item.filename == "assets/rewrite/definitions/legacy-skill-parameters.json":
                    continue
                data = original.read(item)
                if item.filename == "assets/rewrite/extension.json":
                    data = json.dumps(extension).encode("utf-8")
                modified.writestr(item, data)
            modified.writestr("assets/rewrite/definitions/legacy-skill-parameters.json", json.dumps(parameters).encode("utf-8"))
        restricted_env = os.environ.copy()
        restricted_env.pop("JAVA_HOME", None)
        restricted_env.pop("ANDROID_HOME", None)
        restricted_env.pop("ANDROID_SDK_ROOT", None)
        system32 = Path(os.environ["SystemRoot"]) / "System32"
        restricted_env["PATH"] = os.pathsep.join((str(system32), str(system32 / "WindowsPowerShell/v1.0")))
        signing_cmd = 'cmd.exe /d /s /c ""' + str(kit / "03-sign-apk.cmd") + '" "' + str(changed_apk) + '" --no-pause"'
        command(signing_cmd, "resign-modified-apk-without-sdk-java", environment=restricted_env)
        signed_receipts = list((kit / "exports").glob("apk-*/signing-result.json"))
        check(len(signed_receipts) == 1, "One signed APK receipt")
        signed_receipt = json.loads(signed_receipts[0].read_text("utf-8-sig"))
        check(signed_receipt["payloadUnchanged"] and signed_receipt["signatureVerified"] and signed_receipt["pageAlignmentKiB"] == 16, "Signature and payload validation")
        signed_output = Path(signed_receipt["output"])
        check(sha(signed_output.read_bytes()) == signed_receipt["sha256"], "Signed APK checksum")
        check(sha(signing_apk.read_bytes()) == input_hash, "Official APK preserved")
        with zipfile.ZipFile(signed_output) as signed:
            check(json.loads(signed.read("assets/rewrite/definitions/legacy-skill-parameters.json")) == parameters, "APK parameter file preserved")
            check(json.loads(signed.read("assets/rewrite/extension.json")) == extension, "APK existing registrations preserved")
        check(not list(signed_output.parent.glob("*.tmp.apk")), "Signing temporary files removed")
        key_path = kit / "signing/ew4-release-debug.keystore"
        key_bytes = key_path.read_bytes()
        try:
            key_path.write_bytes(key_bytes + b"\0")
            command(signing_cmd, "reject-modified-keystore", expected=1, environment=restricted_env)
        finally:
            key_path.write_bytes(key_bytes)
        check(len(list((kit / "exports").glob("apk-*/signing-result.json"))) == 1, "Rejected key produces no APK")
        signing_verified = True
    report = {"archiveSha256": sha(archive_path.read_bytes()), "archiveBytes": archive_path.stat().st_size,
              "immutableFiles": len(immutable), "checks": checks, "failures": 0, "results": results,
              "windowsPowerShellCmdLaunchers": True, "chineseAndSpacePaths": True,
              "validExportReimportedAndActivated": True, "invalidProjectRejected": True,
              "gameVersion": info["gameVersion"], "sourceCommit": info["sourceCommit"],
              "mobileDeviceExecution": False, "apkSigningVerified": signing_verified,
              "signingWithoutInstalledSdkOrJava": signing_verified}
except Exception as error:
    report = {"checks": checks, "failures": 1, "error": str(error), "results": results}
finally:
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"checks": report["checks"], "failures": report["failures"], "cases": len(results)}), flush=True)
raise SystemExit(1 if report["failures"] else 0)
