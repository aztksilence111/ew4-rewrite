using System.Globalization;
using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private string LegacySkillDescription(int number)
    {
        string original = content.Label("desc_skill_" + number.ToString("00"));
        var parameters = content.Catalogs.Combat.SkillParameters ?? LegacySkillParameters.Default;
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        string Change(int id)
        {
            double percent = parameters.Skills[Ew4Combat.Skill(id)]["multiplier"] * 100 - 100;
            return Math.Abs(percent) < .000001 ? "不变" : percent > 0
                ? "提高" + Number(percent) + "%" : "减少" + Number(-percent) + "%";
        }
        string Saving(int id) => Number(100 - parameters.Percent(id)) + "%";
        string Teaching(int id, string ability) => "此将军被整编时，接收将军的" + ability + "能力提高"
            + parameters.Gain(id) + "级，最高通过此技能提升至" + parameters.Maximum(id) + "级。";

        // Match the original wording so a Mod's custom descriptions retain priority.
        return (number, original) switch
        {
            (2, "部队的移动将无视地形影响") => "陆地移动按平地消耗计算；渡河、上下运输船的移动限制仍生效。",
            (3, "部队所在区域可以防止火灾") => "部队驻守时，阻止敌军攻击点燃所在区域或部队；不会扑灭已有火灾。",
            (4, "部队士气不会出现低落状态") => "部队的有效士气最低为0，不会因士气低落而受到攻击惩罚或无法反击。",
            (5, "部队对炮台和要塞的伤害大幅增加") => "部队对炮台和要塞造成的伤害" + Change(4) + "。",
            (6, "使敌军对炮兵的回避无效") => "指挥炮兵时，攻击和反击均无视敌军的全部回避减伤；不忽略防御和装甲。",
            (7, "炮兵移动+2") => "炮兵移动+" + parameters.Bonus(6) + "。",
            (8, "炮兵准确的炮弹轨迹将更有效的杀伤敌军") => "炮兵每次基础攻击的下限+" + parameters.Bonus(7) + "。",
            (9, "炮兵使用炸药可以大幅提高炮弹的威力") => "炮兵每次基础攻击的上限+" + parameters.Bonus(8) + "。",
            (10, "使敌军对骑兵的回避无效") => "指挥骑兵时，攻击和反击均无视敌军的全部回避减伤；不忽略防御和装甲。",
            (11, "突然袭击可以让骑兵的冲锋更具有效果") => "骑兵每次基础攻击的上限+" + parameters.Bonus(10) + "。",
            (12, "利用骑兵的高机动力对敌军造成更大的伤害") => "骑兵每次基础攻击的下限+" + parameters.Bonus(11) + "。",
            (13, "利用阵形可以有效提高步兵的战斗力") => "步兵每次基础攻击的上限+" + parameters.Bonus(12) + "。",
            (14, "运用步兵战术能对敌军造成更大伤害") => "步兵每次基础攻击的下限+" + parameters.Bonus(13) + "。",
            (15, "使敌军对步兵的回避无效") => "指挥步兵时，攻击和反击均无视敌军的全部回避减伤；不忽略防御和装甲。",
            (16, "步兵部队输出不受自身生命值影响") => "步兵攻击和反击时，基础攻击次数按满血计算，不因生命值降低而减少。",
            (17, "减少海军被攻击时所受到的伤害") => "指挥舰船时，受到的攻击和反击伤害" + Change(16) + "。",
            (18, "陆军在海上作战没有劣势") => "陆军在海上攻击和反击时，免除20%的伤害降低惩罚。",
            (19, "舰船移动+1") => "舰船移动+" + parameters.Bonus(18) + "。",
            (20, "将军获得的爵位经验大幅增加") => "战斗中获得的爵位经验" + Change(19) + "；同类经验装备优先，与此技能不叠加。",
            (21, "将军获得的军衔经验增加") => "战斗中获得的军衔经验" + Change(20) + "；与战争大师同时拥有时以大师为准，同类经验装备优先，不叠加。",
            (22, "将军获得的军衔经验大幅增加") => "战斗中获得的军衔经验" + Change(21) + "；与战争专家不叠加，同类经验装备优先，不叠加。",
            (23, "将军部队不消耗粮草也不受粮食不足影响") => "将军部队粮草消耗为0，免除粮食短缺造成的士气惩罚。",
            (24, "将军部队所在地获得的资源增加") => "将军部队所在地的金币、工业和粮食产出" + Change(23) + "；与经济大师同时拥有时以大师为准。",
            (25, "将军部队所在地获得的资源大幅增加") => "将军部队所在地的金币、工业和粮食产出" + Change(24) + "；与经济专家不叠加。",
            (26, "建造掩体所耗费的资源大幅减少") => "本部队建造掩体所需的金币减少" + Saving(25) + "。",
            (27, "建造运输船所耗费的资源大幅减少") => "本部队建造运输船所需的金币减少" + Saving(26) + "。",
            (28, "建造战壕所耗费的资源大幅减少") => "本部队建造战壕所需的金币减少" + Saving(27) + "。",
            (29, "建造栅栏所耗费的资源大幅减少") => "本部队建造栅栏所需的金币减少" + Saving(28) + "。",
            (30, "提高部队对守城敌军的战斗力") => "攻击和反击建筑内的敌军时，无视建筑提供的回避减伤；不忽略其他回避修正、防御和装甲。",
            (31, "减少升级建筑时所耗费的资源") => "将军部队所在地升级建筑所需的金币和工业减少" + Saving(30) + "。",
            (32, "攻击时有一定概率打出致命一击") => "主动攻击时有" + parameters.Percent(31) + "%概率，使本次各次基础攻击均取上限值；反击不触发。",
            (33, "防守时有一定概率将受到的伤害降至最低") => "受到敌军主动攻击时，有" + parameters.Percent(32) + "%概率将本次受到的伤害降至1点；受到反击时不触发。",
            (34, "将军被整编时增加步兵1级") => Teaching(33, "步兵"),
            (35, "将军被整编时增加骑兵1级") => Teaching(34, "骑兵"),
            (36, "将军被整编时增加炮兵1级") => Teaching(35, "炮兵"),
            (37, "将军被整编时增加海军1级") => Teaching(36, "海军"),
            (38, "将军被整编时增加要塞1级") => Teaching(37, "要塞"),
            (39, "将军被整编时增加商业1级") => Teaching(38, "商业"),
            (40, "将军被整编时增加移动1级") => Teaching(39, "移动"),
            _ => original
        };
    }
}
