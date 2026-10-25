# 独立规则配置

源码基础数据在 `rewrite/data/legacy/`，发行包中在 `data/legacy/`。
作者覆盖包的路径为 `legacy/文件名.json`，不带 `data/`。
配置已接入实际运行、Mod 校验和双端打包。各文件 `schema` 为 1，覆盖按完整文件替换。

| 文件 | 节点 | 内容 |
| --- | --- | --- |
| `commanders.json` | `commanders[]` | 将领名称键、属性、商业、军衔爵位、价格、抽取条件、装备、技能引用 |
| `skills.json` | `skills[]` | 原版技能 ID、legacy_id、names.zh_CN、legacy_description；文字直接用于界面 |
| `units.json` | `units[]` | 兵种血量、移动、攻击、射程、粮耗、特性、经验、点燃、武器类型 |
| `unit-variants.json` | `variants` | 各国各兵种等级对应的完整配置 ID 映射 |
| `items.json` | `items[]` | 装备/消耗品的 id/name/function/value/target/price/flag/consumable |
| `recruitment.json` | `cards[]` | 招募/设施/运输卡价格、工业、兵种、等级、冷却、建造回合 |
| `buildings.json` | `buildings[]` | 建筑各等级的产出、防御、补给、招募条件 |
| `installations.json` | `installations[]` | 战壕、栅栏、掩体防御 |
| `terrain-rules.json` | `terrain[]` | 地形移动消耗和战斗修正 |
| `conquest-list.json` | `entries[]` | 原征服目录元数据，不自动新增剧本或菜单分页 |
| `combat-rules.json` | `tables` | 公共攻击次数阈值、武器克制倍率及原生来源资料 |
| `unit-progression.json` | `rules` | 部队经验、装甲、治疗、升级、训练费用 |
| `commander-progression.json` | `rules` | 军衔/爵位经验、生命、回血 |
| `economy-rules.json` | `rules` | 爵位商业底线及军衔/爵位价格系数 |

`catalog.json` 为 schema_version 3 的来源与文件索引。
`runtime_rule_tables.json`、`metagame_rule_tables.json` 为 schema_version 2 的兼容索引。
普通独立 JSON 编辑无需手改原 XML 或其哈希。

将领属性已展开，`id:"ew4.commander.1"` 必须匹配 `legacy_id:1`。
`name` 仍是头像和文字资源键，改名需配套资源。
`skills` 是不限四槽的字符串 ID 数组，可引用原版技能和已注册的 R 版技能。
R 技能效果仍须在 `rewrite/extension.json` 引用的定义 JSON/Lua 中实现；添加描述不会产生效果。
`commanderBindings` 在基础配置后应用。高级 `skill_slots` 需与 `skills` 引用一致。
原版倍率、概率、教学等仍使用 [legacySkillParameters](../../examples/legacy-skill-parameters/README.md)。
部分默认技能说明会按参数展开，作者自定义说明保留原文。

兵种 `weapon` 为 gun/guns/mgun/cannon/rocket/cold。
省略 `damage_multipliers` 时从公共 weapon_category_multipliers 按武器取值，
每种武器连续五项对应步兵、骑兵、炮兵、舰船、要塞。
省略 `attack_bands` 时按等级和 attack_count_health_thresholds 生成攻击次数。
显式填写这两个字段可对单兵种覆盖；自定义等级必须提供 attack_bands。
攻击上下限是每次基础攻击，不是最终伤害。

combat-rules 中的 recruit_action_by_unit_grade、combat_constants_181f28、
war_experience_multipliers、economic_multipliers 为来源资料，未新增为可执行入口。
经验和经济技能倍率使用上述技能参数接口。

建筑等嵌套记录保存在 children 数组中，tag 表示元素名，例如 level、recruit。
其余属性保留原 XML 名称，数值可用 JSON 数字。
内存转换统一供规则模块读取，装备效果与价格、将领商业与战斗属性使用同一份 JSON。

复制需要修改的完整文件到覆盖包 legacy/ 后执行 pack、verify，再导入启用。
缺省文件沿用基础版；文件参与指纹和存档隔离。已有账户实例不会自动按新定义重置。
旧 APK/XML 导入会生成独立文件。旧 schema 2 完整 catalog、schema 1 完整规则表仍可读。
绑定旧基础指纹的 ew4mod 需重新导入原资源或重新制作覆盖包；未增加玩家档案自动迁移。

工具命令：split-catalog <旧catalog.json> <输出legacy目录> --base=<旧规则表目录>。
默认拒绝覆盖，明确重生成时加 --replace。
catalog-export <输出.json> --base=<完整data目录> 可导出合成视图。
源码维护后用 `python tools/catalog_files.py --refresh-index rewrite/data` 更新资源校验清单。
