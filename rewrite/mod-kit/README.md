# EW4 rewrite 离线 Mod 工具包

战役初始显示与隐藏使用 [campaignVisibility 模板](examples/campaign-visibility/README.md)；背景音乐可通过 [client/music.json](data/client/music/README.md) 配置主菜单、随机战斗曲目和失败曲。

全局国家表为 `client/countries.json`，独立于剧本；支持自由命名 ID、国旗/战斗/兵模分别配置，以及 BTL 内嵌 CNTR 长 ID 扩展，见[指南](docs/porting/GLOBAL_COUNTRIES.md)及[示例](examples/global-countries/README.md)。

物品/装备可复用技能 JSON 与外置 Lua：通过 `itemBindings` 授予装备主动/被动技能或定义消耗品使用效果，见[接口说明](docs/porting/ITEM_EFFECTS.md)和[可打包示例](examples/item-effects/README.md)。

原版将领技能已有[JSON 数值模板](examples/legacy-skill-parameters/README.md)：配置倍率、概率、移动/攻击加成、折扣、经验/经济和教学成长，无需逐次编译引擎。

将领专属喊话使用 [commanderDialogues JSON 模板](examples/commander-dialogues/README.md)，按定义 ID 配置击杀台词；killRules支持目标将领/兵种/剧本/回合/血量条件、概率及固定或随机选句，未配置时保留通用喊话；占领、退场字段已预留。

事件可切换同一玩家所扮演的势力、替换或恢复胜负条件、指定结局分支，见[角色与结局接口](docs/porting/SCENARIO_CONTROL.md)及[可封装示例](examples/scenario-control/README.md)。

已有原版Mod的作者先读[移植指南](docs/porting/README.md)：包含配套UI/布局/点击交互、文件和函数入口、账户与资源隔离、生效步骤及验收。指南区分当前实现和后续规划；完整界面解释器、规则/表现包组合等尚未实现，不是现成配置功能。

按原资源找rewrite位置可直接查[原包入库/运行入口对照表](docs/porting/ENTRYPOINTS.md)，包括文件、类/函数、产物路径和已核实的原SO地址。

新增开放规则入口：[API 1/2完整指南](docs/porting/RULE_EXTENSIONS.md)与[API 1示例](examples/rule-extension/README.md)、[API 2六技能示例](examples/common-skills/README.md)。支持兵种标签、主动/被动技能、动态将领技能数量/顺序/坐标、外置Lua和征服/战役胜负条件；API 2还支持召唤、传送、范围效果、持续状态及移动力。制作步骤见[Mod修改指南README](docs/porting/README.md)；扩展放包根rewrite/extension.json，电脑和Android共用加载逻辑，旧发行版需升级后才支持API 2。

剧本事件支持自定义条件、直接结果、多按钮弹窗、周期军费及独立双边外交，见[事件接口](docs/porting/SCENARIO_EVENTS.md)和[周期/外交示例](examples/scenario-recurrence-diplomacy/README.md)。可针对任意 AI 国家调拨三种资源、设置两国敌友关系，并保存周期进度。

事件还可[创建势力、复国或改名换旗](docs/porting/SCENARIO_FACTIONS.md)：支持独立兵种配置、国土颜色及包内 PNG 旗帜，可直接执行或放入选择按钮。见[可打包示例](examples/scenario-factions/README.md)。

## 按修改目标找文件

`data/`是工具的基础资源。作者请在自己的工作副本修改原格式资源，或制作覆盖包；覆盖路径没有`data/`前缀。以下文档均可离线打开。

| 修改目标 | 具体入口 | 说明 |
| --- | --- | --- |
| 背景、标题、头像、旗帜、按钮 | `client/ui/main.png`、`client/ui/title.png`、`client/ui/commanders1_hd/Napoleon.png`等 | [裁剪图](data/client/ui/README.md)、[完整图集](data/client/images/README.md) |
| 将领属性/四技能/价格 | `def_commander.xml`的`id/business/skill1...skill4/price` | [原数据字段表](data/original/README.md) |
| 兵种生命/移动/攻击与招募价 | `def_army.xml`的`strength/movement/minatk/maxatk`，`def_card.xml`的`price/industry` | [规则说明](data/legacy/README.md) |
| 征服分页/灰框/卡片图 | `frontend/conquest-pages.json`的`pages[].slots[]` | [完整JSON示例](data/frontend/README.md) |
| 新征服/新战役章节 | BTL、`def_battleline.xml`、`def_battlelist.xml`、文字表 | [分步示例](examples/README.md) |
| 地形/底图/辖区 | `def_map.xml`、地形BIN、块索引BIN、PVR、剧本BTL/JURI | [地图依赖](data/client/maps/README.md)、[手工辖区](docs/porting/BTL_JURISDICTIONS.md) |
| 中文译名/稳定文字 | `frontend/city-names.json`、原文字表、自带TTF | [文字与字体](data/client/fonts/README.md)，不沿用Mod位图字库样式 |
| 开火/受击/灰尘/音效 | `client/effects.json`、`animations/*.json`、`sounds/*` | [动画与特效](data/client/animations/README.md) |
| 升级/成长/价格系数 | `legacy/runtime_rule_tables.json`、`metagame_rule_tables.json` | [数值表字段](data/legacy/README.md) |

[制作流程](docs/README.md) · [配置索引](docs/CONFIG_REFERENCE.md) · [源码文件/函数索引](docs/SOURCE_INDEX.md) · [排错表](docs/TROUBLESHOOTING.md)。源码索引中的`rewrite/...`指源码仓库，不是发行包里缺失的文件；普通资源修改不需要编译。

电脑与 Android 的导入入口均离线工作，不需要安装被导入的 APK。导入范围受下文格式与规则边界限制，不能据此推断任意现有改版都能运行。主菜单左下角 **Mod** 打开管理界面，填写稳定的标识及名称，选择 APK、ZIP 或 `.ew4mod`，等待校验完成后启用。启用会重新加载界面；原版存档不被覆盖。

## 目录

| 目录或文件 | 内容 |
| --- | --- |
| `Ew4.ModTool.exe` | 电脑端命令行转换、校验、打包工具；自带运行环境 |
| `data/` | 与当前 rewrite 版本匹配的基础资源，工具离线转换时使用 |
| `mods/` | 导入后独立安装的内容包，目录名是内容哈希 |
| `saves/` | 该工具配置下的账户与存档；游戏有自己的存档目录 |
| `examples/` | 修改方法和最小覆盖包范例 |
| `THIRD_PARTY_NOTICES.md` | 转换库与字库的许可说明 |

## 两种制作方式

1. **基于原包的改版**：游戏内直接选择修改后的APK；也可将`assets/`内容压成ZIP。支持已识别格式的UI/头像/旗帜/兵种贴图、地图、BTL、将领XML、文字和音频；文字用rewrite自带字体，不沿用Mod位图字库样式。转换生成图集裁剪、动画、地图块和数据目录，但只执行前端已接入的布局/交互字段，大幅改UI需按移植指南适配。原文件保留在作者手里，失败不替换当前启用内容。
2. **针对 rewrite 的覆盖包**：创建 `client/`、`original/`、`legacy/` 或 `frontend/` 的所需文件，用 `pack` 生成清单并校验，再用 `import`/`export` 输出 `.ew4mod`。缺省文件使用内置版本。普通资源更新不需要重新编译或重打 APK。

```powershell
.\Ew4.ModTool.exe import "D:\mods\example.apk" --id=example --name="示例改版"
.\Ew4.ModTool.exe list
.\Ew4.ModTool.exe export <list输出的key> "D:\mods\example.ew4mod"
.\Ew4.ModTool.exe pack "D:\mods\overlay" --id=example --name="示例覆盖" --version=1.1
.\Ew4.ModTool.exe verify "D:\mods\overlay"
```

`--base=路径` 指向基础 `data`，`--mods=路径`、`--saves=路径` 可指定独立工作目录。命令行的 `activate` 只改变其 `--mods` 对应的选择；游戏中启用以游戏管理界面为准。建议所有目录放在可写位置。

## 修改生效与恢复

- 原包资源修改后重新导入；不要直接修改已安装的哈希目录。覆盖包修改后重新执行 `pack`、导入并启用。直接手改清单的哈希不会绕过规则校验。
- 同一Mod更新保持ID，不同作品不能复用ID。账户/战局按ID与逻辑指纹隔离；相同ID和指纹会共用目录。仅贴图变化时目录保持；规则/剧本/原XML或INI变化可能使用新目录，旧档保留，不自动迁移。
- 管理器同时只启用一个 Mod，防止多个包互相覆盖。切回“原版”即可恢复内置资源。导入不自动启用。
- `.ew4mod` 绑定当前基础资源版本；基础版本不同会拒绝加载，可用新版本重新导入原 APK/资源生成新包。
- Android 在应用私有目录保存导入内容；使用系统文件选择器授权读取单个文件，无需开放整个存储。手机亦可直接导入 APK。转换较大地图会使用缓存空间，请预留源文件和转换结果的空间。

## 兼容边界

先看[真实改版兼容状态](docs/COMPATIBILITY.md)：2026-10-19原版、皇凯、一战和大鏖战通过已测导入/开局及代表操作，大前线和君临天下仍有阻塞；不代表完整通关或全部UI与原生扩展已恢复。

这是资源与已实现规则的兼容层。BTL 必须是已验证的版本2，地图为原 YSAE 格式，动画为 BILE v3；PNG、ETC1 PKM、RGBA4444 PVR 可以转换。原兵种分支和旧格式0至2级可读，原技能 ID 0至39使用 rewrite 已实现的效果。

原包 `.so`/DEX 不执行。只在原生代码中新增的技能、数值表、脚本命令或新文件格式不能通过换名字/图标恢复。导入报告记录原生库哈希并提示边界，未支持的原格式技能ID等会拒绝导入。新效果可在API 1支持事件/操作中用JSON/Lua实现，超出接口的行为仍需修改rewrite代码发布支持版本。

不要在Mod中放JS、HTML、CSS、DLL、SO或EXE；只运行API 1显式注册的Lua处理函数。单文件上限256MiB、输入/转换包总计1GiB、25000文件；扩展JSON/Lua另有更小预算，见指南。贴图最长边8192、至多1600万像素。分辨率和图集坐标应配套修改，地图更换需同时提供地形、贴图索引、PVR与地图定义。大幅布局/交互变更需客户端适配，换图片不会自动实现。

本轮没有更新 Android 安装包；双端入口需要包含该功能的客户端版本。旧 APK 不会因电脑源码更新而自动增加入口。

切换内容包会重新创建游戏会话，切换前请保存。当前管理器提供导入和启用，没有删除、导出存档或撤销已保存进度的按钮。CLI的export导出内容包，不包含账户或战局。
