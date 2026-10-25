using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class SessionAccountChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Session/account: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " actual=" + error.Code); return; }
            throw new InvalidOperationException("Expected session/account rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        var buildings = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var combat = LegacyCombatCatalog.Read(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json")),
            File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")), File.ReadAllText(Path.Combine(assets, "def_installation.xml")), buildings);
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var itemCatalog = LegacyItemTradingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_item.xml")));
        string metaTables = File.ReadAllText(Path.Combine(root, "reverse/reports/metagame_rule_tables.json"));
        var accounts = LegacyCommanderAccounts.Read(runtime, combat, metaTables);
        using var rosterVectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/roster_vectors.json")));
        foreach (var sample in rosterVectors.RootElement.GetProperty("records").EnumerateArray())
        {
            byte[] raw = sample.GetProperty("raw").EnumerateArray().Select(v => v.GetByte()).ToArray();
            var record = LegacyRosterCommander.Read(raw);
            int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(offset));
            Check(record.LegacyId == Int(0) && record.Learning.Rank == new ProgressionState(Int(4), Int(12))
                && record.Learning.Title == new ProgressionState(Int(8), Int(16)), "native roster identity and progression fields");
            for (int i = 0; i < 7; i++) Check(record.Learning.Abilities[LegacyCommanderAccounts.Abilities[i]] == Int(20 + i * 4), "native roster ability field");
            Check(record.Training == Int(48) && record.Items.SequenceEqual(new[] { Int(52), Int(56) }), "training is separate from skill count and equipment");
            Check(raw.SequenceEqual(record.ToBytes()), "roster byte roundtrip"); raw[0] ^= 0xff;
            Check(!raw.SequenceEqual(record.ToBytes()), "roster input isolation");
        }
        foreach (var sample in rosterVectors.RootElement.GetProperty("commerce").EnumerateArray())
            Check(Math.Max(accounts.TitleCommerce[sample.GetProperty("title").GetInt32()], sample.GetProperty("business").GetInt32()) == sample.GetProperty("expected").GetInt32(), "native title commerce floor");
        int definitions = 0;
        foreach (var pair in runtime.Commanders)
        {
            var owned = accounts.FromDefinition(pair.Key, "owned." + pair.Key);
            var decoded = AccountCommanders.Resolve(owned, accounts.Rules());
            Check(decoded.Stars.OrderBy(p => p.Key).SequenceEqual(pair.Value.Definition.Stars.OrderBy(p => p.Key))
                && decoded.Movement == pair.Value.Definition.Movement && decoded.Skills.SequenceEqual(pair.Value.Definition.Skills), "all initial roster abilities and skills match original");
            byte[] raw = new byte[60];
            int[] fields = [pair.Key, owned.Learning.Rank.Level, owned.Learning.Title.Level, 23, 7,
                .. LegacyCommanderAccounts.Abilities.Select(k => owned.Learning.Abilities[k]), owned.Training,
                .. owned.Items.Select(id => int.Parse(id["ew4.item.".Length..])).Concat(Enumerable.Repeat(-1, 2)).Take(2)];
            for (int i = 0; i < fields.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(i * 4), fields[i]);
            var imported = accounts.Import(LegacyRosterCommander.Read(raw), owned.Id);
            Check(imported.Learning.Rank.Experience == 23 && imported.Learning.Title.Experience == 7 && imported.Items.SequenceEqual(owned.Items), "real-definition roster save restored");
            definitions++;
        }
        using var dispatch = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/commander_dispatch_vectors.json")));
        var emptyAccount = new PlayerAccount { Id = "player", ContentRevision = accounts.SourceRevision, Medals = 20000 };
        foreach (var vector in dispatch.RootElement.GetProperty("inheritance").EnumerateArray())
        {
            var input = vector.GetProperty("inputs");
            int Int(string key) => input.GetProperty(key).GetInt32();
            string[] abilities = LegacyCommanderAccounts.Abilities.ToArray();
            var initial = new CommanderLearningState(new(Int("receiver_rank"), Int("receiver_rank_xp")), new(Int("receiver_title"), Int("receiver_title_xp")),
                abilities.Select((id, i) => (id, v: input.GetProperty("abilities")[i].GetInt32())).ToDictionary(p => p.id, p => p.v));
            var donorLearning = new CommanderLearningState(new(Int("donor_rank"), Int("donor_rank_xp")), new(Int("donor_title"), Int("donor_title_xp")), initial.Abilities);
            var rules = accounts.Rules() with { Definitions = new Dictionary<string, Commander>
                { ["donor.def"] = new() { Id = "donor.def", Skills = input.GetProperty("skills").EnumerateArray().Select(v => Ew4Combat.Skill(v.GetInt32())).ToArray() },
                  ["receiver.def"] = new() { Id = "receiver.def" } } };
            var state = emptyAccount with { Roster = [new("donor", "donor.def", donorLearning, 3, ["ew4.item.0"]), new("receiver", "receiver.def", initial, 4, [])],
                Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 1 } };
            byte[] before = JsonSerializer.SerializeToUtf8Bytes(state);
            var actual = AccountCommanders.Inherit(state, state.Revision, "donor", "receiver", [], rules);
            var receiver = actual.Roster.Single(); var expected = vector.GetProperty("expected").EnumerateArray().Select(v => v.GetInt64()).ToArray();
            Check(new[] { (long)receiver.Learning.Rank.Level, receiver.Learning.Title.Level, receiver.Learning.Rank.Experience, receiver.Learning.Title.Experience }
                .Concat(abilities.Select(id => (long)receiver.Learning.Abilities[id])).SequenceEqual(expected), "native inheritance math inside roster transaction");
            Check(actual.Revision == 1 && actual.Roster.Count == 1 && receiver.Id == "receiver" && receiver.Training == 4
                && actual.Inventory["ew4.item.0"] == 1 && actual.Medals == state.Medals, "inherit removes donor, releases gear, keeps receiver training and wallet");
            Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(state)), "inheritance input unchanged");
        }
        var worldFixture = WorldChecks.Example();
        foreach (var vector in dispatch.RootElement.GetProperty("deployment").EnumerateArray())
        {
            var input = vector.GetProperty("inputs");
            bool Bool(string key) => input.GetProperty(key).GetBoolean();
            int Int(string key) => input.GetProperty(key).GetInt32();
            int[][] rows = input.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
            var candidates = rows.Select((r, i) => new TavernCandidate("offer." + i, "candidate." + i, 8, r[4], new(r[1], r[2], 0), r[3])).ToArray();
            var tavern = new TavernRules { Assignment = new() { Definitions = candidates.ToDictionary(c => c.CommanderId, c => new Commander { Id = c.CommanderId }) },
                Templates = candidates.ToDictionary(c => c.CommanderId, c => new CommanderRuntime { Id = c.CommanderId }) };
            var unit = worldFixture.Units[1] with { CommanderId = Bool("assigned") ? "occupied" : null };
            var world = worldFixture with { Round = Int("round"), Units = Bool("unit") ? [worldFixture.Units[0], unit] : [worldFixture.Units[0]],
                Commanders = Bool("assigned") ? [new() { Id = "occupied", Owner = "winner" }] : [],
                Cells = worldFixture.Cells.Select((c, i) => i != 8 ? c : c with { Owner = Bool("country") ? "winner" : null, BuildingKind = "city", BuildingLevel = 1 }).ToArray(),
                Factions = worldFixture.Factions.Select(f => f.Id != "winner" ? f : f with { Human = !Bool("human"), Resources = new(Int("money"), Int("industry"), 500) }).ToArray() };
            var state = new GameSession { ContentRevision = "session.fixture", PlayerFaction = "winner", World = world,
                Account = emptyAccount with { Medals = Int("medals") }, TavernCandidates = candidates };
            byte[] before = SessionSave.Encode(state);
            bool success = vector.GetProperty("expected").GetProperty("success").GetBoolean();
            string selected = "offer." + Int("index");
            if (!success)
            {
                bool rejected = false;
                try { SessionActions.DeployTavern(state, state.Revision, unit.Id, selected, tavern); } catch (RuleViolation error)
                { Check(error.Code is "session.tavern_unavailable" or "session.tavern_funds", "native rejected deployment has exact modern reason"); rejected = true; }
                Check(rejected && before.SequenceEqual(SessionSave.Encode(state)), "native rejection has no partial charge or offer loss");
            }
            else
            {
                var next = SessionActions.DeployTavern(state, state.Revision, unit.Id, selected, tavern).State;
                int[] row = rows[Int("index")]; bool free = Bool("human");
                Check(next.World.Units.Single(u => u.Id == unit.Id).CommanderId == "candidate." + Int("index") && next.TavernCandidates.Count == rows.Length - 1, "native hire attaches and removes selected candidate");
                Check(next.World.Factions.Single(f => f.Id == "winner").Resources == new Resources(Int("money") - (free ? 0 : row[1]), Int("industry") - (free ? 0 : row[2]), 500)
                    && next.Account.Medals == Int("medals") - (free ? 0 : row[3]), "native battlefield cost and out-of-battle medals committed together");
                Check(next.Revision == 1 && next.World.Revision == 1 && next.Account.Revision == (!free && row[3] > 0 ? 1 : 0), "session/world/account revisions reflect exactly one transaction");
                var binding = new SessionContentBinding(next.ContentRevision, world.ContentRevision, emptyAccount.ContentRevision);
                Check(SessionSave.Encode(SessionSave.Decode(SessionSave.Encode(next), binding)).SequenceEqual(SessionSave.Encode(next)), "native hire plus remaining candidates saved with account atomically");
                Check(before.SequenceEqual(SessionSave.Encode(state)), "native successful deployment does not mutate input");
            }
        }
        var terrain = LegacyTerrainRules.FromXml(File.ReadAllText(Path.Combine(assets, "def_mapterrain.xml")), File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")));
        var maps = new Dictionary<int, LegacyTerrainMap> { [1] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "europe.bin"))), [2] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "america.bin"))) };
        var names = XDocument.Load(Path.Combine(assets, "def_areaname.xml")).Root!.Elements("areaname").ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        int shops = 0, taverns = 0, allCandidates = 0;
        foreach (string path in Directory.EnumerateFiles(assets, "*.btl"))
        {
            var battle = LegacyBattle.Read(File.ReadAllBytes(path));
            var ids = battle.Factions.Select((f, i) => "faction." + i).ToArray();
            var map = LegacyScenarioMap.Import(maps[battle.MapId], battle, terrain, ids, LegacyBuildingCatalog.Kinds, LegacyRecruitmentCatalog.InstallationKinds, names, buildings);
            var imported = LegacyScenarioCommerce.Import(battle, map.Cells, runtime, itemCatalog, accounts, combat, Path.GetFileNameWithoutExtension(path));
            Check(imported.Markets.Count == battle.Markets.Count && imported.Candidates.Count == battle.Taverns.Sum(t => t.Candidates.Count), "all scenario commerce components imported");
            Check(imported.Tavern.Assignment.Definitions.Count == imported.Candidates.Count && imported.Tavern.Templates.Count == imported.Candidates.Count, "all hire templates and commander definitions bound");
            foreach (var candidate in imported.Candidates)
                Check(imported.Tavern.Assignment.Definitions[candidate.CommanderId].Id == candidate.CommanderId && imported.Trading.CommanderCommerce.ContainsKey(candidate.CommanderId), "candidate runtime identity and future commerce ability");
            shops += imported.Markets.Count; taverns += battle.Taverns.Count; allCandidates += imported.Candidates.Count;
        }
        var actor = accounts.FromDefinition(1, "owned.Napoleon");
        var second = accounts.FromDefinition(2, "owned.Davout");
        var accountExample = emptyAccount with { Roster = [actor, second], Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 1 } };
        var accountRules = accounts.Rules();
        var equipped = AccountCommanders.Equip(accountExample, 0, actor.Id, "ew4.item.0", accountRules);
        Check(equipped.Roster[0].Items.SequenceEqual(new[] { "ew4.item.0" }) && equipped.Inventory["ew4.item.0"] == 1 && equipped.Revision == 1, "equip assigns an owned item without losing ownership");
        Reject(() => AccountCommanders.Equip(equipped, 1, second.Id, "ew4.item.0", accountRules), "account.equipment");
        var unequipped = AccountCommanders.Unequip(equipped, 1, actor.Id, "ew4.item.0", accountRules);
        Check(unequipped.Roster[0].Items.Count == 0 && unequipped.Inventory["ew4.item.0"] == 1, "unequip releases copy");
        Reject(() => AccountCommanders.Inherit(accountExample, 0, actor.Id, actor.Id, [], accountRules), "account.inheritance");
        Reject(() => AccountCommanders.Inherit(accountExample, 0, actor.Id, second.Id, [actor.Id], accountRules), "account.inheritance");
        Reject(() => AccountCommanders.Inherit(accountExample, 0, actor.Id, second.Id, [], accountRules with { InheritanceMedals = 20001 }), "account.medals");
        Reject(() => AccountCommanders.Inherit(accountExample, 1, actor.Id, second.Id, [], accountRules), "account.revision");
        var many = emptyAccount with { Roster = Enumerable.Range(0, 100).Select(i => accounts.FromDefinition(1, "instance." + i)).ToArray() };
        Check(AccountCommanders.Inherit(many, 0, "instance.99", "instance.98", [], accountRules).Roster.Count == 99, "no 12-slot roster ceiling");
        Reject(() => AccountCommanders.Inherit(many, 0, "instance.99", "instance.98", [], accountRules with { MaximumRoster = 12 }), "account.roster_capacity");
        var session = new GameSession { ContentRevision = "session.fixture", PlayerFaction = "winner", Account = accountExample,
            World = worldFixture with { Cells = worldFixture.Cells.Select((c, i) => i == 8 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() },
            Markets = [new(8, new("test.shop", [new("star", "ew4.item.53", 2), new("horse", "ew4.item.0")]))],
            TavernCandidates = [new("late", "late.commander", 8, 100, new(0, 0, 0), 1)] };
        var trading = new SessionTradingRules { Items = itemCatalog.Rules(), TitleCommerce = accounts.TitleCommerce };
        var purchased = SessionActions.BuyItem(session, 0, "test.shop", "star", trading).State;
        Check(purchased.Account.Medals == session.Account.Medals - 5 && purchased.Account.Inventory["ew4.item.53"] == 1
            && purchased.World.Revision == session.World.Revision && WorldSave.Encode(purchased.World).SequenceEqual(WorldSave.Encode(session.World)), "shop medals never spend battlefield resources");
        var soldSession = SessionActions.SellItem(purchased, 1, "test.shop", "ew4.item.0", trading).State;
        Check(soldSession.Account.Medals == purchased.Account.Medals + 489 && !soldSession.Account.Inventory.ContainsKey("ew4.item.0"), "session sale uses real item value");
        Reject(() => SessionActions.BuyItem(purchased, 0, "test.shop", "star", trading), "session.revision");
        Reject(() => SessionActions.SellItem(session with { Account = equipped }, 0, "test.shop", "ew4.item.0", trading), "trading.unavailable");
        var inheritedSession = SessionActions.Inherit(session, 0, actor.Id, second.Id, accountRules).State;
        Check(inheritedSession.Account.Roster.Count == 1 && inheritedSession.World.Revision == 0 && inheritedSession.Revision == 1, "inheritance is an account/session transaction");
        Reject(() => SessionActions.Inherit(session with { World = session.World with { Commanders = [new() { Id = actor.Id, Owner = "winner" }] } }, 0, actor.Id, second.Id, accountRules), "account.inheritance");
        var bindings = new SessionContentBinding(session.ContentRevision, session.World.ContentRevision, session.Account.ContentRevision);
        var stationed = session with { World = session.World with
            { Commanders = [new() { Id = actor.Id, Owner = "winner", Title = new(8, 0) }],
                Units = session.World.Units.Select(u => u.Owner == "winner" ? u with { CommanderId = actor.Id } : u).ToArray() },
            Account = session.Account with { Roster = session.Account.Roster.Select(c => c.Id == actor.Id ? c with
                { Learning = c.Learning with { Abilities = c.Learning.Abilities.ToDictionary(p => p.Key, p => p.Key == "commerce" ? 0 : p.Value) } } : c).ToArray() } };
        Check(SessionActions.CommerceLevel(stationed, stationed.Markets[0], trading) == 3, "actual world title supplies missing personal commerce");
        var discounted = SessionActions.BuyItem(stationed with { Account = stationed.Account with { Inventory = new Dictionary<string, long>() } }, 0, "test.shop", "horse", trading).State;
        Check(discounted.Account.Medals == stationed.Account.Medals - (815 - 815 * 12 / 100), "derived title floor reaches real purchase transaction");
        var candidateList = Enumerable.Range(0, 100).Select(i => new TavernCandidate("many." + i, "many.commander." + i, 8, 1, new(0, 0, 0), 0)).ToArray();
        var manyTaverns = new TavernRules { Assignment = new() { Definitions = candidateList.ToDictionary(c => c.CommanderId, c => new Commander { Id = c.CommanderId }) },
            Templates = candidateList.ToDictionary(c => c.CommanderId, c => new CommanderRuntime { Id = c.CommanderId }) };
        var manySession = SessionActions.DeployTavern(session with { TavernCandidates = candidateList }, 0, "unit.2", "many.99", manyTaverns).State;
        Check(manySession.TavernCandidates.Count == 99 && manySession.World.Units.Single(u => u.Id == "unit.2").CommanderId == "many.commander.99", "no five-candidate tavern capacity");
        var transportWorld = session.World with { Factions = session.World.Factions.Select(f => f with { Resources = new(100, 100, 100) }).ToArray() };
        var transportRules = LegacyRecruitmentCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_card.xml"))).Transport();
        var worldChange = WorldSupportActions.BuyTransport(transportWorld, transportWorld.Revision, "unit.2", transportRules);
        var integrated = SessionActions.CommitWorld(session with { World = transportWorld }, 0, worldChange).State;
        Check(integrated.World.Units.Single(u => u.Id == "unit.2").Transport && integrated.Revision == 1
            && JsonSerializer.Serialize(integrated.Account) == JsonSerializer.Serialize(session.Account), "ordinary world action shares session boundary without changing medals");
        Reject(() => SessionActions.CommitWorld(session with { World = transportWorld }, 0, worldChange with { State = worldChange.State with { Revision = 3 } }), "session.world_revision");
        byte[] saved = SessionSave.Encode(session);
        Check(saved.SequenceEqual(SessionSave.Encode(SessionSave.Decode(saved, bindings))), "whole session deterministic roundtrip includes roster, gear, medals, stock and candidates");
        Reject(() => SessionSave.Decode(saved, bindings with { Session = "wrong" }), "save.content_revision");
        Reject(() => SessionSave.Decode(saved, bindings with { World = "wrong" }), "save.content_revision");
        Reject(() => SessionSave.Decode(saved, bindings with { Account = "wrong" }), "save.content_revision");
        Reject(() => SessionSave.Decode(saved, bindings, new(30)), "save.size");
        Reject(() => SessionSave.Decode(saved[..^3], bindings), "save.malformed");
        byte[] Tamper(Action<JsonObject> change, bool checksum = false)
        {
            var node = JsonNode.Parse(saved)!.AsObject(); change(node);
            if (checksum) node["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node["payload"]!.ToJsonString())));
            return Encoding.UTF8.GetBytes(node.ToJsonString());
        }
        Reject(() => SessionSave.Decode(Tamper(n => n["version"] = 99), bindings), "save.version");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["account"]!["medals"] = 99), bindings), "save.checksum");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["account"]!.AsObject().Remove("roster")), bindings), "save.required_field");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["account"]!["roster"]![0]!["learning"]!["rank"]!.AsObject().Remove("experience")), bindings), "save.required_field");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["account"]!["invented"] = 1, true), bindings), "save.malformed");
        string json = Encoding.UTF8.GetString(saved);
        Reject(() => SessionSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"medals\":20000", "\"medals\":20000,\"medals\":1")), bindings), "save.duplicate_property");
        string directory = Path.Combine(root, "rewrite/reports/save-check"); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "session.json");
        SessionSave.WriteAtomic(target, session); SessionSave.WriteAtomic(target, purchased);
        Check(SessionSave.Read(target, bindings).Account.Medals == purchased.Account.Medals && SessionSave.Read(target + ".bak", bindings).Account.Medals == session.Account.Medals, "whole session atomic save and backup");
        Reject(() => SessionSave.WriteAtomic(target, session with { Account = session.Account with { Medals = -1 } }), "account.state");
        Check(SessionSave.Read(target, bindings).Account.Medals == purchased.Account.Medals, "failed session save leaves committed wallet and world together");
        var copy = session.Copy(); ((Dictionary<string, long>)copy.Account.Inventory)["ew4.item.0"] = 999;
        ((Dictionary<string, int>)copy.Account.Roster[0].Learning.Abilities)["commerce"] = 999;
        Check(session.Account.Inventory["ew4.item.0"] == 1 && session.Account.Roster[0].Learning.Abilities["commerce"] != 999, "session copy deeply isolates account state");
        Reject(() => LegacyRosterCommander.Read(new byte[59]), "legacy.roster_record");
        Reject(() => LegacyCommanderAccounts.Read(runtime, combat, metaTables.Replace(combat.NativeSourceHash, new string('0', 64))), "legacy.metagame_source");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/session_accounts.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            definitions, shops, taverns, candidates = allCandidates, nativeInheritanceTransactions = 1000, nativeHireTransactions = 800,
            limitations = "Component imports and atomic session/account actions verified; full native profile migration, commander purchase/cultivation, roster battlefield deployment quota, complete scenario Start/client remain incomplete." }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
