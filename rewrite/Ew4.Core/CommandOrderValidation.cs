using System;
using System.Linq;

namespace Ew4.Core;

public static partial class CommandOrders
{
    private const string SelectorConditions = "ground mobile self armor not_armor infantry_or_armor engineer own city settled city_near city_two port_two port_near airport no_building damaged fresh not_attacked spent stationary last_stationary low_morale normal_morale vulnerable weaker morale_susceptible infantry_near artillery_near armor_near naval_near no_artillery_two river_near city_or_river city_or_road city_or_forest infantry_field_near two_categories_near infantry_armor_pair port_or_coast_near enemy_territory air_ground_weapon fighter own_enemy_adjacent siege_flanker fortified under_construction city_or_mountain infantry_pair_if_cavalry fighter_near city_or_port attacked_since_end attacked_fort killed_city air_attack naval_attack landed two_allies";
    private const string CombatConditions = "main counter first first_attack first_defense first_air_defense adjacent distant enemy_ground enemy_mobile enemy_infantry enemy_naval enemy_air enemy_cavalry enemy_armor enemy_fort enemy_city_nonfort enemy_siege enemy_building enemy_field enemy_nonarmor_field enemy_low enemy_healthy enemy_low_morale enemy_isolated enemy_mountain_forest enemy_forest_fort flanking two_flanking infantry_flanking other_type_flanking first_against_target other_type_hit infantry_hit kill";
    private const string Plans = "advance advance_inspire advance_two_stage air_close_support air_evasion air_kill_shock air_morale air_spotting air_spot_action payload_action artillery_handoff assault_suppress attack_or_kill_shock attacker_morale battle_confusion_escape bombard_confusion bombard_suppress calibration choose choose_artillery_mark choose_confusion_protection choose_infantry_mark choose_intel_protection choose_low_morale_cover choose_morale_chain choose_morale_city choose_reactivate_intel_kill choose_swap_on_defend choose_swap_on_shock city_direction city_rally combined_breach combined_shock confuse_half construction_finish counter_heal counter_resolve counter_shock damage_redirect different_type_terrain discipline encircled_target enemy_terrain engineer_construction extract_vulnerable extract_weaker first_river food_income forest_ambush forest_escape forest_shock industry_income infantry_artillery intel_range intel_range_city intel_terrain intelligence_warning kill_shock_advance landing_guard landing_recovery lateral master_training_health morale_guard mutual_guard naval_block naval_swap net_movement_attack net_movement_shock next_ally_movement_on_kill next_attack_on_defend next_attack_on_kill next_movement_on_defend own_road pre_morale rear_city_road rear_guard road_recipient siege_followup splash_protection survive swap_flank teaching_income terrain_sequence training_health transport_protection vulnerable_pair weaker_guard";

    private static void ValidateOrderNames(ExtensionSkill s, CommandOrder r, int api)
    {
        const string operations="op_swap op_recruit op_relocate op_entrench op_mountain op_retreat op_recover op_delay op_artillery op_contact op_air_ground op_cavalry op_sabotage op_armor";
        bool Known(string names,string value)=>(names==Plans?names+" "+operations:names).Split(' ').Contains(value,StringComparer.Ordinal);
        bool SelectorNames(OrderSelector? selector)=>selector==null || selector.Conditions.All(c=>Known(SelectorConditions,c));
        if (r.Additional==null || r.Additional.Length>16 || r.Additional.Any(a=>a==null || a.Additional==null || a.Additional.Length!=0)
            || r.Conditions==null || r.Conditions.Any(c=>!Known(SelectorConditions,c) && !Known(CombatConditions,c))
            || r.Plan!=null && !Known(Plans,r.Plan) || !SelectorNames(r.Source) || !SelectorNames(r.Recipients)
            || !SelectorNames(r.Enemy) || !SelectorNames(r.SecondaryEnemy)
            || r.IncomeCap<0 || r.Bonus.MoraleChange is < -4 or > 4 || r.Bonus.LowerAttack is < 0 or > 100 || r.Bonus.UpperAttack is < 0 or > 100)
            throw new RuleViolation("orders.configuration","Unknown or invalid order mechanism: "+s.Id);
        foreach(var additional in r.Additional)
            Validate(s with { Order=additional, Kind=additional.Event=="activate"?"active":"passive" },api);
    }
}
