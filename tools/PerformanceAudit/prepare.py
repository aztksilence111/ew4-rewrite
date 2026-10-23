"""Copy only C# sources/projects to an isolated folder and optionally add scoped probes."""
import argparse
import hashlib
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("root", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--instrument", action="store_true")
args = parser.parse_args()
root, output = args.root.resolve(), args.output.resolve()
if root / "build" not in output.parents:
    raise SystemExit("Output must be a descendant of this repository's build directory")
source_dir = Path(__file__).resolve().parent
projects = ["Ew4.Core", "Ew4.Persistence", "Ew4.Legacy", "Ew4.Client"]
selected = {
    "ClientGame": {"Dispatch", "Action", "View", "Commit", "CommitSession", "InstallSession", "PrepareChange", "Save", "Selection"},
    "ClientGame.AiBatch": {"AiBatch"}, "ClientSaves": {"AutoSave"},
    "ClientHeadquarters": {"StoreAccount"}, "ClientSettlement": {"SettleBattle"},
    "ClientServices": {"AiCombat"},
    "WorldActions": {"Ready", "Reachable", "MovementContext", "Move"},
    "WorldAiSteps": {"PlanStep", "ExecuteStep"},
    "WorldAiUnits": {"PlanNextUnit", "PlanUnit", "ExecuteUnit"},
    "WorldAi": {"StrikeCandidates", "TacticalMoveCandidates", "ExecuteMove", "ThreatStrength"},
    "WorldAiStrategy": {"StrategicCandidates", "ExecuteStrategic"},
    "WorldAiProduction": {"PlanProduction", "ExecuteProduction"},
    "WorldAiCommanders": {"PlanCommander"},
    "HexMap": {"Reachable", "DistancesTo"},
    "WorldBattle": {"Preview", "Attack"}, "WorldTurns": {"EndTurn", "StartFaction"},
    "SessionSave": {"Encode"}, "WorldSave": {"Encode"},
}
pattern = re.compile(r"(?m)^    (?:public|private|internal|protected) [^\n;{}]*?\b(?P<name>\w+)\s*\([^;{}]*?\)\s*\{")
manifest = []
for project in projects:
    for source in (root / "rewrite" / project).rglob("*"):
        relative = source.relative_to(root / "rewrite" / project)
        if not source.is_file() or any(part in {"bin", "obj"} for part in relative.parts):
            continue
        if source.suffix not in {".cs", ".csproj"}:
            continue
        target = output / project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        raw = source.read_bytes()
        text = raw.decode("utf-8-sig")
        probes = []
        if args.instrument and source.suffix == ".cs":
            if source.name == "WorldState.cs":
                text = text.replace('public WorldState Copy() => this with', 'public WorldState Copy() => Ew4.Core.PerfProbe.Measure("WorldState.Copy", () => this with')
                end = text.rfind('\n    };')
                if end < 0:
                    raise RuntimeError("WorldState.Copy end changed")
                text = text[:end] + text[end:].replace('\n    };', '\n    });', 1)
            def insert(match):
                name = match.group("name")
                if name not in selected.get(source.stem, set()) and not (project == "Ew4.Core" and name == "Validate"):
                    return match.group(0)
                owners = re.findall(r"\b(?:class|record|struct)\s+(\w+)", text[:match.start()])
                owner = owners[-1] if owners else source.stem
                label = source.stem + "." + (owner + "." if name == "Validate" else "") + name
                probes.append(label)
                observed = '\n        Ew4.Core.PerfProbe.ObserveWorld(this);' if owner == "WorldState" and name == "Validate" else ''
                return match.group(0) + '\n        using var __auditScope = Ew4.Core.PerfProbe.Enter("' + label + '");' + observed
            text = pattern.sub(insert, text)
            if source.name == "ClientGame.cs":
                old = 'return JsonSerializer.Serialize(new { ok = true, data = result }, Json);'
                if text.count(old) != 1:
                    raise RuntimeError("Dispatch serialization anchor changed")
                text = text.replace(old, 'return Ew4.Core.PerfProbe.Measure("JSON.response", () => JsonSerializer.Serialize(new { ok = true, data = result }, Json));')
                text = text.replace('{ stream.Write(bytes); stream.Flush(true); }', '{ using var __auditIo = Ew4.Core.PerfProbe.Enter("Save.writeFlush"); stream.Write(bytes); stream.Flush(true); }')
                text = text.replace('var bytes = JsonSerializer.SerializeToUtf8Bytes(new ClientSlot', 'var bytes = Ew4.Core.PerfProbe.Measure("JSON.saveEnvelope", () => JsonSerializer.SerializeToUtf8Bytes(new ClientSlot')
                text = text.replace('FactionCountries(current.Session.World).GetValueOrDefault(current.Session.PlayerFaction)), Json);', 'FactionCountries(current.Session.World).GetValueOrDefault(current.Session.PlayerFaction)), Json));')
        target.write_text(text, encoding="utf-8")
        manifest.append({"path": f"{project}/{relative.as_posix()}", "sha256": hashlib.sha256(raw).hexdigest(), "probes": probes})
(output / "Ew4.Core" / "PerfProbe.cs").write_bytes((source_dir / "PerfProbe.cs").read_bytes())
harness = output / "Harness"
harness.mkdir(exist_ok=True)
(harness / "Program.cs").write_bytes((source_dir / "Program.cs").read_bytes())
(harness / "Harness.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Ew4.Client/Ew4.Client.csproj" /></ItemGroup>
</Project>
''', encoding="utf-8")
(output / "source-manifest.json").write_text(json.dumps({"instrumented": args.instrument, "files": manifest}, indent=2), encoding="utf-8")
print(json.dumps({"files": len(manifest), "probes": sum(len(f["probes"]) for f in manifest), "output": str(output)}))
