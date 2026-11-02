# 剧本收入示例

将 `rewrite/` 打包到 Mod 覆盖目录。此示例只作用于 `conquest1`：一级农田产出 12，一级城市产出 2，玩家势力每回合基础粮食 4。其他资源和其他剧本保持原规则；开局库存不变。

这是接口演示，未提供军队平衡。使用 `faction_create.income` 明确拆分势力的保底，使用 `site_income` 配置可夺取粮源，详见[接口说明](../../docs/porting/SCENARIO_ECONOMY.md)。
