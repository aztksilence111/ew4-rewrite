# 源码文件与函数索引

曲线地理文字：`Web/wwwroot/battlefield.js` 的 `textPath/pathLettering/mapText` 共用中英路径；`tools/eastasia_lettering.py` 生成地形贴合路径及逐字动画，`Mods/ResourceValidation.CheckMapLabelPaths` 校验导入字段，见[作者接口](porting/MAP_TEXT_PATHS.md)。

通用禁行地形：`Core/Geography.cs`校验，`Core/HexMap.cs`统一通行，`Legacy/GeographyCatalog.cs`配置应用，`ClientContent.Start`真实加载；字段与模板命令见[API 3地理接口](porting/GEOGRAPHY.md)。

独立规则文件由 `Ew4.Legacy/LegacyCatalogFiles.cs` 加载和合成；`ClientContent` 将同一配置交给
战斗、总部、商业、招募及显示模块。文件与字段见[独立配置指南](porting/SPLIT_CATALOG.md)。

路径相对源码仓库根目录；工具发行包不含这些源码。函数名比行号更适合后续版本搜索。仓库中可用`rg -n "函数名" rewrite/对应目录`定位；下表不是让普通作者修改所有代码。

需要从旧文件或原生函数反查时，先读[原包入库/运行入口对照](porting/ENTRYPOINTS.md)，包括原SO基线、Ghidra标识和证据文件，避免混用不同库版本的地址。

## 规则扩展API 1/2

物品/装备：`Ew4.Legacy/ExtensionItems.cs` 的 ValidateItems/ConfigureItem、`ExtensionLoader.Load/ConfigureRuntime` 加载并校验 itemBindings；`Ew4Commander.Attach` 合并装备授予技能，`ConsumableItems.Use` 通过 RuleEventBus.Dispatch 的 grantedSlot 执行一次消耗效果。Lua 身份字段由 RuleScriptContext/ExtensionScriptRuntime 传递。验证入口 ItemExtensionChecks、ItemExtensionModChecks 和 tools/verify_item_extensions.cjs，作者入口见[物品接口](porting/ITEM_EFFECTS.md)。

原版技能数值：`Ew4.Core/LegacySkillParameters.cs` 的 FromOverrides/Multiplier/Bonus/Percent/Gain/Maximum；`ExtensionLoader.Load` 读取 legacySkillParameters，`ClientContent` 向战斗、将领任命、原军队恢复、经验、折扣与教学传递同一组参数。验证入口 LegacySkillParameterChecks、LegacySkillParameterModChecks 和 tools/verify_legacy_skill_parameters_web.cjs，作者入口见[参数模板](../examples/legacy-skill-parameters/README.md)。

势力事件：Ew4.Core/ScenarioFactions.cs 的 Create/Revive/Identity/Control，以及 ScenarioRules 的 Match/Apply/Validate；客户端 ClientContent.Start 配置新势力兵种与招募，ClientGame.View 投影名称、国土颜色和旗帜。格式见[势力接口](porting/SCENARIO_FACTIONS.md)，验证入口 ScenarioFactionChecks 和 tools/verify_scenario_factions_web.cjs。

角色与胜负切换：SessionActions.SyncPlayer同步权限/派遣历史，WorldState.ScenarioVictory/ScenarioEnding保存本局目标与结局，WorldVictory.Evaluate使用事件覆盖；ExtensionVictory统一校验/复制，ExtensionLoader载入嵌套Lua。见[控制接口](porting/SCENARIO_CONTROL.md)、ScenarioControlChecks和tools/verify_scenario_control_web.cjs。

API 2通用操作在`Ew4.Core/SkillWorldEffects.cs`：RegisterUnitTemplates、ValidCellTarget、ApplyEffect、MovementBudget、ExpireStatuses、ChangeNotices；状态保存在Combatant.Statuses，移动上限缓存在UnitState.ExtensionMovementLimit。ExtensionLoader.ConfigureRuntime注册召唤模板；WorldSkillActions.Execute接收可选targetCell。验证入口为CommonSkillChecks、CommonSkillModChecks及tools/verify_common_skills_web.cjs。

完整格式及源码映射见[规则接口指南](porting/RULE_EXTENSIONS.md)。主入口：`Ew4.Legacy/ExtensionLoader.cs`的Load/PatchCatalog/ValidateVictoryScripts，`ExtensionScriptRuntime.cs`的Run/Execute，`Ew4.Core/RuleExtensions.cs`的RuleEventBus和WorldSkillActions.Execute，`ExtensionVictory.cs`的Evaluate，`Ew4.ModTool/VictoryCommands.cs`的Run。UI在`wwwroot/skill-ui.js`及menus.commanderInfo/app.openUnitInfo/openSkillAction；将领不固定四技能位。

## 导入、转换、验证、存储

| 源码路径 | 类/函数 | 负责内容 |
| --- | --- | --- |
| `rewrite/Ew4.Mods/ModFiles.cs` | ModManifest/ModInfo/ImportProgress | 清单、安装信息、进度数据结构 |
| `rewrite/Ew4.Mods/ModFiles.cs` | ModBase.Content/Read/Revision | 基础数据路径映射及基础版本 |
| `rewrite/Ew4.Mods/ModFiles.cs` | ModFiles.PathName/RuntimePath/Identifier/Xml | 路径与文件白名单、ID、XML读取 |
| `rewrite/Ew4.Mods/ModFiles.cs` | ImportSource.Read | APK/ZIP/目录输入、预算与重复路径检查 |
| `rewrite/Ew4.Mods/ModStore.cs` | BeginImport/Import/ImportCore | 后台进度、输入分流和临时安装提交 |
| `rewrite/Ew4.Mods/ModStore.cs` | Seal/LogicRevision/Validate/Export | 生成清单、存档逻辑版本、验证、导出 |
| `rewrite/Ew4.Mods/ModStore.cs` | ReadFrom/Read/Activate/NewGame | 覆盖优先级、启用切换和独立存档 |
| `rewrite/Ew4.Mods/LegacyModImporter.cs` | Convert/Changed/Read | 原资源变化检测、索引/文字/分页等转换 |
| `rewrite/Ew4.Mods/LegacyModImporter.cs` | ConvertMenuAssets | 布局背景引用、HD比例、选择页裁剪、menuTextures映射；不导入字库样式 |
| `rewrite/Ew4.Mods/FrontendResources.cs` | ModStore.Frontend/ReadContent | HTML/CSS/JS资源版本地址与切换中旧请求的内容隔离 |
| `rewrite/Ew4.Web/wwwroot/menu-art.js` | MenuArt.prepare/texture/apply/drawStrategic；cityName | 主菜单分块拼接、布局选背景、完整尺寸预览、双向城市名称 |
| `rewrite/Ew4.Web/wwwroot/original-ui.js` | TextRenderer.prepare/layout/element | 自带字体、固定字号、城市白填充黑描边；不依赖Mod位图字库 |
| `rewrite/Ew4.Mods/LegacyResourceResolver.cs` | Read/Changed/CompleteCityLabels/CheckMapReferences | 包优先、缺失共享文件回退、精确补名、完整APK地图检查及来源哈希 |
| `rewrite/Ew4.Mods/LegacyXml.cs` | Normalize | TinyXML声明、相连属性、未完成尾注释的兼容读取；不接受DTD |
| `rewrite/Ew4.Mods/LegacyModImporter.cs` | ConvertProfiles | def_army/def_motion到显示profile |
| `rewrite/Ew4.Mods/LegacyModImporter.cs` | ConvertAnimations/ConvertMaps/ConvertEffects | 动作、地图分块、粒子与时间轴 |
| `rewrite/Ew4.Mods/LegacyCatalogConverter.cs` | Sources/Convert/Strings | 规则源清单、XML转catalog、INI文字 |
| `rewrite/Ew4.Mods/LegacyGraphics.cs` | Raster.Decode/Crop/Png；LegacyBinary.Font/Animation | 贴图解码/裁剪、二进制FNT和BILE解析 |
| `rewrite/Ew4.Mods/ResourceValidation.cs` | Check/CheckAnimation | 图片引用、显示profile、分页及关键帧检查 |
| `rewrite/Ew4.ModTool/Program.cs` | 顶层命令分发/Run | import/list/pack/verify/activate/export |
| `rewrite/Ew4.Web/Program.cs` | Web路由及Read/Mime | /api/mod-import、/api/mods、/content资源 |
| `rewrite/Ew4.Android/MainActivity.cs` | OnCreate/Pick/OnActivityResult；ImportBridge | 手机系统文件选择、私有缓存及JS桥接 |

## 剧本、规则、存档

自定义剧本事件：`rewrite/Ew4.Core/ScenarioRules.cs` 的 Match/Evaluate/Choose，`ClientScenarioEvents.cs` 的 ScenarioPopupView；格式见[事件接口](porting/SCENARIO_EVENTS.md)。名册派遣候选由 `ClientDeployment.cs` 的 Deployment 和 `RosterDeployment.CanDeploy` 共用校验，前端 `Menus.deployGeneral` 查询可用将领后进入名册选择。

周期调度与资源短缺事务也在 `ScenarioRules.cs`；双边关系在 `WorldDiplomacy.cs` 的 Set/Relation/Allied/Hostile，移动、战斗、AI、支援及 `WorldVictory.cs`/`ExtensionVictory.cs` 共用该关系。专项回归为 `ScenarioRecurrenceDiplomacyChecks.cs`，前端关系投影检查为 `tools/verify_scenario_diplomacy_ui.cjs`。

| 源码路径 | 类/函数 | 负责内容 |
| --- | --- | --- |
| `rewrite/Ew4.Client/ClientContent.cs` | 构造函数/Label/Battle/Start | 配置读取、章节/地图、剧本和新局 |
| `rewrite/Ew4.Legacy/LegacyResources.cs` | LegacyBattle.Read/LegacyTerrainMap.Read | BTL与YSAE地形二进制 |
| `rewrite/Ew4.Legacy/LegacyBattleExtensions.cs` | Read/Jurisdictions/Write；LegacyJurisdictionData.Apply | BTL的JURI v1协议、主体绑定及逐格记录 |
| `rewrite/Ew4.ModTool/JurisdictionCommands.cs` | Run | jurisdiction-export/apply/strip及实际开局校验 |
| `rewrite/Ew4.Legacy/LegacyRuntimeCatalog.cs` | FromJson/VerifySources/Unit/UnitIndex | 转换目录、源哈希、兵种身份与索引 |
| `rewrite/Ew4.Legacy/LegacyRuntimeCatalog.cs` | SerializedUnit/HasUnit | BTL平面编队索引、国家是否具有招募定义 |
| `rewrite/Ew4.Legacy/LegacyResources.cs` | LegacyBattle.EffectiveSites | 每格最后一条有效设施；原始Sites/ToBytes仍完整保留 |
| `rewrite/Ew4.Legacy/LegacyScenarioMap.cs` | Import；LegacyMapDiagnostic | 缺名称保留设施、场景外元数据、地图诊断 |
| `rewrite/Ew4.Legacy/LegacyScenarioEvents.cs` | Import | 原重复事件更新规则、设施占领和将领死亡引用 |
| `rewrite/Ew4.Legacy/LegacyScenarioHost.cs` | LegacyScenarioHost.Start | 剧本转换为运行世界与规则 |
| `rewrite/Ew4.Legacy/LegacyTerrain.cs` | LegacyTerrainRules.FromXml/Resolve | 地形移动、防御分类解析 |
| `rewrite/Ew4.Legacy/LegacyBuildingCatalog.cs` | Read | 建筑类型、等级及首都定义 |
| `rewrite/Ew4.Legacy/LegacyRecruitmentCatalog.cs` | Read/Offers/Installations/Transport | 招募/建造/运输卡和费用 |
| `rewrite/Ew4.Legacy/LegacyCombatCatalog.cs` | Read/BattleRules/TurnRules | 原生数值表到战斗/回合规则 |
| `rewrite/Ew4.Legacy/LegacyCommanderAccounts.cs` | Read/FromDefinition/Rules/DeploymentRules | 将领账户、继承、折扣与部署 |
| `rewrite/Ew4.Core/Combat.cs` | Ew4Random.Next；Ew4Combat.AttackCount/Calculate/Rolls/Damage | 随机数、攻击次数、主动/反击及最终伤害 |
| `rewrite/Ew4.Core/WorldBattle.cs` | Preview/Attack；Transaction.ConsumeWeapon | 后端战斗事务；consumed_on_attack武器开火后移除 |
| `rewrite/Ew4.Client/CampaignProgress.cs` | RemainingYellow/Settle | 有效设施黄圈计数与隐藏关解锁 |
| `rewrite/Ew4.Core/Commanders.cs` | Ew4Commander.Attach/Resolve/Equip | 将领装备能力汇总；技能也分散于其他玩法类 |
| `rewrite/Ew4.Core/TerritorialJurisdictions.cs` | TerritorialJurisdictions.FromAssignments/Create；JurisdictionClaims.Move/DestroyFort/CanBuildFort | BTL手工划区优先/无扩展自动生成、固定辖区、中心占领、堡垒范围及建造资格 |
| `rewrite/Ew4.Core/WorldDiplomacy.cs` | Provoked/NeutralTo/Hostile | 双边关系、玩家视角中立标志和敌对判定 |
| `rewrite/Ew4.Client/ClientGame.cs` | Dispatch/Load及状态/行动分发 | 客户端宿主和保存恢复入口 |
| `rewrite/Ew4.Client/ClientSaves.cs` | SavePath/ReadSlot/Saves/AutoSave | 七槽路径、模式分区、自动档 |
| `rewrite/Ew4.Client/ClientHeadquarters.cs` | Profile属性及账户流程 | account.json及指挥部操作 |
| `rewrite/Ew4.Legacy/HeadquartersPresentation.cs`、`rewrite/Ew4.Web/wwwroot/headquarters-presentation.js` | 指挥部完整布局校验/渲染与命令绑定 | API 3 headquartersPresentation，支持增删按钮、自定义子页及布局 |

## 前端显示

| 源码路径 | 类/函数 | 负责内容 |
| --- | --- | --- |
| `rewrite/Ew4.Web/wwwroot/mods.js` | 管理器模块 | Mod列表、导入、进度轮询、启用重载 |
| `rewrite/Ew4.Web/wwwroot/app.js` | scenarioList/updateConquestPagination/turnConquestPage/chooseScenario | 征服页、灰框、翻页及选国 |
| `rewrite/Ew4.Web/wwwroot/menus.js` | Menus.open/previewChapter/openChapter/slideInSidebar | 战役大章、说明弹框、章节条目及滑入 |
| `rewrite/Ew4.Web/wwwroot/menus.js` | Menus.portrait/card/lettering | 将领头像、卡片和字库文字 |
| `rewrite/Ew4.Web/wwwroot/original-ui.js` | TextRenderer.prepare/layout/element | 自带TTF、固定文字角色、白填充黑描边；旧位图度量不控制当前文本 |
| `rewrite/Ew4.Web/wwwroot/app.js` | labelImage/originalLabels/uiImage/sprite | 预渲染文字、裁剪图、动态图集 |
| `rewrite/Ew4.Web/wwwroot/app.js` | loadLibrary/animation/playCombat/playMovements | 动作库、层级绘制、战斗同步和移动 |
| `rewrite/Ew4.Web/wwwroot/effects.js` | Effects.motion/firstEvent/lastEvent/timeline/emit/sound/trail | 时间轴、粒子、声音、灰尘 |
| `rewrite/Ew4.Web/wwwroot/effects.js` | DamageNumbers.add/frame | 白色伤害/红色暴击浮字 |
| `rewrite/Ew4.Web/wwwroot/battlefield.js` | Renderer；flagFrame/flagPosition/flag/flags | 战场、国旗帧和锚点；labels负责地点文字 |
| `rewrite/Ew4.Web/wwwroot/map-surface.js` | Surface类 | 地图分块底图显示 |

## 构建与测试

`tools/publish_mod_kit.ps1`发布离线工具及文档。`rewrite/Ew4.ModChecks/Program.cs`是构造样本回归，`RealApkChecks.cs`是工作区真实APK导入/启动/保存重载检查；两者不能互相替代。测试报告在`rewrite/reports/`，普通发行包不包含开发报告。

新增技能需要同时考虑触发点、规则结算、描述/图标、将领映射和存档兼容。不是把所有效果都堆到Convert里；Convert只负责数据转换，伤害以Core为准，前端动画不决定伤害。

完整迁移步骤及规划边界见[移植指南](porting/README.md)。layouts被导入不代表控件/事件全部支持；对照MenuArt、Menus、app.js的读取和绑定点确认，不把规划中的界面解释器或规则配置器当作现有函数。
