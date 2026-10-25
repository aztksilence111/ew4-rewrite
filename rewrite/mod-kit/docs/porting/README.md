# 原版 Mod 移植到 rewrite

地理名称可按单字或字母沿地形曲线排列，地图字段与示例见[曲线地理文字](MAP_TEXT_PATHS.md)。

将领、技能文字、兵种、装备、建筑及成长等可直接编辑独立 JSON，见[文件与字段](SPLIT_CATALOG.md)。
旧 APK/XML 导入会生成同一组文件。

商店、刷新次数恢复条件、初始资源，以及战役说明窗口UI和任务目标的入口见[局外配置与战役说明](METAGAME_AND_BRIEFING.md)。

本文面向已有欧四 APK/资源包的作者。更新时间：2026-10-21。路径以工具包或作者覆盖包根目录为准；`rewrite/...` 是源码仓库位置，不是发行包中缺失的文件。

按文件和函数找入口请读[原包入库与rewrite入口对照表](ENTRYPOINTS.md)：列出原资源、转换产物、转换函数、运行函数，以及已核实原SO函数地址和反编译证据；原版函数与rewrite不强求一对一。

国家兵模可独立编辑 `client/unit-visuals.json`，支持新增势力复用、逐兵种覆盖和自定义动作定义；国旗、战斗属性及回合顺序的独立入口见[国家兵模接口](UNIT_VISUALS.md)。

独立于所有剧本的国家表为 `client/countries.json`，长 ID、中文 ID、原版短引用兼容及 BTL 内嵌 CNTR 扩展的导出/写入/移除见[全局国家表](GLOBAL_COUNTRIES.md)。

禁行雪山与禁航海洋使用 API 3 [通用地理接口](GEOGRAPHY.md)，附模板命令和双地图示例。

## 已实现与规划必须分开

| 能力 | 当前实现 | 后续规划，当前不可依赖 |
| --- | --- | --- |
| 离线导入 | 电脑和Android导入APK/ZIP/.ew4mod，校验后明确启用 | 多Mod分组、版本详情和完整管理流程 |
| 内容共存 | 多包安装，同时启用一个；原版保留为内置资源 | 内容/规则/表现包的依赖声明与组合选择 |
| UI资源 | 转换贴图和布局树；背景拼接/裁切及部分控件读取布局 | 完整控件、交互、动画、功能绑定及专用界面适配机制 |
| UI切换 | 重载页面；按包版本区分资源地址 | 全生命周期清理、游戏请求绑定内容/会话标识 |
| 文字 | 原文本内容配合rewrite自带字体；城市白字黑描边 | 经验证的表现包字体配置接口 |
| 兵种与技能 | API 1原有能力；API 2增加地块目标、召唤、传送、范围效果、持续状态和移动力，见[规则接口](RULE_EXTENSIONS.md) | 未列出的事件/写操作、主动技能AI规划仍需支持版本 |
| 胜负条件与角色 | Mod/BTL VICT及事件动态替换；组合/目标/回合/资源/Lua、切换所扮演势力与分支结局，见[控制接口](SCENARIO_CONTROL.md) | 可视化条件编辑器 |
| 势力事件 | 条件直接结果或选择按钮中创建/复国，独立兵种、改名/国土颜色/旗帜，见[势力接口](SCENARIO_FACTIONS.md) | 任意动态改写世界对象、可视化事件编辑器 |
| 辖区 | BTL可含JURI v1逐格中心记录；缺省自动计算，战局保存固定辖区 | 含布兵/设施/事件的新剧本容器及可视化辖区编辑器 |
| 账户 | 原版与Mod按ID和逻辑指纹分目录 | 独立档案身份、ID冲突处理和版本迁移 |

当前技能入口是包根`rewrite/extension.json`及其定义/绑定/脚本引用，不是向mod.json添加skills字段。尚无`uiPackage`、任意UI功能bindings或新完整剧本容器入口。实际入口见[配置索引](../CONFIG_REFERENCE.md)与[可打包示例](../../examples/rule-extension/README.md)。

只调原版技能平衡时，使用入口的 `legacySkillParameters` 引用[JSON 参数模板](../../examples/legacy-skill-parameters/README.md)，可以调整 29 种技能的 36 个数值，无须重写技能或 Lua。此字段需包含本次增补的引擎版本；省略字段继续使用旧规则。

物品/装备扩展见[物品效果接口](ITEM_EFFECTS.md)及[可打包示例](../../examples/item-effects/README.md)：复用技能 JSON 和外置 Lua，可授予装备技能或替换消耗品使用效果。

## 使用外置代码制作新技能（API 2）

支持此更新的游戏已内置通用技能后端，作者可用包内 JSON 和 Lua 组合新技能，无须逐个技能重新编译游戏。旧发行版必须先升级到支持 API 2 的版本；直接导入新版配置不会给旧引擎增加能力。原 SO/DEX/DLL 仍不作为插件加载。

1. 在作者覆盖包根创建 `rewrite/extension.json`，保留 `schema:1`，填写 `apiVersion:2`。
2. `skills` 指向技能数组；`statuses` 指向持续状态数组；`unitBindings` 或 `commanderBindings` 绑定兵种/将领完整 ID；`scripts` 登记外置 Lua。
3. 在效果中选择通用 `operation`，用 `script/function` 计算其数值。Lua 返回数值或 `{value=数值,state={计数=整数}}`；后端统一校验、执行和保存，不能通过修改 Lua 快照直接改变世界。
4. 修改后重新 pack、verify、import、启用并开新局。先验费用、目标、冷却、保存重载，再验死亡/外交/回合结算。

| 制作目标 | 技能定义的关键字段 |
| --- | --- |
| 召唤部队 | `target:"cell"`；效果 `operation:"summon", profile:"完整兵种ID", value:1` |
| 传送自身 | `target:"cell"`；效果 `operation:"teleport"`；仅友方/无主合法落点，不占领领土 |
| 范围伤害/治疗 | 效果 `scope:"area", radius:1, relation:"enemy"` 或 `"ally"`，操作为 `damage/heal` |
| 中毒、鼓舞等状态 | 效果 `operation:"apply_status", status:"my.status", duration:3, value:1`；状态自身声明回合和战斗效果 |
| 清除指定状态 | 效果 `operation:"remove_status", status:"my.status"` |
| 恢复/扣减当前移动力 | 效果 `operation:"movement", value:3` 或负数；不重置行动标记 |
| 修改移动上限 | 被动技能或状态内 `event:"movement.modify", operation:"movement_add", value:3`；可为负数 |
| 限时减伤 | 状态内 `event:"combat.modify_incoming_damage", operation:"damage_multiplier", value:0.8` |

`range` 是施法距离，`radius` 是效果范围，单位均为六角格。状态时长按受影响单位所属方的回合结束递减；同名状态按 `refresh/replace/stack` 处理。范围效果只扣一次费用、记录一次冷却，单目标计算失败则整次动作不提交。玩家和 AI 单位都使用被动与状态规则；AI 尚不主动选择自定义主动技能。

可直接使用[六技能完整示例](../../examples/common-skills/README.md)，其中包含召唤、传送、范围炮击、群体急行军、叠层中毒和解毒。所有字段、Lua上下文及规则边界见[完整技能接口](RULE_EXTENSIONS.md)的“API 2 通用技能”。

## 1. 备份并列出改动

保留原APK、资源、作者源文件及原版对照，不编辑已安装哈希目录。记录名称、稳定ID、版本和源文件SHA256，不同作品不要复用ID。至少记录以下差异：

- 视觉：主菜单、战役/征服背景、按钮、头像、旗帜、兵种、特效。
- 布局与交互：位置、尺寸、点击范围、新页面、分页、弹窗、遮挡、动画、返回路径。
- 内容：章节、剧本、地图、国家、将领和文字。
- 规则：技能、爵位、兵种行为、招募和经济，以及只在SO/DEX中修改的内容。

rewrite不执行导入包的SO/DEX。原脚本或原生代码产生的行为不能仅从图片恢复；需作者描述、提供对照，或逆向确认后实现。不能删关卡、截断爵位或把未知技能换成旧技能来冒充完整移植。

## 2. 准备输入并离线转换

电脑和手机均可直接选择APK，不必先安装原Mod。资源ZIP中文件位于根目录或统一`assets/`下，保持原相对路径。完整APK被引用的BTL/地图必须存在，不假设自定义地图能从原版补齐。

电脑CLI示例，在工具包根目录运行：

```powershell
.\Ew4.ModTool.exe import "D:\mods\history.apk" --id=history-mod --name="历史改版"
.\Ew4.ModTool.exe list
.\Ew4.ModTool.exe verify "D:\ew4-mod-kit\mods\<list中的key>"
.\Ew4.ModTool.exe export <list中的key> "D:\mods\history-mod-1.0.ew4mod"
```

尖括号内容是待替换参数；输出文件必须不存在。CLI安装/存档目录独立于游戏，在目标客户端导入导出文件后才能启用。转换失败不替换当前启用包。已转换资源修改后重新pack/import，不手改清单哈希。

## 3. UI按整个界面移植

目标是默认加载Mod配套的视觉、布局和交互，而不是换背景后继续使用所有原版按钮。**当前尚未实现任意原布局自动完整还原。** 布局树被保存不代表前端使用其每个属性或事件。

| 修改目标 | 原输入或转换产物 | 函数入口与边界 |
| --- | --- | --- |
| 主菜单背景/标题 | `layout-568h.xml`及引用纹理；`manifest.layouts.form_mainmenu` | `LegacyModImporter.ConvertMenuAssets`、`MenuArt.prepare`按bg1/bg2裁切、旋转、缩放；标题引用独立读取 |
| 战役/征服选择背景 | `form_selcampaign`、`form_selconquest`的`back/tx/ty/tw/th` | `ConvertMenuAssets`、`MenuArt.prepare/apply`；两页和战略预览可能用不同纹理 |
| 纹理映射 | `client/manifest.json.menuTextures`的file/ratio | `MenuArt.texture`；HD比例须与布局裁切匹配 |
| 大章按钮 | `layouts.form_selcampaign.children`，如`btn_selcamp_<章节ID>` | `Menus.open/previewChapter`读取对应布置；新交互不因写入children自动生效 |
| 招募/商店/指挥部/返回 | 配套布局、`client/ui/`及`client/images/` | `menus.js`、`app.js`、`index.html`仍有具体控件和事件绑定；大改需客户端适配 |
| 动作/音效 | `client/animations/`、`effects.json`、`sounds/` | `Effects.motion/timeline`、`app.playCombat`等；文件存在不等于已绑定触发 |
| 点击范围 | 原控件区域和遮挡关系 | 对照实际DOM事件与定位，不能只移动图片、留下旧点击区 |

`manifest.json`是完整替换，不做局部JSON合并。改源图集后重新导入，生成配套坐标、裁剪图和引用。核对HD/SD、透明边距、锚点、旋转和图层顺序；只改images可能仍读旧ui裁剪图。

主菜单/选择页背景优先按布局引用的完整纹理生成。只换`client/ui/main.png`等派生背景，不能假定覆盖此路径。直接派生PNG覆盖的补充源码已加入，但截至本次记录尚未完成最终回归和发布；作者需验证发行版本，必要时从原布局及源纹理重新导入。

### 大改界面的操作顺序

1. 逐页记录原截图、控件、执行命令、点击区、入场/退出动画和返回目标。
2. 先转换资源，列出已恢复、显示偏差、点击偏差、行为缺失，不把能开首页当完成。
3. 已接入字段改源资源后重导入；未被读取的属性接入布局解释或专用适配器，不虚构配置开关。
4. 新业务操作先实现后端规则与命令，再绑定控件。UI不能直接决定伤害、资源或胜负。
5. 同时验收画面和操作；图片正确但点不动仍属于未完成移植。

未来独立表现包只能替换声明兼容的界面，默认使用Mod自带界面。其他作品风格需布局和功能适配，不能承诺任意贴图包直接互换。稳定的外层管理入口应避免被Mod菜单遮蔽；这些是规划要求，不是当前已发布接口。包内JS/HTML/CSS不会作为自定义界面程序执行。

## 4. 文本与字库样式分开

文本取自`stringtable_cn.ini`、`def_areaname.xml`、将领定义等，城市中英对应在`frontend/city-names.json`。FNT/PNG字形、颜色、描边、advance和行高不再决定UI文字。

`original-ui.js`的`TextRenderer.prepare/layout/element`使用六份内置WOFF2（Noto Serif SC、Jigmo、Unifont）及固定字号角色，不使用系统字体；`coverage.json`记录实际覆盖范围，详见`data/client/fonts/README.md`。城市白填充黑描边。历史`manifest.fonts`、旧`ui.labels`不是现行样式配置；改`preserveColor`或重画位图字库不会修复现行描边。标题logo等文字美术仍属于图片。

中英文分别测试，自创名没有翻译时保留原名，作者需补译名，不能由字库图案推导。许可和具体入口见[fonts README](../../data/client/fonts/README.md)。

## 5. 剧本、将领与规则一起核对

| 修改目标 | 当前入口 | 生效与验证 |
| --- | --- | --- |
| 将领属性/四技能 | `def_commander.xml`；`LegacyCatalogConverter.Convert`、`Ew4Commander.Resolve` | 改原XML后重导入；只改描述/图标不改变效果 |
| 兵种属性/费用 | `def_army.xml`、`def_card.xml`；`LegacyCombatCatalog`、`LegacyRecruitmentCatalog` | 核对显示stats和后端catalog，在新局招募/攻击 |
| 战役/征服 | BTL、`def_battleline.xml`、`def_battlelist.xml`、地图及配套数据 | 核对清单、入口、国家选择和全部目标剧本 |
| 征服分页 | `frontend/conquest-pages.json` | 配置槽位并登记剧本；空框不代表已有新剧本 |
| 辖区 | BTL的JURI扩展；`LegacyBattleExtensions`、`TerritorialJurisdictions.FromAssignments/Create` | 显式记录优先、无记录才自动生成；制作与协议见[手工辖区指南](BTL_JURISDICTIONS.md) |
| 新原生行为 | `Ew4Combat`、`WorldBattle`及相应玩法模块 | 实现语义、测试并发布支持版本，单改XML不会生成未知效果 |

增加爵位要配套生命、治疗、商业、经验和培养规则。精确`strength=-999`已按确认含义映射为一次性武器，不代表任意负生命值都支持。逐包状态见[兼容报告](../COMPATIBILITY.md)。

## 6. 多Mod、原版与账户隔离

当前多包安装、同时启用一个。原版账户在存档根目录；Mod在`mods/<id>/<logicRevision>/`内有自己的`account.json`和`saves/`。相同ID和逻辑指纹会共用档案，不是每个安装key都新建账户。不同作品必须用不同ID；安装key、ID和逻辑指纹不是一个值。

规则/剧本/原XML或INI变化可能产生新逻辑目录，旧档保留但不自动迁移。CLI export只导出内容，不含将领账户/战局；备份完整档案，不只复制account.json。具体路径见[saves README](../../saves/README.md)。

`ModStore.ReadFrom`只从当前包覆盖或内置资源读取，不借其他已安装Mod；原版只读内置。`FrontendResources.Frontend/ReadContent`提供原版/每包不同的版本URL，带版本的旧资源请求仍读取所属包。

当前基础回退尚未按完整资源组限定，游戏API尚未统一绑定内容/会话标识，不能宣称所有切换竞态和依赖组合已隔离。明确依赖范围、独立档案身份、拒绝旧操作请求和统一切换清理均属后续规划。

## 7. 发布前验收并记录边界

使用测试档案，记录客户端/工具版本、Mod ID/版本/源hash、转换警告、测试范围和已知缺失行为：

1. 主菜单、章节、选国、指挥部、商店、招募逐页对比原包。
2. 点击可见控件中心与边缘，确认没有旧点击区、遮挡、失效返回或空按钮。
3. 检查弹窗、滑入、按钮状态和动画，电脑/手机目标分辨率分别测试。
4. 验证本包修改的将领/技能、攻击反击、费用、回合、一次性单位等实际行为。
5. 每类目标剧本开局并保存，退出重启后加载，确认辖区、事件和随机状态恢复。
6. 原版 → Mod A → Mod B → 原版往返，核对背景/图集/音效/布局及账户/装备/货币/存档归属，使用差异明显的数据。
7. 测试缺失引用、切换失败、重复ID和旧版本存档；记录阻塞，不隐藏错误或删除数据。

导入成功证明已执行的格式/引用/开局校验，不证明UI完整还原、长期AI正确或所有原生扩展已实现。自动化用独立实例和档案，不覆盖用户进度。源码位置见[函数索引](../SOURCE_INDEX.md)，异常排查见[排错表](../TROUBLESHOOTING.md)。
