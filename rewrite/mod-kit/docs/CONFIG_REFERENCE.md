# 配置索引：从目标定位到文件

指挥部完整布局、增删按钮、数据/命令绑定及自定义子页使用 API 3 `headquartersPresentation`，见[指挥部界面指南](porting/HEADQUARTERS_LAYOUT.md)及[最小示例](../examples/headquarters-layout/README.md)。

国家地块颜色：`client/countries.json` 的 `color`；新增/现有势力：`faction_create.color` / `faction_identity.color`；阵营地图：API 3 `mapColors` 引用 `rewrite/map-colors.json`。见[格式、优先级与模板](porting/MAP_COLORS.md)。

中英文地理名称沿曲线逐字排列使用 `client/manifest.json` 中的 `maps.<地图名>.labels[].textPath`，见[字段与作者配置](porting/MAP_TEXT_PATHS.md)。

API 3 的 `geography` 引用 `rewrite/geography.json`，配置禁行山地/海洋与默认图集外观，见[通用地理接口](porting/GEOGRAPHY.md)。

基础规则现支持直接编辑 `legacy/` 下的独立 JSON，见[文件与字段](porting/SPLIT_CATALOG.md)。
下文原 XML 路径继续作为旧资源导入入口。

战役初始显示与隐藏可用 API 2 的 `campaignVisibility` 逐关设置，见[示例](../examples/campaign-visibility/README.md)。背景音乐使用 `client/music.json`，见[音乐配置](../data/client/music/README.md)。

局外初始货币、商品位/库存、刷新次数及恢复条件使用 API 2 的 `metagame`；战役说明UI/目标展示使用 `stageIntroPresentation`，真实目标沿用胜负规则。见[配置指南](porting/METAGAME_AND_BRIEFING.md)及[示例](../examples/feedback-config/README.md)。

结果 `player_control` 切换所扮演势力，`victory_rules.victoryRules` 替换本局胜负条件，`victory_default` 恢复原规则，`ending.key/outcome` 指定结局，见[字段与示例](porting/SCENARIO_CONTROL.md)。

剧本条件/结果/选择按钮：`rewrite/extension.json` 的 `scenarioEvents` 指向事件 JSON；全字段及函数入口见[剧本事件接口](porting/SCENARIO_EVENTS.md)。

结果 faction_create/faction_revive/faction_identity 用于创建、复国及修改名称/颜色/旗帜；独立兵种 unitVariant、可选 templateFaction 和 PNG flagImage 见[势力接口](porting/SCENARIO_FACTIONS.md)。

事件顶层 `repeat` 配置周期，`insufficientResourcesEffects` 配置资源不足结果；条件/结果 `type:"relation"` 使用 `faction`、`otherFaction`、`relation` 控制独立双边外交。示例见[周期与外交](../examples/scenario-recurrence-diplomacy/README.md)。

下面路径以作者覆盖包根目录为准。原格式输入则去掉original前缀，放ZIP根目录或统一assets下。所有JSON为整文件替换，文件名/键名区分大小写。

| 目标 | 文件及关键节点 | 加载/执行入口 |
| --- | --- | --- |
| 导入包标识/版本/文件哈希 | 根mod.json：schema/id/name/version/baseRevision/scenarios/files/warnings/logicRevision | ModStore.Seal/Validate |
| 将领基础属性和技能 | legacy/commanders.json；旧 XML 经导入器转换 | LegacyCatalogFiles.Load；LegacyRuntimeCatalog.FromJson |
| 将领专属喊话 | extension.json的commanderDialogues，apiVersion:2；JSON的schema:1/commanders/ew4.commander.N/kill及killRules | ExtensionLoader.Load；ClientGame；app.js；[条件、固定/随机选句及回退规则](../examples/commander-dialogues/README.md)，occupy/retreat仅预留 |
| 自定义技能/特性/Lua | rewrite/extension.json及其skills/traits/scripts引用 | ExtensionLoader.Load；RuleEventBus.Modify/Dispatch；[完整接口](porting/RULE_EXTENSIONS.md) |
| 物品/装备外置效果 | extension.json的itemBindings，apiVersion:2；target/skills/useSkill/replaceLegacyEffect/value | ExtensionLoader.ConfigureItem；Ew4Commander.Attach；ConsumableItems.Use；[字段与模板](porting/ITEM_EFFECTS.md) |
| 原版技能倍率/概率/折扣/成长 | extension.json的legacySkillParameters，apiVersion:2；JSON的schema:1/skills | LegacySkillParameters；[29技能参数模板](../examples/legacy-skill-parameters/README.md) |
| 上将派遣名额 | extension.json的deploymentLimits，apiVersion:2；JSON的schema:1/limits，支持剧本ID及`*` | ClientContent.Start；RosterDeployment；[字段、模板及存档规则](../examples/deployment-limits/README.md) |
| 召唤/传送/范围/状态/移动力 | apiVersion:2；skills效果及statuses状态定义 | RuleEventBus.ApplyEffect/MovementBudget/ExpireStatuses；[制作步骤](porting/README.md)、[六技能示例](../examples/common-skills/README.md) |
| 动态技能数量/顺序/坐标 | extension.json的unitBindings/commanderBindings/presentation | ExtensionLoader.PatchCatalog；Ew4SkillUI.layout；不限制将领四槽 |
| 自定义征服/战役胜负 | extension.json的victoryRules或BTL的VICT v1 | ExtensionVictory.Evaluate；LegacyBattle.WithVictory；[规则及CLI](porting/RULE_EXTENSIONS.md) |
| 多阶段目标与替换结算界面 | scenarioEvents的victory_rules；extension.json的victoryPresentation | ScenarioRules；VictoryPresentation.Validate；Results.open；[完整指南](porting/VICTORY_MODDING.md) |
| 将领名字 | original/stringtable_cn.ini：name_Napoleon等 | ClientContent.Label；Menus.text |
| 将领头像 | client/ui/commanders1_hd/Napoleon.png或commanders2_hd；manifest.atlases决定图集 | Menus.portrait |
| 公主/素描 | client/ui/princesses_hd、princesses_sketch_hd | Menus.portrait及名册界面 |
| 兵种生命/移动/基础攻击 | original/def_army.xml：strength/movement/minatk/maxatk | LegacyCatalogConverter.Convert；Ew4Combat.Calculate |
| 招募与建造价 | original/def_card.xml：price/industry/army/grade/type | LegacyRecruitmentCatalog.Read/Offers |
| 国家兵模、动作及锚点 | client/unit-visuals.json：factions/variants/profiles/defaults/definitions；[独立接口](porting/UNIT_VISUALS.md) | Effects.profile/motion；app.visualProfile；battlefield.Renderer |
| 全局国家身份与默认资源 | client/countries.json：countries/aliases；[国家表与长ID](porting/GLOBAL_COUNTRIES.md) | CountryCatalog；ClientContent；CNTR v1 / country-export/apply/strip |
| 兵种卡片 | manifest.profiles[id].card/cardMarker，对应client/ui/image_recruit_hd等 | app.unitCard/openRecruitment |
| 地形及移动 | original/def_mapterrain.xml、def_terraintype.xml和地形BIN | LegacyTerrainRules.FromXml/Resolve |
| 建筑定义 | original/def_construction.xml/def_capital.xml | LegacyBuildingCatalog.Read |
| 战斗场景 | original/campaign1_01.btl等 | LegacyBattle.Read；ClientContent.Start |
| 逐格手工辖区 | 对应BTL尾部JURI v1，不是自动加载的JSON | LegacyBattleExtensions；TerritorialJurisdictions.FromAssignments；[协议和命令](porting/BTL_JURISDICTIONS.md) |
| 大章按钮/年份/国家 | original/def_battleline.xml：line顺序/name/start/end/countries | ClientContent.Chapters；Menus.open/previewChapter |
| 章节关卡说明与地图标记 | original/def_battlelist.xml和manifest.strategic | ClientContent.Presentations；Menus.openChapter |
| 战役标题/介绍 | original/stringtable_cn.ini：title_<name>/desc_<name> | ClientContent.Label |
| 征服页和占位 | frontend/conquest-pages.json：pages[].slots[] | app.scenarioList/turnConquestPage |
| 地图登记 | original/def_map.xml：id/name/file/tile/textpos | ClientContent构造；LegacyModImporter.ConvertMaps |
| 显示底图/叠层 | client/manifest.json：maps[name].tiles/overlays/labels/pixelRatio | map-surface.js与battlefield.js |
| 国旗/设施/运输船 | client/images/buildings*.png及manifest.atlases；选国旗在ui/image_flag_hd | battlefield.flag/flags；app.sprite |
| 城市中文译名 | frontend/city-names.json：英文原名到中文 | app加载；battlefield.labels |
| 布局背景引用 | client/manifest.json：layouts、menuTextures的file/ratio | ConvertMenuAssets；MenuArt.prepare/texture/apply，只有已读取字段生效 |
| 历史位图字库 | manifest.fonts及images/font_*，不再控制现行文本 | 不通过glyphs/preserveColor修改字号/描边 |
| 当前统一字体 | client/fonts/*.woff2与coverage.json（Noto Serif SC/Jigmo/Unifont） | original-ui.prepareFonts、TextRenderer.prepare/layout/element、displayText |
| 历史预渲染菜单字 | manifest.ui.labels，新转换器清空旧文字缓存 | 当前按文本和统一字体绘制 |
| 粒子/受击时间轴 | client/effects.json：bindings/motions/timelines/definitions | Effects.motion/timeline/emit；app.playCombat |
| 动画关键帧 | client/animations/<library>.json：fps/items/elements/atlas | LegacyBinary.Animation；app.animation |
| 音效 | client/sounds/<文件名>与effects.sounds及时间轴sound | Effects.sound |
| 升级经验/治疗/费用 | legacy/unit-progression.json、commander-progression.json | LegacyCatalogFiles.Rules；LegacyCombatCatalog.Read |
| 局外折扣/商业 | legacy/economy-rules.json | LegacyCatalogFiles.Rules；LegacyCommanderAccounts.Read |
| 使用哪个Mod | 安装目录active.json，不是包内配置 | ModStore.Activate |
| 账户和战局 | account.json、saves/mode-*/slot-*.json | ClientGame、ClientSaves |

## 不存在的资源开关

胜利/失败结算已支持 `rewrite/extension.json.victoryPresentation` 指向布局与数据/命令绑定配置，并可通过事件多阶段替换胜负规则，见[结算扩展指南](porting/VICTORY_MODDING.md)。指挥部已支持 `headquartersPresentation` 完整布局与按钮绑定；其余界面的通用控件/命令绑定、规则/表现包组合和新的完整剧本容器尚在规划，不能向当前mod.json添加想象的字段启用。手工辖区已通过BTL的JURI扩展实现。大改UI需一起处理布局、点击区、交互和配套资源，见[移植指南](porting/README.md)。

技能/特性已支持`rewrite/extension.json`显式注册定义与Lua，可配置胜负条件；字段必须符合[API 1指南](porting/RULE_EXTENSIONS.md)，不是任意位置的skills.json都自动加载。当前没有`jurisdiction.json`公共地图分区文件或`combat.random.uniform`开关。辖区优先级、外交、解散返还和随机取样仍由后端代码控制；具体类见[函数索引](SOURCE_INDEX.md)。

当前基础攻击正常区间有min钳位，但分布偏向下限；反向端点未完整拒绝。最终伤害还受攻击次数/修正/倍率影响。字段minatk/maxatk不能当最终伤害硬上下限使用。

## 清单是自动产物

`files`把覆盖相对路径映射为SHA256；`scenarios`是可加载剧本ID全集，`baseRevision`绑定当前基础资源，`logicRevision`由实际原定义/规则内容计算。仅修改version不替代资源校验；改内容后重新pack。手改哈希不会把无效XML或缺失动作变有效。
