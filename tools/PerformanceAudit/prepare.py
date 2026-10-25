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
parser.add_argument("--strategy-detail", action="store_true", help="Diagnostic-only goal and rejection counters")
parser.add_argument("--source-root", type=Path, help="Frozen source tree under the repository build directory")
args = parser.parse_args()
root, output = args.root.resolve(), args.output.resolve()
source_root = args.source_root.resolve() if args.source_root else root
if source_root != root and root / "build" not in source_root.parents:
    raise SystemExit("Frozen source root must be under repository build/")
if root / "build" not in output.parents:
    raise SystemExit("Output must be a descendant of this repository's build directory")
source_dir = Path(__file__).resolve().parent
projects = ["Ew4.Core", "Ew4.Persistence", "Ew4.Legacy", "Ew4.Client"]
selected = {
    "ClientGame": {"Dispatch", "Action", "View", "Commit", "CommitSession", "InstallSession", "PrepareChange", "Save", "Selection"},
    "ClientGame.AiBatch": {"AiBatch"}, "ClientSaves": {"AutoSave"},
    "ClientContent": {"Battle"}, "ClientObjectives": {"ObserveObjectives"},
    "PauseObjectives": {"PauseObjectives"}, "MetagameObjectives": {"Observe"},
    "WorldProvocations": {"Captured"},
    "ExtensionScriptRuntime": {"Execute"},
    "ClientHeadquarters": {"StoreAccount"}, "ClientSettlement": {"SettleBattle"},
    "ClientServices": {"AiCombat"},
    "WorldActions": {"Ready", "Reachable", "MovementContext", "Move", "MoveCore", "ProjectMove"},
    "WorldAiSteps": {"PlanStep", "ExecuteStep"},
    "WorldAiUnits": {"PlanNextUnit", "PlanUnit", "ExecuteUnit"},
    "WorldAi": {"StrikeCandidates", "TacticalMoveCandidates", "ExecuteMove", "ThreatStrength"},
    "WorldAiStrategy": {"StrategicCandidates", "ExecuteStrategic"},
    "WorldAiProduction": {"PlanProduction", "ExecuteProduction"},
    "WorldAiCommanders": {"PlanCommander"},
    "TerritorialJurisdictions": {"Policy", "MoveOwners", "TransferDistrict"},
    "WorldDeaths": {"Remove"},
    "HexMap": {"Reachable", "DistancesTo", "DistancesForAdvance"},
    "WorldBattle": {"Preview", "Attack"}, "WorldTurns": {"EndTurn", "StartFaction"},
    "SessionSave": {"Encode"}, "WorldSave": {"Encode", "EncodeValidated"},
}
pattern = re.compile(r"(?m)^    (?:public|private|internal|protected) [^\n;{}]*?\b(?P<name>\w+)\s*\([^;{}]*?\)\s*\{")
manifest = []
for project in projects:
    for source in (source_root / "rewrite" / project).rglob("*"):
        relative = source.relative_to(source_root / "rewrite" / project)
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
            if source.name == "ClientHeadquarters.cs":
                anchor = 'byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);'
                if text.count(anchor) != 1:
                    raise RuntimeError("Account serialization anchor changed")
                text = text.replace(anchor, 'Ew4.Core.PerfProbe.Count("account.serializeWriteFlush");\n        ' + anchor)
                probes.append("account.serializeWriteFlush")
            if source.name == "WorldActions.cs":
                text = text.replace('=> PlanningScope.CanTrust(rules) ? MoveCore(state, state.Revision, unitId, destination, rules, range, true)\n            : Move(state, state.Revision, unitId, destination, rules);', '{ using var __candidate = Ew4.Core.PerfProbe.Enter("WorldActions.ProjectMove"); return PlanningScope.CanTrust(rules) ? MoveCore(state, state.Revision, unitId, destination, rules, range, true)\n            : Move(state, state.Revision, unitId, destination, rules); }')
            if source.name == "WorldState.cs":
                text = text.replace('public WorldState Copy() => this with', 'public WorldState Copy() => Ew4.Core.PerfProbe.Measure("WorldState.Copy", () => this with')
                end = text.rfind('\n    };')
                if end < 0:
                    raise RuntimeError("WorldState.Copy end changed")
                text = text[:end] + text[end:].replace('\n    };', '\n    });', 1)
                probes.append("WorldState.Copy")
            def insert(match):
                name = match.group("name")
                if name not in selected.get(source.stem, set()) and not (project == "Ew4.Core" and name in {"Validate", "ValidateCore"}):
                    return match.group(0)
                owners = re.findall(r"\b(?:class|record|struct)\s+(\w+)", text[:match.start()])
                owner = owners[-1] if owners else source.stem
                label = source.stem + "." + (owner + "." if name in {"Validate", "ValidateCore"} else "") + name
                probes.append(label)
                observed = '\n        Ew4.Core.PerfProbe.ObserveWorld(this);' if owner == "WorldState" and name in {"Validate", "ValidateCore"} else ''
                return match.group(0) + '\n        using var __auditScope = Ew4.Core.PerfProbe.Enter("' + label + '");' + observed
            text = pattern.sub(insert, text)
            if source.name == "WorldState.cs":
                text = text.replace('foreach (var cell in Cells)\n        {', 'foreach (var cell in Cells)\n        {\n            if (PerfProbe.Active) PerfProbe.CellValidationVisits++;')
                unit_validation = 'foreach (var unit in Units)\n        {\n            unit.Combatant.Validate();'
                if text.count(unit_validation) != 1:
                    raise RuntimeError("Primary unit validation loop changed")
                text = text.replace(unit_validation, 'foreach (var unit in Units)\n        {\n            if (PerfProbe.Active) PerfProbe.UnitValidationVisits++;\n            unit.Combatant.Validate();')
            if source.name == "ScenarioRules.cs":
                anchor = 'if (world.DestroyedScenarioUnits.Any(string.IsNullOrWhiteSpace)'
                if text.count(anchor) != 1:
                    raise RuntimeError("Death history validation anchor changed")
                text = text.replace(anchor, 'PerfProbe.Count("history.validationEntries", world.DestroyedScenarioUnits.Count + world.DeadScenarioCommanders.Count);\n        ' + anchor)
            if source.name == "PlanningScope.cs":
                text = text.replace('        current = this;', '        PerfProbe.Count(validated == null ? "scope.disabled" : "scope.enabled");\n        current = this;')
                text = text.replace('current.queries?.GetValue(state, value => new(value))', 'current.queries?.GetValue(state, value => { PerfProbe.Count("query.indexCreated"); return new(value); })')
                text = text.replace('        proofs.Add(next);', '        PerfProbe.Count("candidate.controlledMovement");\n        proofs.Add(next);')
                text = text.replace('if (scope?.validated?.Contains(value) == true) return;', 'if (scope?.validated?.Contains(value) == true) { if (value is WorldState) PerfProbe.Validation("hit"); return; }')
                text = text.replace('        validate();', '        if (value is WorldState) PerfProbe.Validation(scope?.validated == null ? "disabled" : "unseen");\n        validate();')
                text = text.replace('foreach (int i in controlled ? ownership!.Changed : Enumerable.Range(0, cells.Count))\n        {',
                    'foreach (int i in controlled ? ownership!.Changed : Enumerable.Range(0, cells.Count))\n        {\n            PerfProbe.Count("candidate.ownershipVisits");')
                text = text.replace('for (int i = 0; i < cells.Count; i++)\n        {',
                    'for (int i = 0; i < cells.Count; i++)\n        {\n            PerfProbe.Count("candidate.ownershipVisits");')
            if source.name == "WorldEvents.cs":
                text = text.replace('var next = deferredCopy ? state : state.Copy();',
                    'PerfProbe.Count(deferredCopy ? "event.deferredCopy" : "event.eagerCopy");\n        var next = deferredCopy ? state : state.Copy();')
            if source.name == "TerritorialJurisdictions.cs":
                text = text.replace('            var areas = state.Jurisdictions!;',
                    '            PerfProbe.Count("jurisdiction.centerScanCells", state.Cells.Count);\n            PerfProbe.Count("jurisdiction.unitScan", 2L * state.Units.Count);\n            var areas = state.Jurisdictions!;')
                text = text.replace('            occupants = queries?.Occupants ?? state.Units.GroupBy',
                    '            PerfProbe.Count(queries == null ? "jurisdiction.localOccupants" : "jurisdiction.sharedOccupants");\n            if (queries == null) PerfProbe.Count("jurisdiction.unitScan", state.Units.Count);\n            occupants = queries?.Occupants ?? state.Units.GroupBy')
                text = text.replace('            for (int i = 0; i < state.Cells.Count; i++)\n                if (state.Jurisdictions.CellCenters[i] == center',
                    '            PerfProbe.Count("jurisdiction.transferScanCells", state.Cells.Count);\n            for (int i = 0; i < state.Cells.Count; i++)\n                if (state.Jurisdictions.CellCenters[i] == center')
                text = text.replace('            foreach (int i in transfer.DistrictCells(center))',
                    '            PerfProbe.Count("jurisdiction.transferScanCells", transfer.DistrictCells(center).Count);\n            foreach (int i in transfer.DistrictCells(center))')
            if source.name == "PlanningQueries.cs":
                text = text.replace('jurisdictions ??= new(state)', 'jurisdictions ??= PerfProbe.Measure("jurisdiction.topology", () => { PerfProbe.Count("jurisdiction.centerScanCells", state.Cells.Count); return new JurisdictionQueries(state); })')
            if source.name == "HexMap.cs":
                start = text.index('public static IReadOnlyDictionary<int, long> DistancesTo(')
                end = text.index('public static int? EntryCost(', start)
                section = text[start:end]
                section = section.replace('queue.Add((0, cell));', 'queue.Add((0, cell)); PerfProbe.Count("search.enqueue");')
                section = section.replace('queue.Add((total, previous));', 'queue.Add((total, previous)); PerfProbe.Count("search.enqueue");')
                section = section.replace('queue.Remove(current);', 'queue.Remove(current); PerfProbe.Count("search.dequeue");')
                section = section.replace('var current = queue.RemoveMin();', 'var current = queue.RemoveMin(); PerfProbe.Count("search.dequeue");')
                section = section.replace('if (distances[current.Cell] != current.Cost) continue;', 'if (distances[current.Cell] != current.Cost) { PerfProbe.Count("search.stale"); continue; }\n            PerfProbe.Count("search.visit");')
                section = section.replace('if (costs[current.Cell] != current.Cost) continue;', 'if (costs[current.Cell] != current.Cost) { PerfProbe.Count("search.stale"); continue; }\n                PerfProbe.Count("search.visit");')
                text = text[:start] + section + text[end:]
            if source.name == "WorldAiStrategy.cs":
                if args.strategy_detail:
                    anchors = {
                        'var random = new AiRandom(state.AiRandomState);': 'PerfProbe.Distinct("strategy.units", unit.Id);\n        PerfProbe.Count("strategy.behavior." + unit.Behavior);\n        var random = new AiRandom(state.AiRandomState);',
                        'var mover = purchase ? unit with { Transport = true } : unit;': 'PerfProbe.Count("strategy.branches");\n            var mover = purchase ? unit with { Transport = true } : unit;',
                        'var goals = new List<AiStrategyGoalOption>();': 'PerfProbe.Count("strategy.opponents");\n                var goals = new List<AiStrategyGoalOption>();',
                        'var cell = state.Cells[i];\n                    var stationed': 'PerfProbe.Count("strategy.targetSites");\n                    var cell = state.Cells[i];\n                    var stationed',
                        'var endpoints = new List<int>();': 'PerfProbe.Count("strategy.goalAttempts");\n                    var endpoints = new List<int>();',
                        'if (endpoints.Count == 0) continue;': 'if (endpoints.Count == 0) { PerfProbe.Count("strategy.noEndpoints"); continue; }',
                        'if (!endpoints.Any(cell => components[cell] == components[unit.Cell])) continue;': 'if (!endpoints.Any(cell => components[cell] == components[unit.Cell])) { PerfProbe.Count("strategy.disconnected"); continue; }',
                        'if (!distances.TryGetValue(unit.Cell, out long sourceDistance)) continue;': 'if (!distances.TryGetValue(unit.Cell, out long sourceDistance)) { PerfProbe.Count("strategy.sourceUnreachable"); continue; }',
                        'if (chosen < 0) continue;': 'if (chosen < 0) { PerfProbe.Count("strategy.noLanding"); continue; }',
                        'if (purchase && regularDestinations.Contains((goal.Cell, destination))) continue;': 'if (purchase && regularDestinations.Contains((goal.Cell, destination))) { PerfProbe.Count("strategy.transportDuplicate"); continue; }',
                        'if (weight <= 0) continue;': 'if (weight <= 0) { PerfProbe.Count("strategy.nonPositiveWeight"); continue; }',
                        'result.Add(new(new(state.Revision, unitId, destination, goal.Cell, purchase), weight));': 'PerfProbe.Count("strategy.chosen");\n                        result.Add(new(new(state.Revision, unitId, destination, goal.Cell, purchase), weight));',
                    }
                    for anchor, replacement in anchors.items():
                        if text.count(anchor) != 1:
                            raise RuntimeError("Strategy diagnostic anchor changed: " + anchor)
                        text = text.replace(anchor, replacement)
                text = text.replace('var distanceFields = new Dictionary<string, IReadOnlyDictionary<int, long>>(StringComparer.Ordinal);',
                    'var distanceFields = new Dictionary<string, IReadOnlyDictionary<int, long>>(StringComparer.Ordinal);\n            var __distanceHistory = new Dictionary<string, int>(); int __distancePosition = 0;')
                text = text.replace('string fieldKey = string.Join(",", endpoints);', 'string fieldKey = string.Join(",", endpoints);\n                    PerfProbe.DistanceKey(state, unit.Id, purchase, fieldKey, distanceFields.ContainsKey(fieldKey), __distanceHistory, __distancePosition++);')
                text = text.replace('if (distanceFields.Count >= 8) distanceFields.Clear();', 'if (distanceFields.Count >= 8) { PerfProbe.Count("cache.evicted", distanceFields.Count); distanceFields.Clear(); }')
                text = text.replace('if (!distanceFields.TryGetValue(fieldKey, out var distances))', 'PerfProbe.Count("cache.lookup");\n                    if (!distanceFields.TryGetValue(fieldKey, out var distances))')
                text = text.replace('distanceFields[fieldKey] = distances = Movement.DistancesTo', 'PerfProbe.Count("cache.miss");\n                        distanceFields[fieldKey] = distances = Movement.DistancesTo')
            if source.name == "ClientGame.cs":
                old = 'return JsonSerializer.Serialize(new { ok = true, data = result }, Json);'
                current = 'string response = JsonSerializer.Serialize(new { ok = true, data = result }, Json);'
                if text.count(old) + text.count(current) != 1:
                    raise RuntimeError("Dispatch serialization anchor changed")
                text = text.replace(old, 'return Ew4.Core.PerfProbe.Measure("JSON.response", () => JsonSerializer.Serialize(new { ok = true, data = result }, Json));')
                text = text.replace(current, 'string response = Ew4.Core.PerfProbe.Measure("JSON.response", () => JsonSerializer.Serialize(new { ok = true, data = result }, Json));')
                probes.append("JSON.response")
                stream_save = '{ JsonSerializer.Serialize(stream, slot, Json); stream.Flush(true); }'
                if stream_save in text:
                    text = text.replace(stream_save, '{ { using var __auditEnvelope = Ew4.Core.PerfProbe.Enter("Save.serializeWrite"); JsonSerializer.Serialize(stream, slot, Json); } { using var __auditFlush = Ew4.Core.PerfProbe.Enter("Save.flush"); stream.Flush(true); } }')
                    probes.extend(["Save.serializeWrite", "Save.flush"])
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
(output / "driver-manifest.json").write_text(json.dumps({"files": [
    {"path": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
    for p in sorted(source_dir.iterdir()) if p.is_file() and p.suffix in {".py", ".cs"}
]}, indent=2), encoding="utf-8")
print(json.dumps({"files": len(manifest), "probes": sum(len(f["probes"]) for f in manifest), "output": str(output)}))
