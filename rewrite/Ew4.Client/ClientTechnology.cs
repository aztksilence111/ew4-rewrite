using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private static readonly string[] TechnologyImages = [
        "militia", "line", "light", "grenadier", "guard", "machinegun", "lightcavalry", "heavycavalry", "lancer", "armored",
        "lightartillery", "heavyartillery", "coastalartillery", "rocket", "ship1", "ship2", "ship3", "ship4",
        "fortress1", "fortress2", "fortress3", "fortressartillery"
    ];
    private static readonly string[] TechnologyPreviews = [
        "recruit_militia.png", "recruit_line.png", "recruit_light.png", "recruit_grenadier.png", "recruit_guard.png", "recruit_machinegun.png",
        "recruit_lightcavalry.png", "recruit_heavycavalry.png", "recruit_musketcavalry.png", "recruit_armoredcar.png",
        "recruit_lightartillery.png", "recruit_heavyartillery.png", "recruit_fortressartillery.png", "recruit_rocket.png",
        "recruit_cruiser.png", "recruit_frigate.png", "recruit_battleship.png", "recruit_ironclads.png",
        "build_smallfortress.png", "build_mediumfortress.png", "build_largefortress.png", "build_coastalartillery.png"
    ];
    private object Technology(ClientCommand command)
    {
        if (command.Op is not ("" or "upgrade")) throw new RuleViolation("technology.command", "无效科技操作");
        var account = Account();
        var technology = content.Catalogs.Technology;
        var levels = technology.Snapshot(account, command.Group);
        if (command.Op == "upgrade")
        {
            if (command.Slot is < 0 or >= 22) throw new RuleViolation("technology.slot", "原版可升级兵种仅包含0至21号项目");
            account = technology.Upgrade(account, command.Revision, command.Group, command.Slot);
            InstallAccount(account);
            levels = technology.Snapshot(account, command.Group);
        }
        return new { account.Revision, warzone = command.Group, stars = CampaignTechnologyStars.Balance(account),
            chapter = content.Chapters.FirstOrDefault(c => c.Id == command.Group + 1)?.Name ?? (command.Group + 1).ToString(),
            entries = levels.Take(22).Select((level, slot) => {
                string name = content.Label("name_" + (content.TechnologyUnitNames.ElementAtOrDefault(slot) ?? slot.ToString()));
                int? cost = technology.UpgradeCost(slot, level);
                return new { slot, name, level, maximum = level >= 3, cost,
                    canUpgrade = cost.HasValue && cost <= CampaignTechnologyStars.Balance(account),
                    group = slot < 6 ? 0 : slot < 10 ? 1 : slot < 14 ? 2 : slot < 18 ? 3 : 4,
                    previewImage = TechnologyPreviews[slot], image = "button_upgrade_" + TechnologyImages[slot] + ".png" };
            }).ToArray() };
    }
}
