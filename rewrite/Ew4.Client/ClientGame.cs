using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

namespace Ew4.Client;

public sealed record ClientCommand(string Op = "", long Revision = -1, string? Scenario = null, string? Country = null,
    string? Unit = null, string? Target = null, int Cell = -1, string? Offer = null, string? Commander = null, string? Item = null, JsonElement? Tutorial = null, int Group = 0, int Slot = 0, int Mode = 0,
    IReadOnlyList<string>? LocalPlayers = null, string? Tag = null, IReadOnlyList<string>? TaggedCommanders = null, bool Delta = false,
    string? KnownVersion = null, string[]? Units = null, string? Policy = null, bool? AutoBattle = null, int[]? Goals = null,
    int? TaskId = null, string? Kind = null, int? Radius = null, int[]? Line = null, bool? DeployFirst = null);
public sealed record ClientSlot(string Scenario, string? Country, byte[] Session, MovementHistoryState? MovementHistory = null, JsonElement? Tutorial = null,
    int Round = 0, DateTimeOffset? SavedAt = null, IReadOnlyList<string>? LocalPlayers = null, string? DisplayCountry = null,
    IReadOnlyList<int>? TechnologySnapshot = null);

// Both hosts serialize commands here, so rendering cannot bypass turn ownership or stale-state checks.
public sealed partial class ClientGame(ClientContent content, string saveDirectory, Func<DateTimeOffset>? clock = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private LegacyScenarioStart? game;
    private string? country;
    private JsonElement? tutorialCheckpoint;
    private bool storyRestored;
    private readonly MovementHistory movementHistory = new();
    private WorldState? deliveredWorld, deltaBase;
    private long deliveredRevision = -1;
    private Dictionary<string, JsonElement> deliveredUnits = new();
    private Dictionary<string, JsonElement>? pendingUnits;
    private bool viewBuilt;
    private WorldState? movementBase;
    private IReadOnlyList<WorldMovement> executedMovements = [];
    private readonly Dictionary<string, LegacyBattle> requestBattles = new(StringComparer.Ordinal);
    private LegacyBattle RequestBattle(string scenario)
    {
        if (!requestBattles.TryGetValue(scenario, out var battle))
            requestBattles.Add(scenario, battle = content.Battle(scenario));
        return battle;
    }
    private string Slot => Path.Combine(saveDirectory, "session.json");
    public string Dispatch(string path, string body)
    {
        lock (gate)
        {
            try
            {
                deltaBase = null; pendingUnits = null; viewBuilt = false;
                if (path == "/api/action")
                {
                    var request = Command(body);
                    if (request.Delta && request.Op != "state" && request.Revision == deliveredRevision
                        && game?.Session.World.ScenarioId == deliveredWorld?.ScenarioId) deltaBase = deliveredWorld;
                }
                object result = path switch
                {
                    "/api/catalog" => new { build = typeof(ClientGame).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
                        buildVariant = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Ew4.Android")
                            ?.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
                                && a.ConstructorArguments[0].Value as string == "Ew4RuntimeAot")?.ConstructorArguments[1].Value as string is { } aot
                                ? aot == "true" ? "android-aot" : "android-jit" : "web-jit",
                        scenarios = content.Scenarios,
                        chapters = content.Chapters.Select(c => c with { Countries = c.Countries.Select(content.Countries.Canonical).ToArray() }),
                        presentations = content.Presentations.ToDictionary(p => p.Key, p => p.Value with {
                            Countries = p.Value.Countries.Select(content.Countries.Canonical).ToArray() }),
                        unlockedCampaigns = Account().UnlockedCampaigns, campaignStars = Account().CampaignStars,
                        hasSave = File.Exists(Slot), extensionLayouts = content.Extensions.Layouts,
                        commanderDialogues = content.Extensions.CommanderDialogues, countries = content.Countries.Countries,
                        countryAliases = content.Countries.Aliases, formationCounts = content.Extensions.FormationCounts },
                    "/api/country-registry" => new { schema = 1, countries = content.Countries.Countries, aliases = content.Countries.Aliases },
                    "/api/countries" => Countries(Command(body)),
                    "/api/local-players" => LocalPlayers(Command(body)),
                    "/api/headquarters" => Headquarters(),
                    "/api/technology" => Technology(Command(body)),
                    "/api/objectives" => Objectives(Command(body)),
                    "/api/pause-objectives" => PauseObjectives(Command(body)),
                    "/api/deployment" => Deployment(Command(body)),
                    "/api/unit-info" => UnitInfo(Command(body)),
                    "/api/commander" => CommanderAction(Command(body)),
                    "/api/commerce" => Commerce(Command(body)),
                    "/api/items" => Consumables(Command(body)),
                    "/api/exchange" => Exchange(Command(body)),
                    "/api/start" => Start(Command(body)),
                    "/api/leave" => Leave(Command(body)),
                    "/api/action" => Action(Command(body)),
                    "/api/save" => Save(JsonSerializer.Deserialize<ClientCommand>(body, Json) ?? new()),
                    "/api/saves" => Saves(Command(body)),
                    "/api/load" => Load(JsonSerializer.Deserialize<ClientCommand>(body, Json) ?? new()),
                    _ => throw new RuleViolation("client.route", "Unknown command")
                };
                string response = JsonSerializer.Serialize(new { ok = true, data = result }, Json);
                if (viewBuilt) { deliveredWorld = Require().Session.World; deliveredRevision = Require().Session.Revision; deliveredUnits = pendingUnits!; }
                return response;
            }
            catch (Exception error) when (error is RuleViolation or IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or OverflowException)
            {
                return JsonSerializer.Serialize(new { ok = false, code = error is RuleViolation rule ? rule.Code : "client.failure", message = error.Message }, Json);
            }
            finally { requestBattles.Clear(); }
        }
    }
    private static ClientCommand Command(string body) => JsonSerializer.Deserialize<ClientCommand>(body, Json)
        ?? throw new RuleViolation("client.command", "Missing command payload");
    private object Countries(ClientCommand command) => content.Battle(command.Scenario!).Factions
        .Where(f => f.Camp != 4).Select(f => new { id = content.Countries.Canonical(f.CountryId), name = content.CountryName(f.CountryId), camp = f.Camp,
            flag = content.Country(f.CountryId)?.Flag ?? f.Variant, flagImage = content.Country(f.CountryId)?.FlagImage }).ToArray();
    private object Leave(ClientCommand command)
    {
        if (game != null && command.Revision != game.Session.Revision)
            throw new RuleViolation("client.stale", "State changed; refresh the command");
        game = null;
        country = null;
        tutorialCheckpoint = null;
        movementHistory.Clear();
        return new { left = true };
    }
    private object Start(ClientCommand command)
    {
        ValidateLocalSelection(command);
        if (!CampaignProgress.Accessible(content, Account(), command.Scenario!))
            throw new RuleViolation("campaign.locked", "请先完成对应黄圈目标并赢得战役，解锁此隐藏关卡");
        var next = content.Start(command.Scenario!, command.Country, Account(), command.LocalPlayers);
        next = next with { Session = SettleBattle(next.Session with { BattleId = Guid.NewGuid().ToString("N") }) };
        StoreAccount(next.Session.Account);
        game = next; country = command.Country;
        storyRestored = false;
        tutorialCheckpoint = null;
        movementHistory.Reset(next.Session.World);
        AutoSave();
        return View();
    }
    private object Load(ClientCommand command)
    {
        var slot = ReadSlot(SavePath(command.Slot, command.Mode, true));
        var start = content.Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
        GameSession state;
        bool upgradedEquipment = false;
        try
        {
            state = SessionSave.Decode(slot.Session, new(start.Session.ContentRevision, start.Session.World.ContentRevision, start.Session.Account.ContentRevision));
        }
        catch (RuleViolation error) when (error.Code == "save.content_revision" && content.CorrectOriginalEquipment)
        {
            var prior = content.WithoutEquipmentCorrections().Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
            // Authenticate the old content binding before upgrading resolved bonuses.
            state = SessionSave.Decode(slot.Session, new(prior.Session.ContentRevision, prior.Session.World.ContentRevision, prior.Session.Account.ContentRevision));
            state = UpgradeEquipment(state, prior, start);
            upgradedEquipment = true;
        }
        bool upgradedCapacity = JsonSerializer.Serialize(state.World.ArmyCapacity, Json)
            != JsonSerializer.Serialize(start.Session.World.ArmyCapacity, Json);
        state = state with { World = state.World with { ArmyCapacity = start.Session.World.ArmyCapacity?.Copy() } };
        if (start.Session.World.Jurisdictions != null)
        {
            if (state.World.Jurisdictions == null)
                state = state with { World = state.World with { Jurisdictions = start.Session.World.Jurisdictions.Copy() with { CenterCaptureVersion = 0 } } };
            state = state with { World = JurisdictionClaims.UpgradeCapturedDistricts(state.World, start.Session.World) };
        }
        if (state.World.Provocations == null && state.World.Outcome == BattleOutcome.Continue)
            state = state with { World = state.World with { Provocations = start.Session.World.Provocations,
                DiplomacyNeutralCamps = start.Rules.Turns.NeutralCamps.ToArray() } };
        if (state.World.LocalMultiplayer is { WinnerFaction: null, ShareAlliedVictory: false } local
            && start.Session.World.LocalMultiplayer?.ShareAlliedVictory == true)
        {
            state = state with { World = state.World with { LocalMultiplayer = local with {
                ShareAlliedVictory = true,
                InitialObjectiveOwner = local.InitialObjectiveOwner == null ? null : start.Session.World.LocalMultiplayer.InitialObjectiveOwner } } };
            state = state with { World = LocalMultiplayerGame.Resolve(state.World, start.Rules.Turns.Victory) };
        }
        var recovery = WorldTurns.RecoverDefeatedFaction(state.World, start.Rules.Turns);
        bool recovered = !ReferenceEquals(recovery.State, state.World);
        if (recovered) state = SessionActions.CommitWorld(state, state.Revision, recovery).State;
        var profile = Account();
        var unlocks = profile.UnlockedCampaigns.Union(state.Account.UnlockedCampaigns, StringComparer.Ordinal).ToArray();
        if (profile.Revision > state.Account.Revision) state = state with { Account = profile };
        if (!state.Account.UnlockedCampaigns.SequenceEqual(unlocks))
            state = state with { Account = state.Account with { Revision = checked(Math.Max(profile.Revision, state.Account.Revision) + 1), UnlockedCampaigns = unlocks } };
        state = SettleBattle(state);
        state.Validate();
        StoreAccount(state.Account);
        game = start with { Session = state, Notices = recovered ? recovery.Notices : start.Notices }; country = slot.Country;
        storyRestored = StoryPlayback.Current(state.World) != null;
        tutorialCheckpoint = slot.Tutorial;
        if (upgradedEquipment || recovered || upgradedCapacity) movementHistory.Reset(state.World);
        else movementHistory.Load(slot.MovementHistory, state.World);
        AutoSave();
        return View();
    }
    private object Save(ClientCommand command)
    {
        var current = Require();
        string path = SavePath(command.Slot, command.Mode);
        if(command.Mode != 0 && content.Scenarios.Single(s => s.Id == current.Session.World.ScenarioId).Mode != command.Mode)
            throw new RuleViolation("save.mode", "Save mode does not match the current game");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        tutorialCheckpoint = command.Tutorial;
        var slot = new ClientSlot(current.Session.World.ScenarioId, country, SessionSave.Encode(current.Session), movementHistory.Save(), tutorialCheckpoint,
            current.Session.World.Round, DateTimeOffset.Now, current.Session.World.LocalMultiplayer?.HumanFactions,
            FactionCountries(current.Session.World).GetValueOrDefault(current.Session.PlayerFaction), current.TechnologySnapshot);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, slot, Json); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
        return new { saved = true };
    }
    private LegacyScenarioStart Require() => game ?? throw new RuleViolation("client.no_session", "No game is open");
    private object Action(ClientCommand command)
    {
        var current = Require(); var session = current.Session; var world = session.World; var rules = current.Rules;
        if (command.Op == "state") return View();
        if (command.Revision != session.Revision) throw new RuleViolation("client.stale", "State changed; refresh the command");
        if (command.Op is "story_next" or "story_choice" or "story_skip")
        {
            var advanced = StoryPlayback.Advance(world, command.Offer!, command.Tag!, command.Op == "story_choice" ? command.Target : null,
                command.Op == "story_skip", content.Extensions.Bus.Scripts);
            if (!advanced.Notices.Any(n => n.Kind == "story.completed"))
            {
                long revision = checked(session.Revision + 1);
                game = current with { Session = session with { World = advanced.State, Revision = revision }, Notices = advanced.Notices };
                storyRestored = false;
                return new { presentationOnly = true, baseRevision = session.Revision, revision, story = StoryView(advanced.State) };
            }
            if (advanced.State.PendingDialogue == null && advanced.State.Phase is not (TurnPhase.Active or TurnPhase.Finished))
            {
                var resumed = WorldTurns.Resume(advanced.State, advanced.State.Revision, rules.Turns);
                advanced = new(resumed.State, advanced.Notices.Concat(resumed.Notices).ToArray());
            }
            Commit(advanced, autoSave: false); storyRestored = false; AutoSave(); return View();
        }
        if (StoryPlayback.Current(world) != null) throw new RuleViolation("story.pending", "请先完成当前剧情");
        if (command.Op == "ai_batch") return AiBatch();
        var active = world.Factions.Single(f => f.Id == world.ActiveFaction);
        if (command.Op.StartsWith("player_ai_", StringComparison.Ordinal)) return PlayerAiAction(command);
        if (command.Op == "select") return Selection(command.Cell);
        if (command.Op == "event_choice")
        {
            var chosen = ScenarioRules.Choose(world, world.Revision, command.Offer!, command.Target!, content.Extensions.Bus.Scripts);
            if (chosen.State.PendingDialogue == null && chosen.State.Phase is not (TurnPhase.Active or TurnPhase.Finished))
            {
                var resumed = WorldTurns.Resume(chosen.State, chosen.State.Revision, rules.Turns);
                chosen = new(resumed.State, chosen.Notices.Concat(resumed.Notices).ToArray());
            }
            Commit(chosen); AutoSave(); return View();
        }
        if (command.Op == "dialogue")
        {
            if (world.PendingDialogue == null) throw new RuleViolation("client.dialogue", "No dialogue is waiting");
            WorldChange change = world.Phase == TurnPhase.Active
                ? new(WorldEvents.Acknowledge(world, world.PendingDialogue) with { Revision = checked(world.Revision + 1) }, Array.Empty<WorldNotice>())
                : WorldTurns.Resume(world, world.Revision, rules.Turns, world.PendingDialogue);
            Commit(change); return View();
        }
        if (command.Op == "ai")
        {
            if (active.Human && active.Id == session.PlayerFaction && (PlayerAi.Pending(world) || PlayerAi.Automatic(world)))
            {
                var combatView = AdvancePlayerAi();
                return View(world, combat: combatView);
            }
            if (active.Human || world.PendingDialogue != null) throw new RuleViolation("client.ai_turn", "AI does not own the active turn");
            var production = rules.Production[active.Id];
            var plan = WorldAi.PlanStep(world, rules.Ai, production, rules.Turns);
            var combat = AiCombat(world, plan);
            Commit(WorldAi.ExecuteStep(world, plan, rules.Ai, production, rules.Turns, rules.Battle)); return View(world, combat: combat);
        }
        if (active.Id != session.PlayerFaction || !active.Human) throw new RuleViolation("client.player_turn", "Player does not own the active turn");
        if (PlayerAi.Pending(world)) throw new RuleViolation("player_ai.ending", "请先停止执行再操作单位");
        if (command.Op == "use_item")
        {
            var itemRules = content.Catalogs.Items.Rules();
            var change = ConsumableItems.Use(session, session.Revision, command.Unit!, command.Item!, itemRules, rules.Training, content.Extensions.Bus);
            CommitSession(change, command.Unit);
            movementHistory.Reset(game!.Session.World);
            AutoSave();
            return View(suppliedUnit: itemRules.Definitions[command.Item!].Equipment.Effect == "ew4.item_effect.8" ? command.Unit : null);
        }
        if (command.Op == "undo")
        {
            var undo = movementHistory.Undo(world, command.Unit);
            string unit = undo.Change.Notices[0].Subject;
            Commit(undo.Change);
            return View(null, new[] { new { unit, path = undo.Path, instant = true } });
        }
        if (command.Op == "deploy")
        {
            var change = RosterDeployment.Deploy(session, session.Revision, command.Unit!, command.Commander!, rules.Roster);
            CommitSession(change, command.Unit);
            AutoSave();
            return View();
        }
        if (command.Op == "hire")
        {
            var hired = SessionActions.DeployTavern(session, session.Revision, command.Unit!, command.Offer!, rules.Tavern);
            CommitSession(hired, command.Unit);
            AutoSave();
            return View();
        }
        var preview = command.Op == "attack" ? WorldBattle.Preview(world, world.Revision, command.Unit!, command.Target!, rules.Battle) : null;
        WorldChange action = command.Op switch
        {
            "skill" => WorldSkillActions.Execute(world, world.Revision, command.Unit!, command.Offer!, command.Target, content.Extensions.Bus, rules.Movement,
                command.Cell < 0 ? null : command.Cell),
            "move" => WorldActions.Move(world, world.Revision, command.Unit!, command.Cell, rules.Movement),
            "disband" => WorldDisband.Execute(world, world.Revision, command.Unit!, DisbandConfiguration()),
            "attack" => WorldBattle.Attack(world, world.Revision, command.Unit!, command.Target!, rules.Battle),
            "end" => PlayerAi.BeginEnd(world, world.Revision, rules.Turns, rules.Ai),
            "train" => WorldImprovements.Train(world, world.Revision, command.Unit!, rules.Training),
            "upgrade" => WorldImprovements.UpgradeBuilding(world, world.Revision, command.Cell, rules.Upgrades),
            "transport" => WorldSupportActions.BuyTransport(world, world.Revision, command.Unit!, rules.Transport),
            "installation" => WorldSupportActions.BuildInstallation(world, world.Revision, command.Unit!, rules.Installations[command.Offer!]),
            "recruit" => WorldActions.Recruit(world, world.Revision, command.Cell, "client.unit." + world.Revision, rules.Recruitment[active.Id][command.Offer!]),
            _ => throw new RuleViolation("client.command", "Unknown action")
        };
        int[]? movedPath = command.Op == "move" ? action.Movements.Single(m => m.Unit == command.Unit).Path.ToArray() : null;
        if (command.Op != "end") action = action with { State = PlayerAi.MarkAction(action.State,
            command.Unit ?? world.Units.FirstOrDefault(u => u.Owner == active.Id && u.Cell == command.Cell)?.Id) };
        Commit(action);
        if (movedPath != null)
            movementHistory.Record(world, game!.Session.World, command.Unit!, movedPath, game.Notices);
        if (command.Op == "attack") movementHistory.Attack(command.Unit!);
        if (command.Op == "skill") movementHistory.Reset(action.State);
        if (command.Op == "disband") { movementHistory.Forget(command.Unit!); AutoSave(); }
        return View(world, combat: preview == null ? null : new { attacker = preview.Attacker, defender = preview.Defender, counterattack = preview.Main.Counterattack,
            damage = preview.Main.DefenderDamage, counterDamage = preview.Main.AttackerDamage,
            critical = preview.Main.OffensiveProc && !preview.Main.DefensiveProc, counterCritical = false,
            splashTarget = preview.SplashTarget, splashDamage = preview.SplashDamage });
    }
    private WorldChange PrepareChange(WorldChange change)
    {
        var current = Require();
        var scenarioChange = ScenarioRules.Evaluate(change.State, content.Extensions.Bus.Scripts);
        change = new(scenarioChange.State, change.Notices.Concat(scenarioChange.Notices).ToArray());
        bool dispatchEvents = content.Extensions.Bus.Skills.Count > 0 || content.Extensions.Bus.Statuses.Count > 0;
        if (dispatchEvents || current.Rules.Turns.Victory.Custom != null || change.State.ScenarioEvents.Count > 0)
        {
            var world = change.State;
            if (dispatchEvents)
            {
                foreach (string id in world.Units.Where(u => !current.Session.World.Units.Any(old => old.Id == u.Id)).Select(u => u.Id).ToArray())
                    world = content.Extensions.Bus.Dispatch(world, "unit.recruited", id, new Ew4Random(world.RandomState));
                foreach (var notice in change.Notices.Where(n => n.Kind is "unit.destroyed" or "battle.damage"))
                    if (current.Session.World.Units.FirstOrDefault(u => u.Id == notice.Subject) is UnitState before)
                        world = content.Extensions.Bus.Dispatch(world, notice.Kind == "unit.destroyed" ? "unit.destroyed" : "unit.damaged",
                            before.Id, new Ew4Random(world.RandomState), eventSource: before);
            }
            var scenarioEvents = ScenarioRules.Evaluate(world, content.Extensions.Bus.Scripts);
            world = scenarioEvents.State;
            change = change with { Notices = change.Notices.Concat(scenarioEvents.Notices).ToArray() };
            if ((current.Rules.Turns.Victory.Custom != null || world.ScenarioEvents.Count > 0) && world.Outcome == BattleOutcome.Continue && world.PendingDialogue == null)
            {
                var outcome = WorldVictory.Evaluate(world, current.Rules.Turns.PlayerCamp!, current.Rules.Turns.Victory, current.Rules.Turns.NeutralCamps);
                if (outcome != BattleOutcome.Continue) world = world with { Outcome = outcome, Phase = TurnPhase.Finished };
            }
            change = change with { State = world };
        }
        var recovery = WorldTurns.RecoverDefeatedFaction(change.State, current.Rules.Turns);
        if (!ReferenceEquals(recovery.State, change.State))
            change = change with { State = recovery.State, Notices = change.Notices.Concat(recovery.Notices).ToArray() };
        return change with { State = PlayerAi.Reconcile(LocalMultiplayerGame.Resolve(change.State, current.Rules.Turns.Victory))
            with { Revision = checked(current.Session.World.Revision + 1) } };
    }
    private void Commit(WorldChange change, bool autoSave = true)
    {
        var current = Require();
        movementBase = current.Session.World; executedMovements = change.Movements;
        change = PrepareChange(change);
        var session = SessionActions.CommitWorld(current.Session, current.Session.Revision, change);
        InstallSession(current, session.State, change.Notices, autoSave);
    }
    private void CommitSession(SessionChange change, string? playerUnit = null)
    {
        var current = Require();
        if (playerUnit != null) change = change with { State = change.State with { World = PlayerAi.MarkAction(change.State.World, playerUnit) } };
        var world = PrepareChange(new(change.State.World, change.Notices));
        var next = SessionActions.SyncPlayer(change.State with { World = world.State });
        next.Validate();
        InstallSession(current, next, world.Notices);
    }
    private void InstallSession(LegacyScenarioStart current, GameSession next, IReadOnlyList<WorldNotice> notices, bool autoSave = true)
    {
        var settled = SettleBattle(next, current.Session.World);
        StoreAccount(settled.Account);
        game = current with { Session = settled, Notices = notices };
        movementHistory.Observe(next.World);
        if (MovementHistory.EventTriggered(current.Session.World, next.World, notices)
            || next.World.ScenarioChoices.Count != current.Session.World.ScenarioChoices.Count
            || next.PlayerFaction != current.Session.PlayerFaction) movementHistory.Invalidate(next.World);
        if(autoSave && (next.World.Round != current.Session.World.Round || next.World.ActiveFaction != current.Session.World.ActiveFaction || next.World.Outcome != current.Session.World.Outcome
            || next.PlayerFaction != current.Session.PlayerFaction
            || next.World.PendingDialogue != current.Session.World.PendingDialogue && next.World.ScenarioEvents.Count > 0))
            AutoSave();
    }
    private object Selection(int cell)
    {
        var current = Require(); var world = current.Session.World;
        if (cell < 0 || cell >= world.Cells.Count) throw new RuleViolation("map.cell", "Cell outside map");
        bool ready = world.Phase == TurnPhase.Active && world.PendingDialogue == null && world.ActiveFaction == current.Session.PlayerFaction;
        var unit = world.Units.FirstOrDefault(u => u.Cell == cell);
        var offers = ready ? current.Rules.Recruitment[world.ActiveFaction].Values.Where(o => WorldActions.CanRecruit(world, cell, o))
            .Select(o => new { o.Id, profile = o.Template?.Profile.Id, o.Cost }).ToArray() : [];
        var occupied = world.Units.Select(u => u.Cell).ToHashSet();
        var range = ready && unit?.Owner == world.ActiveFaction
            ? WorldActions.Reachable(world, unit.Id, current.Rules.Movement).Keys.Where(c => !occupied.Contains(c)).ToArray() : [];
        var targets = ready && unit?.Owner == world.ActiveFaction ? AttackTargets(world, unit).Select(u => u.Cell).ToArray() : [];
        var disbandRules = DisbandConfiguration();
        var refund = ready && unit != null && WorldDisband.CanDisband(world, unit.Id, disbandRules)
            ? WorldDisband.Refund(unit, disbandRules.RecruitmentCosts[unit.Combatant.Profile.Id]) : null;
        var trainingQuote = ready && unit?.Owner == world.ActiveFaction
            ? ActionQuote(() => WorldImprovements.TrainingCost(world, unit.Id, current.Rules.Training)) : null;
        var upgradeQuote = ready ? ActionQuote(() => WorldImprovements.BuildingUpgradeCost(world, cell, current.Rules.Upgrades)) : null;
        var transportQuote = ready && unit?.Owner == world.ActiveFaction
            ? ActionQuote(() => WorldSupportActions.TransportCost(world, unit.Id, current.Rules.Transport)) : null;
        var installationOffers = ready && unit?.Owner == world.ActiveFaction ? current.Rules.Installations
            .Select(p => new { id = p.Key, kind = p.Value.Kind,
                name = content.Catalogs.Cards.Cards.Values.Single(c => c.Id == p.Key).Name,
                quote = ActionQuote(() => WorldSupportActions.InstallationCost(world, unit.Id, p.Value)) })
            .Where(p => p.quote != null).ToArray() : [];
        string? disbandWarning = refund != null && unit!.Combatant.Profile.Category == "fort"
            && current.Rules.Recruitment[world.ActiveFaction].Values.Any(o => o.Template?.Profile.Category == "fort"
                && o.PerTurnLimit.HasValue && world.Factions.Single(f => f.Id == world.ActiveFaction)
                    .TurnUses.GetValueOrDefault(o.TurnGroup) >= o.PerTurnLimit.Value)
            ? "解散后本回合内不能再建造要塞。" : null;
        return new { cell, range, targets, offers, disband = refund, disbandWarning,
            actionQuotes = new { train = trainingQuote, upgrade = upgradeQuote, transport = transportQuote },
            items = ready && unit?.Owner == world.ActiveFaction && unit.Combatant.ConstructionTurns == 0,
            skills = ready && unit?.Owner == world.ActiveFaction ? ActiveSkills(world, unit) : [],
            tavern = ready && world.Cells[cell].Owner == world.ActiveFaction && Service(cell) == "bar",
            shop = ready && world.Cells[cell].Owner == world.ActiveFaction && Service(cell) == "shop",
            market = ready && world.Cells[cell].Owner == world.ActiveFaction && Service(cell) == "trade",
            upgrade = ready && WorldImprovements.CanUpgradeBuilding(world, cell, current.Rules.Upgrades),
            deploy = ready && unit?.Owner == world.ActiveFaction && unit.CommanderId == null && RosterDeployment.IsDeploymentLocation(world, unit),
            unit = unit?.Id, installationOffers, installations = installationOffers.Where(p => p.quote!.Affordable).Select(p => p.id).ToArray(),
            undo = ready && unit != null && movementHistory.Available(world, unit.Id) != null,
            transport = ready && unit?.Owner == world.ActiveFaction && WorldSupportActions.CanBuyTransport(world, unit.Id, current.Rules.Transport) };
    }
    private static IEnumerable<UnitState> AttackTargets(WorldState world, UnitState unit)
    {
        var map = new HexMap(world.Width, world.Height);
        return world.Units.Where(target => !WorldDiplomacy.Allied(world, target.Owner, unit.Owner)
            && AttackLegality.Check(true, true, unit.Combatant.ConstructionTurns, unit.Acted, unit.Morale.Effective,
                map.Distance(unit.Cell, target.Cell), unit.Combatant.Profile.MinimumRange, unit.Combatant.Profile.MaximumRange) == AttackBlock.None);
    }
    private object View(WorldState? previous = null, object? movementOverride = null, object? combat = null, WorldState? cellDeltaFrom = null, string? suppliedUnit = null)
    {
        var current = Require(); var session = current.Session; var world = session.World;
        cellDeltaFrom ??= deltaBase;
        var battle = RequestBattle(world.ScenarioId);
        var factionColors = battle.Factions.Select((source, index) => (source, index))
            .ToDictionary(p => LegacyScenarioInitialization.FactionId(world.ScenarioId, p.index, p.source), p => p.source.Color);
        var factionSources = battle.Factions.Select((source, index) => (source, index))
            .ToDictionary(p => LegacyScenarioInitialization.FactionId(world.ScenarioId, p.index, p.source), p => p.source);
        var unitVariants = factionSources.ToDictionary(p => p.Key, p => p.Value.Variant);
        var countryIds = FactionCountries(world);
        var visualVariants = factionSources.ToDictionary(p => p.Key, p => content.Country(p.Value.CountryId)?.VisualVariant
            ?? content.Country(p.Value.CountryId)?.UnitVariant ?? p.Value.Variant);
        foreach (var (id, source) in factionSources)
            unitVariants[id] = content.Country(source.CountryId)?.UnitVariant ?? source.Variant;
        foreach (var creation in ScenarioFactions.Creations(world.ScenarioEvents))
        {
            string? templateId = ScenarioFactions.Template(world, creation);
            unitVariants[creation.Faction] = creation.UnitVariant ?? (templateId == null ? "others" : unitVariants[templateId]);
            visualVariants[creation.Faction] = content.Country(countryIds[creation.Faction])?.VisualVariant
                ?? (creation.UnitVariant == null && templateId != null ? visualVariants[templateId] : unitVariants[creation.Faction]);
            if (ScenarioFactions.Template(world, creation) is not string template) continue;
            factionSources.Add(creation.Faction, factionSources[template]);
            factionColors.Add(creation.Faction, factionColors[template]);
        }
        var sites = battle.EffectiveSites.ToDictionary(s => (int)s.Cell);
        var economicCommanders = world.Units.Where(u => u.CommanderId != null).GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.First());
        var previousEconomics = cellDeltaFrom?.Units.Where(u => u.CommanderId != null).GroupBy(u => u.Cell)
            .ToDictionary(g => g.Key, g => g.First().Support.IncomeMultiplier);
        var originalUnits = battle.Armies.Select((army, index) => (army, id: world.ScenarioId + ".unit." + index))
            .ToDictionary(p => p.id, p => p.army);
        string? CommanderDefinitionId(UnitState unit) => session.Account.Roster.FirstOrDefault(c => c.Id == unit.CommanderId)?.DefinitionId
            ?? (unit.CommanderId == unit.Id + ".commander" && originalUnits.TryGetValue(unit.Id, out var army)
                && content.Catalogs.Runtime.Commanders.TryGetValue(army.Commander, out var definition) ? definition.Definition.Id : null);
        var movements = new List<object>();
        if (previous != null)
        {
            if (ReferenceEquals(previous, movementBase)) movements.AddRange(executedMovements);
            var before = previous.Units.ToDictionary(u => u.Id);
            foreach (var unit in world.Units)
                if (before.TryGetValue(unit.Id, out var old) && old.Cell != unit.Cell
                    && !executedMovements.Any(m => ReferenceEquals(previous, movementBase) && m.Unit == unit.Id)
                    && WorldActions.Reachable(previous, old.Id, current.Rules.Movement).TryGetValue(unit.Cell, out var reachable))
                    movements.Add(new { unit = unit.Id, path = reachable.Path });
        }
        string? BuildingImage(int index, CellState cell)
        {
            if (cell.BuildingKind == null) return null;
            if (cell.BuildingKind == "city" && sites.TryGetValue(index, out var site)
                && content.Catalogs.Buildings.Capitals.TryGetValue(site.State8, out var capital)) return capital.Image;
            var definition = content.Catalogs.Buildings.Definitions.Values.SingleOrDefault(d => d.Kind == cell.BuildingKind);
            return definition?.Levels.ElementAtOrDefault(Math.Max(0, cell.BuildingLevel - 1))?.Image;
        }
        object TileInfo(int index, CellState cell)
        {
            var definition = content.Catalogs.Buildings.Definitions.Values.SingleOrDefault(d => d.Kind == cell.BuildingKind);
            var level = definition?.Levels.ElementAtOrDefault(Math.Max(0, cell.BuildingLevel - 1));
            var income = WorldTurns.SiteIncome(cell, index, current.Rules.Turns, economicCommanders.GetValueOrDefault(index));
            content.Catalogs.Combat.Context.TerrainDefense.TryGetValue(cell.DefenseTerrain, out var penalties);
            return new { income, defense = level?.DefensePercent ?? 0, penalties,
                armyCapacity = WorldArmyCapacity.BuildingCapacity(world, cell) };
        }
        var navigationMoves = world.Phase == TurnPhase.Active && world.ActiveFaction == session.PlayerFaction
            && world.PendingDialogue == null
            ? WorldActions.MovableUnits(world, session.PlayerFaction, current.Rules.Movement).ToHashSet()
            : new HashSet<string>();
        bool CanAttack(UnitState unit) => unit.Owner == world.ActiveFaction && world.Phase == TurnPhase.Active
            && world.PendingDialogue == null && AttackTargets(world, unit).Any();
        object? DialoguePresentation()
        {
            if (world.PendingDialogue != null && WorldProvocations.Dialogue(world, world.PendingDialogue) is { } diplomatic)
            {
                var diplomaticSpeaker = world.Commanders.Where(c => c.Owner == diplomatic.Faction && !c.Withdrawn
                    && world.Units.Any(u => u.CommanderId == c.Id)).OrderByDescending(c => c.Title.Level)
                    .ThenByDescending(c => c.Rank.Level).FirstOrDefault();
                string? diplomaticPortrait = diplomaticSpeaker != null && current.Rules.Commanders.Definitions.TryGetValue(diplomaticSpeaker.Id, out var diplomaticDefinition)
                    ? diplomaticDefinition.DisplayName : null;
                return new { id = world.PendingDialogue, text = content.Label($"desc_{diplomatic.Kind} {diplomatic.Variant}"), left = true,
                    portrait = diplomaticPortrait == null ? null : diplomaticPortrait + ".png", name = diplomaticPortrait == null
                        ? world.Factions.Single(f => f.Id == diplomatic.Faction).Name : content.Label("name_" + diplomaticPortrait) };
            }
            if (world.PendingDialogue == null || !current.Rules.Dialogues.TryGetValue(world.PendingDialogue, out var entry)) return null;
            string? portrait = content.Catalogs.Runtime.Commanders.TryGetValue(entry.Commander, out var speaker)
                ? speaker.Definition.DisplayName : null;
            return new { id = entry.Id, text = entry.Text, left = entry.Left, portrait = portrait == null ? null : portrait + ".png",
                name = portrait == null ? "" : content.Label(portrait) };
        }
        object CellView(CellState c, int i) => new { c.Owner, c.Name, c.BuildingKind, c.BuildingLevel, c.Terrain.Active, c.Terrain.Sea, c.Objective, c.InstallationKind, c.Environment,
            geographyType = GeographyCatalog.Type(c.Terrain),
            geographyName = GeographyCatalog.Type(c.Terrain) == "none" ? null : content.Extensions.Geography.Visual(GeographyCatalog.Type(c.Terrain)).Name,
            geographySprite = GeographyCatalog.Type(c.Terrain) == "none" ? null : content.Extensions.Geography.Visual(GeographyCatalog.Type(c.Terrain)).Sprites[i % content.Extensions.Geography.Visual(GeographyCatalog.Type(c.Terrain)).Sprites.Length],
            bridgeEdges = c.Terrain.BridgeEdges,
            bridgeStyle0 = sites.TryGetValue(i, out var bridge) ? bridge.BridgeStyle0 : 0,
            bridgeStyle1 = bridge?.BridgeStyle1 ?? 0,
            bridgeStyle2 = bridge?.BridgeStyle2 ?? 0,
            objectiveMarker = c.Objective ? 1 : sites.TryGetValue(i, out var markedSite) && markedSite.Marker == 2 ? 2 : 0,
            jurisdictionCenter = world.Jurisdictions?.CellCenters[i] is int center && center >= 0 ? center : (int?)null,
            tileInfo = TileInfo(i, c),
            facility = sites.TryGetValue(i, out var service) ? service.State6 switch { 1 => "trade", 2 => "shop", 3 => "bar", _ => (string?)null } : null,
            buildingImage = BuildingImage(i, c), buildingStyle = sites.TryGetValue(i, out var site) ? site.State7 : 1 };
        var unitViews = world.Units.Select(u => JsonSerializer.SerializeToElement(new { u.Id, u.Owner, u.Cell, profile = u.Combatant.Profile.Id, category = u.Combatant.Profile.Category,
            skills = SkillViews(u.Combatant), statuses = StatusViews(u.Combatant), traits = u.Combatant.Profile.Traits.Where(content.Extensions.Bus.Skills.ContainsKey)
                .Select(id => SkillView(new SkillSlot(id, "trait." + id))).ToArray(),
            health = u.Combatant.Health, maxHealth = u.Combatant.Profile.MaxHealth, u.Combatant.ConstructionTurns, u.RemainingMovement, u.Acted, u.CommanderId, u.Transport, u.Burning, u.Objective,
            aiControlled = PlayerAi.Managed(world, u), aiPolicy = PlayerAi.Order(u).Policy, aiGoal = PlayerAi.Order(u).Goal, aiGoals = PlayerAi.Targets(PlayerAi.Order(u)), aiTask = PlayerAi.Order(u).TaskId,
            objectiveMarker = u.Objective ? 1 : originalUnits.TryGetValue(u.Id, out var markedUnit) && markedUnit.Objective == 2 ? 2 : 0,
            canMove = u.Combatant.Profile.Category != "fort" && u.RemainingMovement > 0 && u.Morale.Effective != -3 && u.Combatant.ConstructionTurns == 0,
            canAttack = CanAttack(u), canNavigateMove = navigationMoves.Contains(u.Id), veterancy = u.Veterancy, morale = u.Morale.Effective,
            facing = originalUnits.TryGetValue(u.Id, out var source) ? source.Facing : 1,
            commanderDefinitionId = CommanderDefinitionId(u),
            commanderImage = session.Account.Roster.FirstOrDefault(c => c.Id == u.CommanderId) is OwnedCommander owned
                ? content.Catalogs.Accounts.Rules().Definitions[owned.DefinitionId].DisplayName
                : u.CommanderId == u.Id + ".commander" && originalUnits.TryGetValue(u.Id, out var commanded)
                && content.Catalogs.Runtime.Commanders.TryGetValue(commanded.Commander, out var commander) ? commander.Definition.DisplayName : null }, Json)).ToArray();
        pendingUnits = unitViews.ToDictionary(u => u.GetProperty("id").GetString()!, StringComparer.Ordinal);
        viewBuilt = true;
        return new
        {
            session.Revision, scenario = world.ScenarioId, map = content.MapNames[battle.MapId], world.Width, world.Height,
            stageIntro = StageIntro(world.ScenarioId), session.BattleId,
            battleResult = BattleResultView(session), story = StoryView(world),
            tutorialCheckpoint,
            cameraBounds = new { left = battle.Left * 64 - 52, top = (battle.Top - 1) * 54 - 20,
                width = battle.Width * 64 + 72, height = (battle.Height - 1) * 54 + 112 },
            movements = movementOverride ?? movements,
            combat,
            suppliedUnit,
            recruited = previous == null ? [] : world.Units.Where(u => !previous.Units.Any(old => old.Id == u.Id)).Select(u => u.Id).ToArray(),
            undoUnit = movementHistory.Available(world)?.Unit,
            world.Round, phase = world.Phase.ToString(), outcome = world.Outcome.ToString(), world.ActiveFaction, session.PlayerFaction,
            startCountry = country, ending = world.ScenarioEnding, victoryRules = world.ScenarioVictory ?? current.Rules.Turns.Victory.Custom,
            localMultiplayer = LocalMultiplayerView(session),
            dialogue = world.PendingDialogue == null ? null : StoryPlayback.Current(world) != null ? "story" : WorldProvocations.Dialogue(world, world.PendingDialogue) is { } diplomatic
                ? content.Label($"desc_{diplomatic.Kind} {diplomatic.Variant}")
                : current.Rules.Dialogues.TryGetValue(world.PendingDialogue, out var dialogue) ? dialogue.Text : world.PendingDialogue,
            dialoguePresentation = DialoguePresentation(),
            scenarioPopup = ScenarioPopupView(world),
            mapColors = MapColorsPresentation.Resolve(content.Extensions.MapColors, world.ScenarioId),
            playerAi = world.Factions.Single(f => f.Id == session.PlayerFaction).PlayerAi ?? new PlayerAiControl(),
            aiPresentation = content.Extensions.AiControl,
            medals = session.Account.Medals,
            factions = world.Factions.Select(f => new { f.Id, f.Name, f.Camp, f.Human, f.Defeated, f.Resources,
                unitCount = world.Units.Count(u => u.Owner == f.Id), unitLimit = WorldArmyCapacity.Limit(world, f.Id),
                relation = WorldDiplomacy.Relation(world, session.PlayerFaction, f.Id, current.Rules.Turns.NeutralCamps),
                neutral = WorldDiplomacy.NeutralTo(world, f.Id, session.PlayerFaction, current.Rules.Turns.NeutralCamps),
                country = countryIds.GetValueOrDefault(f.Id) ?? f.Id,
                unitVariant = unitVariants.GetValueOrDefault(f.Id),
                visualVariant = visualVariants.GetValueOrDefault(f.Id),
                flag = f.Appearance?.Flag ?? (f.Appearance?.FlagImage == null ? content.Country(countryIds[f.Id])?.Flag ?? factionSources.GetValueOrDefault(f.Id)?.Variant : null),
                flagImage = f.Appearance?.FlagImage ?? (f.Appearance?.Flag == null ? content.Country(countryIds[f.Id])?.FlagImage : null),
                color = (f.Appearance?.Color ?? content.Country(countryIds[f.Id])?.Color) is string color ? ScenarioFactions.PackedColor(color) : factionColors.GetValueOrDefault(f.Id) }),
            baseRevision = deltaBase == null ? (long?)null : deliveredRevision,
            cells = cellDeltaFrom == null ? world.Cells.Select(CellView).ToArray() : null,
            cellUpdates = cellDeltaFrom == null ? null : world.Cells.Select((c, i) => (c, i))
                .Where(p => p.c != cellDeltaFrom.Cells[p.i] || world.Jurisdictions?.CellCenters[p.i] != cellDeltaFrom.Jurisdictions?.CellCenters[p.i]
                    || (economicCommanders.GetValueOrDefault(p.i)?.Support.IncomeMultiplier ?? 1f) != previousEconomics!.GetValueOrDefault(p.i, 1f))
                .Select(p => new { cell = p.i, value = CellView(p.c, p.i) }).ToArray(),
            units = deltaBase == null ? unitViews : null,
            unitUpdates = deltaBase == null ? null : unitViews.Where(u => !deliveredUnits.TryGetValue(u.GetProperty("id").GetString()!, out var prior) || !JsonElement.DeepEquals(prior, u)).ToArray(),
            removedUnits = deltaBase == null ? null : deliveredUnits.Keys.Where(id => !pendingUnits.ContainsKey(id)).ToArray(),
            unitOrder = deltaBase == null ? null : world.Units.Select(u => u.Id).ToArray(),
            notices = current.Notices
        };
    }
}
