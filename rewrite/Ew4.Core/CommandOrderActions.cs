using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed partial class RuleEventBus
{
    private IReadOnlyDictionary<string, UnitProfile> orderProfiles = new Dictionary<string, UnitProfile>();
    private IReadOnlyDictionary<string, long> orderFood = new Dictionary<string, long>();
    public void RegisterOrderProfiles(IReadOnlyDictionary<string, UnitProfile> profiles, IReadOnlyDictionary<string,long>? food = null)
    { orderProfiles = profiles; orderFood = food ?? new Dictionary<string,long>(); }
    internal UnitProfile? OrderProfile(string id) => orderProfiles.GetValueOrDefault(id);
    internal long OrderFood(string id) => orderFood.GetValueOrDefault(id);
}

public static partial class CommandOrders
{
    private static bool PayloadAvailable(WorldState w, UnitState source, CommandOrder r, RuleEventBus? bus) =>
        !source.Acted && w.ExtensionState.GetValueOrDefault("orders/payload/" + source.Id) == 0 && r.Profiles.Length == 1
        && bus?.OrderProfile(r.Profiles[0]) is { } profile && !WorldUnitLayers.Air(w,profile)
        && w.Cells[source.Cell].Terrain.CanTraverse() && !w.Cells[source.Cell].Terrain.Sea
        && (w.Cells[source.Cell].Owner is not string owner || WorldDiplomacy.Allied(w,source.Owner,owner))
        && !WorldUnitLayers.Surface(w).Any(t=>t.Cell==source.Cell)
        && WorldUnitLayers.HasSpace(w,profile,source.Owner,source.Cell)
        && WorldArmyCapacity.CanAdd(w,source.Owner,w.Units.Count(t=>t.Owner==source.Owner));

    private static WorldState DeployPayload(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus bus)
    {
        var r=skill.Order!;
        if (!PayloadAvailable(w,source,r,bus)) throw new RuleViolation("orders.payload","Payload is spent or the surface destination is illegal");
        var profile=bus.OrderProfile(r.Profiles[0])!;
        var unit=new UnitState { Id="orders.payload."+source.Id, Owner=source.Owner, Cell=source.Cell,
            Acted=true, RemainingMovement=0,
            Combatant=new() { Profile=profile, Health=(int)Math.Ceiling(profile.MaxHealth*.75), BuildingKind=w.Cells[source.Cell].BuildingKind },
            Support=new() { FoodConsumption=bus.OrderFood(profile.Id) } };
        foreach(var (s,slot,rule,_) in BoundRules(source,bus).Where(p=>p.Rule.Plan=="transport_protection"))
            unit=unit with { Orders=unit.Orders.Append(new OrderGrant(s.Id,slot.InstanceId,source.Id,source.Owner,source.CommanderId,
                w.Round,source.Cell,RecipientCommander:null,Epoch:Epoch(w,source.Owner),Plan:"payload_protection")).ToArray() };
        var state=w.ExtensionState.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
        state["orders/payload/"+source.Id]=1;
        return w with { Units=w.Units.Append(unit).ToArray(), ExtensionState=state };
    }

    private static CommandOrder DynamicGrant(WorldState w, UnitState recipient, OrderGrant grant, CommandOrder r, RuleEventBus bus)
    {
        if (grant.Plan=="payload_protection") return r with { Bonus=new() { Defense=.1,CounterDefense=.1 } };
        if (r.Plan!="air_spot_action") return r;
        var observer=Unit(w,grant.Source);
        bool valid=observer!=null && Distance(w,observer.Cell,recipient.Cell)<=2
            && Get(w,recipient,"air_spot_used")!=Epoch(w,recipient.Owner)+1;
        double strength=observer!=null && BoundRules(observer,bus).Any(p=>p.Rule.Plan=="air_spotting")?.2:.1;
        return r with { Bonus=new() { Attack=valid?strength:0 } };
    }
}
