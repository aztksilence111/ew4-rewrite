"""Recover the authenticated comparison content and frontend without changing the worktree."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

root=Path(sys.argv[1]).resolve()
folder=root/"build/device-plan-20261024"
target=folder/"frozen-inputs"

def write_descriptor():
    raw=subprocess.check_output(["git","show","HEAD:rewrite/data/runtime-content.json"],cwd=root)
    descriptor=json.loads(raw)
    for entry in descriptor["files"]:
        path=target/"rewrite/data"/entry["path"]
        assert path.stat().st_size==entry["bytes"],entry["path"]
        assert hashlib.sha256(path.read_bytes()).hexdigest()==entry["sha256"],entry["path"]
    (target/"rewrite/data/runtime-content.json").write_bytes(raw)
    return len(descriptor["files"])

if "--descriptor-only" in sys.argv:
    print(json.dumps({"descriptorFiles":write_descriptor(),"errors":0}))
    sys.exit()
pair=json.loads((folder/"android-pair.json").read_text())
apk=root/pair["variants"]["jit"]["apk"]
assert hashlib.sha256(apk.read_bytes()).hexdigest()==pair["variants"]["jit"]["sha256"]
with zipfile.ZipFile(apk) as archive:
    for name in archive.namelist():
        if name.endswith("/"): continue
        if name.startswith("assets/original/"): relative="rewrite/data/original/"+name[len("assets/original/"):]
        elif name.startswith("assets/data/"): relative="rewrite/data/legacy/"+name[len("assets/data/"):]
        elif name.startswith("assets/www/content/"): relative="rewrite/data/client/"+name[len("assets/www/content/"):]
        elif name.startswith("assets/www/"): relative="wwwroot/"+name[len("assets/www/"):]
        else: continue
        output=(target/relative).resolve()
        assert target.resolve() in output.parents
        output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(archive.read(name))
content=json.loads((folder/"baseline-group0/inputs.json").read_text())["content"]
for path,digest in content.items():
    assert hashlib.sha256((target/path).read_bytes()).hexdigest()==digest,path
runtime=json.loads((folder/"combined/source-manifest.json").read_text())["files"]
for entry in runtime:
    source=folder/"combined"/entry["path"]
    output=target/"rewrite"/entry["path"]
    output.parent.mkdir(parents=True,exist_ok=True)
    # Restore exactly the input bytes, including their authenticated newline style.
    text=source.read_text(encoding="utf-8-sig")
    candidates=[text.encode(),text.replace("\n","\r\n").encode()]
    candidates += [b"\xef\xbb\xbf"+value for value in candidates]
    data=next(value for value in candidates if hashlib.sha256(value).hexdigest()==entry["sha256"])
    output.write_bytes(data)
for project in ("Ew4.Mods","Ew4.Web"):
    paths=subprocess.check_output(["git","ls-tree","-r","--name-only","HEAD","rewrite/"+project],cwd=root,text=True).splitlines()
    for path in paths:
        if Path(path).suffix not in {".cs",".csproj"}: continue
        output=target/path;output.parent.mkdir(parents=True,exist_ok=True)
        output.write_bytes(subprocess.check_output(["git","show","HEAD:"+path],cwd=root))
# Its sources and dependencies are already frozen; avoid calling the live frontend builder.
(target/"rewrite/Frontend.Build.targets").write_text("<Project />\n",encoding="utf-8")
shutil.copytree(target/"wwwroot",target/"rewrite/Ew4.Web/wwwroot",dirs_exist_ok=True)
write_descriptor()
(folder/"frozen-inputs.json").write_text(json.dumps({"apk":str(apk.relative_to(root)),"content":content,"runtime":runtime,
    "host":"Core/Persistence/Legacy/Client from the authenticated comparison snapshot; unchanged Web/Mods sources from HEAD; frontend/content from verified JIT APK",
    "scope":"Isolates an unrelated concurrent catalog schema migration; worktree content is preserved"},indent=2)+"\n",encoding="utf-8")
print(json.dumps({"contentFiles":len(content),"runtimeFiles":len(runtime),"errors":0}))
