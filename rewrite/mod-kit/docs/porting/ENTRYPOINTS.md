# 原包入库与 rewrite 入口对照

API 3 禁行地形：`rewrite/extension.json.geography` → `GeographyCatalog.Validate/Apply` → `ClientContent.Start`；核心 `Movement.EntryCost`、`Geography.Validate`；显示 `ClientGame.CellView`、`battlefield.geography`。见[配置指南](GEOGRAPHY.md)。

2026-10-24：规则数据已拆为[独立 JSON](SPLIT_CATALOG.md)。下表的 catalog 字段指旧格式或内存合成视图；
直接编辑入口为 `legacy/units.json`、`commanders.json`、`items.json` 等。原包输入路径继续有效。

更新时间：2026-10-20。与[移植步骤](README.md)、[配置字段](../CONFIG_REFERENCE.md)、[完整源码索引](../SOURCE_INDEX.md)配套使用。这里区分“读取原包”“生成转换产物”和“实际运行”，不能只替换显示产物就认为玩法已经改变。

新规则不必伪装成原格式字段：[规则API 1](RULE_EXTENSIONS.md)通过包根rewrite/extension.json接入定义、绑定、Lua与胜负条件。核心映射为ExtensionLoader.Load/PatchCatalog → RuleEventBus.Modify/Dispatch或WorldSkillActions.Execute → ClientExtensions/skill-ui.js。BTL VICT由LegacyBattleExtensions.Victory/WriteVictory接ExtensionVictory.Evaluate；原BTL主体及JURI保留。

## 路径和函数名的读法

- 原包路径以APK根目录为准；导入assets资源ZIP时去掉`assets/`前缀，或整体保留该前缀。
- 转换产物路径以内容包根目录为准，不带工具的`data/`前缀。
- 下表的源码路径以仓库根目录为准；发行工具包不附带这些源码。函数名中的点号表示类/方法，不是文件扩展名。
- `Mods/` = `rewrite/Ew4.Mods/`，`Legacy/` = `rewrite/Ew4.Legacy/`，`Client/` = `rewrite/Ew4.Client/`，`Core/` = `rewrite/Ew4.Core/`，`Persistence/` = `rewrite/Ew4.Persistence/`，`Web/` = `rewrite/Ew4.Web/`，`Android/` = `rewrite/Ew4.Android/`。
- 表内函数名和地址是当前源码/已有逆向证据，不代表未来通用UI、技能脚本或剧本容器接口已实现。

## 一、原包文件到转换及运行入口

| 功能 | 原包入口 | 入库函数及源码文件 | 转换产物/存储位置 | rewrite读取或执行函数及源码文件 |
| --- | --- | --- | --- | --- |
| APK/ZIP/目录读取 | APK、assets ZIP或作者目录 | `Mods/ModFiles.cs`：`ImportSource.Read`；`Mods/ModStore.cs`：`ImportCore` | 临时`.staging-*`目录，验证后提交到安装key目录 | `ModStore.Validate/Activate/NewGame`，不是安装原APK后调用它 |
| 原XML兼容读取 | `assets/def_*.xml`等 | `Mods/LegacyResourceResolver.cs`：`Read`；`Mods/LegacyXml.cs`：`Normalize`；`Mods/ModFiles.cs`：`Xml` | `original/*.xml`及来源报告，规范化与回退有记录 | `LegacyRuntimeCatalog.VerifySources`及各专用Read；不接受任意损坏XML |
| 兵种属性 | `assets/def_army.xml` | `Mods/LegacyCatalogConverter.cs`：`Convert`；显示另由`LegacyModImporter.ConvertProfiles`生成 | `original/def_army.xml`、`legacy/catalog.json`、`client/manifest.json.profiles` | `Legacy/LegacyRuntimeCatalog.cs`：`FromJson/Unit/SerializedUnit`；`Core/Combat.cs`：`Ew4Combat.Calculate` |
| 将领定义/四技能 | `assets/def_commander.xml` | `Mods/LegacyCatalogConverter.cs`：`Convert`；`LegacyModImporter.Convert`生成显示名称索引 | `original/def_commander.xml`、`legacy/catalog.json`、`manifest.commanders` | `Legacy/LegacyCommanderAccounts.cs`：`Read/FromDefinition`；`Core/Commanders.cs`：`Ew4Commander.Attach/Resolve`；技能效果还分布于具体玩法模块 |
| 将领头像/旗帜/按钮图 | `assets/commanders*_hd.*`、`image_flag_hd.*`、`image_ui*.png`或PKM及配套XML图集 | `Mods/LegacyModImporter.cs`：`Convert/Atlas`；`Mods/LegacyGraphics.cs`：`Raster.Decode/Crop/Png` | `client/images/*.png`、`client/ui/<图集>/<图片>`、`manifest.atlases` | `Web/wwwroot/menus.js`：`Menus.portrait`；`app.js`：`uiImage/sprite`；战场旗另看下表 |
| 装备/物品 | `assets/def_item.xml`和物品图集 | `Mods/LegacyCatalogConverter.cs`：`Convert`；`LegacyModImporter.Convert`裁剪图集 | `original/def_item.xml`、`legacy/catalog.json`、`client/ui/image_item_hd/` | `Legacy/LegacyItemTradingCatalog.cs`：`Read`；`Core/Commanders.cs`：`Ew4Commander.Equip/Resolve` |
| 招募/要塞/设施/运输卡 | `assets/def_card.xml` | `Mods/LegacyCatalogConverter.cs`：`Convert`；`LegacyModImporter.Convert`保留原定义 | `original/def_card.xml`、`legacy/catalog.json` | `Legacy/LegacyRecruitmentCatalog.cs`：`Read/Offers/Installations/Transport`；`Core/WorldActions.cs`：`WorldActions.CanRecruit/Recruit` |
| 建筑/首都 | `assets/def_construction.xml`、`def_capital.xml` | `Mods/LegacyModImporter.cs`：`Convert` | `original/`对应XML；`manifest.buildings/capitals` | `Legacy/LegacyBuildingCatalog.cs`：`Read`；`LegacyScenarioMap.Import`、`LegacyCombatCatalog.Read`使用目录，不从图片推算收入或防御 |
| 地形规则 | `assets/def_mapterrain.xml`、`def_terraintype.xml` | `Mods/LegacyModImporter.cs`：`Convert`保留XML；相关定义进入catalog | `original/`对应XML及`legacy/catalog.json` | `Legacy/LegacyTerrain.cs`：`LegacyTerrainRules.FromXml/Resolve`；`LegacyCombatCatalog.Read`接防御等规则 |
| 剧本BTL | `assets/campaign*.btl`、`conquest*.btl`、`tutorials*.btl` | `Mods/LegacyModImporter.cs`：`Convert`登记剧本并保留源字节 | `original/<剧本>.btl`、`mod.json.scenarios` | `Legacy/LegacyResources.cs`：`LegacyBattle.Read`；`Client/ClientContent.cs`：`Battle/Start`；`Legacy/LegacyScenarioHost.cs`：`Start` |
| 势力/开局布兵 | BTL中的势力/部队记录 | 同上；不把BTL改写成另一套原格式数据 | 保留原BTL；运行时转换为WorldState | `Legacy/LegacyScenarioInitialization.cs`：`Prepare`；`Legacy/LegacyScenarioHost.cs`：`Start` |
| 设施归属/城市名 | BTL设施记录及`assets/def_areaname.xml` | `Mods/LegacyResourceResolver.cs`：`CompleteCityLabels`；`LegacyModImporter.Convert` | `original/`源文件及补名来源；不是底图文字图片 | `Legacy/LegacyScenarioMap.cs`：`Import`；`Client/ClientContent.cs`：构造函数、`Start`；`Web/wwwroot/menu-art.js`：`cityName` |
| 目标/事件/对白 | BTL目标/事件，`assets/def_dialogues.xml`及`stringtable_cn.ini` | `Mods/LegacyModImporter.cs`：`Convert`保留原资源 | `original/`源文件；运行时进入事件与目标状态 | `Legacy/LegacyScenarioEvents.cs`：`Import`；`Core/WorldEvents.cs`：`Process/Acknowledge`；`Core/WorldVictory.cs`：`Facts/Evaluate` |
| 战役大章 | `assets/def_battleline.xml` | `Mods/LegacyModImporter.cs`：`Convert`保留原XML | `original/def_battleline.xml` | `Client/ClientContent.cs`：构造函数生成`Chapters`；`Web/wwwroot/menus.js`：`Menus.open/previewChapter` |
| 关卡说明/战略预览 | `assets/def_battlelist.xml`和引用背景 | `Mods/LegacyModImporter.cs`：`Convert/ConvertMenuAssets` | `original/def_battlelist.xml`、`manifest.strategic/menuTextures` | `ClientContent`构造函数生成`Presentations`；`Menus.openChapter/chooseBattle`；`menu-art.js`：`MenuArt.drawStrategic` |
| 征服入口 | `assets/conquest*.btl`及`def_conquestlist.xml`等定义 | `Mods/LegacyCatalogConverter.cs`：`Convert`保留目录；`LegacyModImporter.Convert`将额外conquest剧本追加到分页 | `original/`、`legacy/catalog.json`、`frontend/conquest-pages.json` | `ClientContent`构造函数登记场景；`Web/wwwroot/app.js`：`scenarioList/updateConquestPagination/turnConquestPage/chooseScenario`；XML目录不等于任意UI入口自动生成 |
| 地图地形 | `assets/def_map.xml`和file指向的地形BIN | `Mods/LegacyResourceResolver.cs`：`CheckMapReferences`；`LegacyModImporter.Convert`保留源数据 | `original/def_map.xml`、`original/<地形BIN>` | `Legacy/LegacyResources.cs`：`LegacyTerrainMap.Read`；`ClientContent`构造函数；`LegacyScenarioMap.Import` |
| 地图显示块 | `assets/mapN_hd.bin`和`assets/map/mapN_*@2x.pvr`等配套资源 | `Mods/LegacyModImporter.cs`：`ConvertMaps`；`LegacyGraphics.cs`：`Raster.Decode` | `client/maps/`、`manifest.maps` | `Web/wwwroot/map-surface.js`：`Surface`；`battlefield.js`：`Renderer`；显示块不决定地块归属 |
| 布局树 | `assets/layout-568h.xml` | `Mods/LegacyModImporter.cs`：`Convert`；`Mods/LegacyCatalogConverter.cs`：`Layout` | `client/manifest.json.layouts` | `Web/wwwroot/menu-art.js`、`menus.js`只读取已接入字段；`app.js/index.html`仍有固定控件和事件绑定 |
| 主菜单背景/标题 | 布局`form_mainmenu`引用纹理，另有`zh_CN.lproj/title_hd.png`等素材 | `Mods/LegacyModImporter.cs`：`ConvertMenuAssets/Convert` | `manifest.menuTextures`、`client/images/`、派生`client/ui/` | `Web/wwwroot/menu-art.js`：`MenuArt.texture/prepare`按布局裁切/旋转/缩放；不是整张图直接铺满 |
| 战役/征服选择背景 | `form_selcampaign/form_selconquest`的`back/tx/ty/tw/th`及引用纹理 | `Mods/LegacyModImporter.cs`：`ConvertMenuAssets` | `manifest.menuTextures`及派生`ui/*_background.png` | `Web/wwwroot/menu-art.js`：`MenuArt.prepare/apply`；不要混用战略预览背景 |
| 文字内容 | `assets/stringtable_cn.ini`及名称XML | `Mods/LegacyCatalogConverter.cs`：`Strings`；`LegacyModImporter.Convert` | `original/`、`manifest.strings`；双语对应可用`frontend/city-names.json` | `Client/ClientContent.cs`：`Label`；`Web/wwwroot/menu-art.js`：`cityName`；`original-ui.js`：`TextRenderer.layout/element` |
| 原位图字库 | `assets/font_*.fnt`及PNG/PKM | 当前`LegacyModImporter.Convert`不导入位图字形样式；`LegacyGraphics.cs`保留历史解析器`LegacyBinary.Font` | 不把原字形/描边当现行文本样式；历史manifest可保留旧元数据 | `Web/wwwroot/original-ui.js`：`prepareFonts`、`TextRenderer.prepare/layout/element`；使用`client/fonts/*.woff2`和`coverage.json`，城市白字黑边 |
| 兵种动作绑定 | `assets/def_motion.xml`、`def_army.xml` | `Mods/LegacyModImporter.cs`：`ConvertProfiles` | `manifest.profiles[id].library/motions/x/y/scale` | `Web/wwwroot/app.js`：`loadLibrary/animation/playCombat/playMovements`；`battlefield.js`：`Renderer` |
| 动画关键帧 | `assets/<动画库>[_hd].bin`及配套XML、纹理 | `Mods/LegacyModImporter.cs`：`ConvertAnimations`；`LegacyGraphics.cs`：`LegacyBinary.Animation` | `client/animations/<库名>.json`及`client/images/` | `Web/wwwroot/app.js`：`loadLibrary/animation`；HD异常只允许成套回退同包有效SD |
| 开火/受击/灰尘 | `def_motion.xml`、`def_effectsanim.xml`、`effect*.xml` | `Mods/LegacyModImporter.cs`：`ConvertEffects` | `client/effects.json`的motions/bindings/timelines/definitions | `Web/wwwroot/effects.js`：`Effects.motion/timeline/emit/trail`；`app.js`：`playCombat/playMovements` |
| 声音/音乐 | 原包WAV/OGG/MP3及时间轴引用 | `Mods/LegacyModImporter.cs`：`Convert`和`ConvertEffects` | `client/sounds/`、`client/music/`及effects绑定 | `Web/wwwroot/effects.js`：`Effects.sound`；声音文件必须与触发引用对应 |
| 教程脚本 | `assets/tutorials_script*.xml` | `Mods/LegacyModImporter.cs`：`Convert` | `manifest.tutorials.<教程>.commands/sha256` | `Web/wwwroot/menus.js`：`Menus.beginTutorial`；`app.js`教程执行路径；不是任意新增XML命令都能解释 |

`manifest`以上均指`client/manifest.json`。在源码中查不到与资源改动对应的读取点时，不要把写入JSON当作实现了功能。完整UI绑定、独立表现包及效果配置器仍属规划，见[移植指南](README.md)。

## 二、原生函数到 rewrite 逻辑入口

本节仅针对已研究的原版ARM64库`apk_work/original/lib/arm64-v8a/lib64ew4_Sagiri.so`，SHA256：

```text
c16905a2f6d155111a1ff702f2b0f8fbad09482dcedce08785a2bf7965fe7f63
```

下列`FUN_...`是Ghidra生成的标识，不是恢复出的原作者函数名。库内相对地址加映像基址`0x100000`得到Ghidra地址，例如`0x71014`对应`FUN_00171014`。源码证据统一在仓库`reverse/native/decompiled/`，函数索引为该目录的`functions.tsv`，不随普通工具包附带。

**其他SO版本不能直接套地址。** 必须先核对库hash；表中为职责对照，存在一对多关系和明确改进，不承诺新旧实现逐指令相同。

| 原版职责 | 原相对地址 / 反编译函数 | 反编译证据文件 | rewrite对应文件及函数 |
| --- | --- | --- | --- |
| 兵种XML加载 | `0x71014` / `FUN_00171014` | `00071014.c` | `Mods/LegacyCatalogConverter.cs`：`Convert`；`Legacy/LegacyRuntimeCatalog.cs`：`FromJson/SerializedUnit` |
| 将领XML加载 | `0x74590` / `FUN_00174590` | `00074590.c` | `Mods/LegacyCatalogConverter.cs`：`Convert`；`Legacy/LegacyRuntimeCatalog.cs`：`FromJson`；`LegacyCommanderAccounts.Read`在`Legacy/LegacyCommanderAccounts.cs` |
| 建筑定义加载 | `0x69d88` / `FUN_00169d88` | `00069d88.c` | `Legacy/LegacyBuildingCatalog.cs`：`Read` |
| 首都定义加载 | `0x70870` / `FUN_00170870` | `00070870.c` | `Legacy/LegacyBuildingCatalog.cs`：`Read` |
| 行动卡加载 | `0x6aa88` / `FUN_0016aa88` | `0006aa88.c` | `Legacy/LegacyRecruitmentCatalog.cs`：`Read/Offers/Installations/Transport` |
| BTL分段加载 | `0x7cb80` / `FUN_0017cb80` | `0007cb80.c` | `Legacy/LegacyResources.cs`：`LegacyBattle.Read`；`LegacyScenarioMap.Import`和`LegacyScenarioEvents.Import`在各自同名文件 |
| 新局初始化 | `0x7dd18` / `FUN_0017dd18` | `0007dd18.c` | `Legacy/LegacyScenarioInitialization.cs`：`Prepare`；`Legacy/LegacyScenarioHost.cs`：`Start`；`Client/ClientContent.cs`：`Start` |
| 战斗数值计算 | `0x81634` / `FUN_00181634` | `00081634.c` | `Core/Combat.cs`：`Ew4Combat.Calculate/AttackCount/Rolls/Damage`；`Core/WorldBattle.cs`：`Preview`提供世界上下文 |
| 战斗结果提交 | `0x825d4` / `FUN_001825d4` | `000825d4.c` | `Core/WorldBattle.cs`：`Attack`；`Core/WorldDeaths.cs`：`Remove`；前端`app.playCombat`只负责演出 |
| 原名册记录转换 | `0x5873c`、`0x586ec` / `FUN_0015873c`、`FUN_001586ec` | `0005873c.c`、`000586ec.c` | `Legacy/LegacyCommanderAccounts.cs`：`LegacyRosterCommander.Read/ToBytes`、`LegacyCommanderAccounts.Import`；不是整份原存档自动迁移 |
| 有效经商能力 | `0x571e8` / `FUN_001571e8` | `000571e8.c` | `Legacy/LegacyCommanderAccounts.cs`：`Read/FromDefinition`读取爵位表和将领经商属性；`Core/GameSession.cs`：`SessionActions.CommerceLevel`；`Core/ItemTrading.cs`：`PurchasePrice/SalePrice` |
| 据点进入/占领事件 | `0x50e00` / `FUN_00150e00` | `00050e00.c` | `Core/WorldActions.cs`：`WorldActions.Move`；`Core/WorldEvents.cs`：`DuringAction/Process`；辖区限制由`TerritorialJurisdictions.cs`中的`JurisdictionClaims.Move`补充 |
| 将领死亡事件 | `0x4f6b8` / `FUN_0014f6b8` | `0004f6b8.c` | `Core/WorldDeaths.cs`：`Remove`；`Core/WorldEvents.cs`：`DuringAction/Process` |
| 回合事件与对白暂停 | `0x8d95c` / `FUN_0018d95c` | `0008d95c.c` | `Legacy/LegacyScenarioEvents.cs`：`Import`；`Core/WorldEvents.cs`：`Process/Acknowledge` |
| 红目标计数 | `0x58e24` / `FUN_00158e24` | `00058e24.c` | `Core/WorldVictory.cs`：`Facts/Evaluate`；原目标归属口径与现代默认有显式策略区别 |
| 红/黄圈绘制 | `0x506c4` / `FUN_001506c4` | `000506c4.c` | `Web/wwwroot/battlefield.js`：`objectiveMarker`、`Renderer.objectives`；`app.animation`播放circle动画 |
| 隐藏关结算解锁 | `0x7f300` / `FUN_0017f300` | `0007f300.c` | `Client/CampaignProgress.cs`：`RemainingYellow/Settle`；`Client/ClientHeadquarters.cs`：`StoreAccount`持久化解锁 |
| 国旗帧动画 | `0x65b14` / `FUN_00165b14` | `00065b14.c` | `Web/wwwroot/battlefield.js`：`flagFrame`、`Renderer.flag/flags`；旗杆位置另由`flagPosition`计算 |

原版布局、字库和部分界面加载器的完整原生调用关系没有在本表逐项确认，因此没有为它们编造函数名或套用其他Mod地址。原资源侧入口已在第一张表列出，后续确认函数后可追加证据。

## 三、rewrite专有入口与当前边界

| 功能 | 文件与入口 | 与原包关系/当前限制 |
| --- | --- | --- |
| 内容清单/逻辑指纹 | `Mods/ModStore.cs`：`Seal/LogicRevision/Validate` | 自动生成mod.json；不通过手改hash绕过依赖检查 |
| 包/账户选择 | `Mods/ModStore.cs`：`Activate/NewGame/ReadFrom` | 原版及Mod账户分目录；同ID和逻辑指纹共享档案，不是每安装key完全独立 |
| 版本化资源URL | `Mods/FrontendResources.cs`：`Frontend/ReadContent` | 按原版/安装key分流资源；不等于全部游戏API已做会话归属验证 |
| 电脑导入入口 | `Web/Program.cs`：`/api/mod-import`路由；`Mods/ModStore.cs`：`BeginImport` | 导入不自动启用 |
| 手机导入入口 | `Android/MainActivity.cs`：`Pick/OnActivityResult`、`ImportBridge` | 系统文件选择器，应用私有目录；不是只支持电脑转换 |
| Mod管理面板 | `Web/wwwroot/mods.js`：`api/row/render/refresh` | 当前列表、导入、启用和重载；分组/删除/档案详情属于规划 |
| 固定辖区与建堡条件 | `Legacy/LegacyBattleExtensions.cs`、`Core/TerritorialJurisdictions.cs`：`FromAssignments/Create`、`JurisdictionClaims.Move/DestroyFort/CanBuildFort` | `ClientContent.Start`在战役/征服优先读取BTL的JURI，缺省才自动划分；港口不作中心 |
| 解散和返还 | `Core/WorldDisband.cs`：`CanDisband/Refund/Execute` | 用户新增征服机制，不是恢复原版功能；将领单位禁止解散 |
| 账户/战局文件 | `Client/ClientHeadquarters.cs`：`Account/StoreAccount`；`Client/ClientGame.cs`：`Save/Load`；`Client/ClientSaves.cs`：`SavePath/AutoSave` | account.json与模式槽分开；CLI内容包export不含账户/存档 |
| 存档编码校验 | `Persistence/SessionSave.cs`：`Encode/Decode`；`Persistence/WorldSave.cs`：`Encode/Decode` | rewrite结构化快照、版本和校验，不是原内存布局；跨版本需明确迁移 |

## 按问题选择修改层

手工辖区是rewrite新增扩展，没有原SO逐格辖区入口：`assets/<剧本>.btl`尾部JURI → `Mods/LegacyModImporter.Convert`原样保留 → `Legacy/LegacyBattle.Read`与`LegacyBattleExtensions.Jurisdictions`读取 → `Client/ClientContent.Start`优先选择 → `Core/TerritorialJurisdictions.FromAssignments`校验/建立边界 → `JurisdictionClaims`执行占领。没有JURI才调用`Create`自动划分。完整格式和CLI见[手工辖区指南](BTL_JURISDICTIONS.md)。

- 图片裁切/锚点错误：先核对原图集和布局，再定位`ConvertMenuAssets/ConvertProfiles`及`MenuArt/Renderer`；不要改伤害函数。
- 图像正确但点不到：定位`Menus/app.js/index.html`的控件与事件，不反复换PNG。
- 属性显示变了但战斗没变：核对catalog、实际兵种ID、`Ew4Combat/WorldBattle`和新局，不只看manifest.stats。
- 原包新技能无效：查旧SO行为，再实现对应后端规则；技能名、ID和图标不自动生成效果。
- 切回原版串图：查`Frontend/ReadContent`的版本路径及前端对象生命周期，不把Mod图复制进内置data。
- 切换后账户不同：先核对ID、logicRevision和存档根目录；不要把一个Mod的账户强行拷给另一个作品。

发布时记录“资源转换、界面交互、玩法规则、保存恢复、双端运行”的各自测试范围。函数对照只帮助定位，不代替逐包实测，也不证明所有旧原生限制已经移除。
