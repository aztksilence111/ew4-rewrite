# 最小覆盖示例

本目录保留可复用的 Mod 接口演示源码与说明。测试包、随机档案、截图日志及重复导出产物在任务结束或正式版覆盖验证后清理，不随示例长期保存。

[阵营配色源码示例](map-colors/README.md)配置指定阵营颜色、默认色池及逐剧本覆盖；本轮不提供打包分发文件。

[暂停目标栏 UI 示例](pause-objectives/README.md)配置边栏尺寸、行距、颜色和分区顺序，胜负及评价读取剧本实际规则。

配置兵种档位的编制数量（1–99）、超过三编的数字表示，见[编制数量模板](formation-counts/README.md)；空模板保留原版数量。

商品位/库存、初始资源、刷新次数恢复条件，以及战役说明UI和实际任务目标，见[商店与说明窗口示例](feedback-config/README.md)。默认无限刷新，示例首关目标为坚守至第3回合。

给装备授予技能、替换消耗品效果及引入外置 Lua，见[物品/装备示例](item-effects/README.md)。

独立国家表、任意形式国家 ID、兵模/旗帜/战斗属性分别配置与事件复用见[全局国家示例](global-countries/README.md)。完整 ID 可通过 CNTR 扩展写入 BTL。

只修改原版将领技能的倍率、概率、折扣、移动/攻击加成与教学成长，见[原版技能参数模板](legacy-skill-parameters/README.md)。

修改全局或指定剧本的上将派遣名额，见[上将名额模板](deployment-limits/README.md)；空模板保留当前限制。

给已有或新增将领配置专属喊话，见[将领台词 JSON 模板](commander-dialogues/README.md)。当前消费击杀通知，占领和退场字段仅作预留。

多阶段修改胜负条件、替换完整结算布局和按钮绑定见[三阶段胜利示例](victory-stages/README.md)。

叛乱后选择扮演、动态胜负条件和分支结局见[角色切换示例](scenario-control/README.md)。

自定义剧本条件、直接结果和多个选择按钮见[剧本事件示例](scenario-events/README.md)。

创建独立势力、选择不创建、修改国号/国土颜色/旗帜见[势力事件示例](scenario-factions/README.md)。

固定回合扣军费、AI 三资源支援、资源短缺结果和阶段性双边外交见[周期与外交示例](scenario-recurrence-diplomacy/README.md)。

新增技能/标签、七技能将领、Lua和征服胜利条件可直接从[可打包规则示例](rule-extension/README.md)开始；详细字段见[API 1](../docs/porting/RULE_EXTENSIONS.md)。

召唤、传送、范围炮击、群体急行军、叠层中毒与解毒见[API 2六技能示例](common-skills/README.md)。这些通用后端操作可以由作者用JSON/Lua组合，不必为每个技能重新编译；需要支持API 2的游戏版本。

只换主菜单背景：在作者自己的空目录下创建 `client/ui/main.png`，保持原图比例与足够分辨率。执行：

```powershell
.\Ew4.ModTool.exe pack "D:\mods\my-overlay" --id=my-overlay --name="我的界面" --version=1.0
.\Ew4.ModTool.exe import "D:\mods\my-overlay"
.\Ew4.ModTool.exe list
.\Ew4.ModTool.exe export <key> "D:\mods\my-overlay.ew4mod"
```

将生成的文件直接在电脑或Android的游戏主菜单左下角Mod入口导入并启用。

只换将领头像：同样结构放入 `client/ui/commanders1_hd/<将领资源名>.png`，例如 `Napoleon.png`；应以 manifest 图集与实际头像路径为准。修改图集布局时请改原PNG/PKM和XML后重新导入，避免裁剪坐标失配。

修改将领属性：复制原 `assets/def_commander.xml` 到作者原资源目录，修改已有字段和0至39的技能引用，压为ZIP后导入。转换器重新建立catalog，并自动隔离逻辑变化后的存档。

新增征服：沿用BTL版本2的结构，设置合法地图、国家、坐标和事件，文件命名 `conquest7.btl` 等。原资源导入会追加菜单页；必要时再用 frontend 配置安排卡片图。新增战役需要同步 `def_battlelist.xml`、`def_battleline.xml`、文字和BTL，新增章有通用按钮布局。

不要把Android安装包改名成.ew4mod；两种格式分别识别，.ew4mod需要校验清单。源文件请留备份，勿在唯一原件上试验。

## 例一：只换拿破仑头像

核对manifest的`atlases.commanders1_hd["Napoleon.png"]`和`data/client/ui/commanders1_hd/Napoleon.png`，基础图为156×156。在自己的覆盖目录放`client/ui/commanders1_hd/Napoleon.png`，保留透明边距；执行pack/import/list/export，在游戏导入启用并打开将领查看。同尺寸换图不需附加manifest；战场若使用另一图集，还需另改对应images/动画。

新增将领或改名还需要XML、文字和图集登记。`Menus.portrait()`从manifest查图集；新文件存在但没有登记新名字，不保证可见。

## 例二：已有将领的能力与四技能

完整def_commander.xml找到id1，只修改business或现有skill字段；整份XML放ZIP根目录导入。技能0对应skill_01图标，-1为空；新技能40～50不能用旧技能顶替。说明文字在name_skill_XX/desc_skill_XX，图标在image_ui等图集；属性、文字、图像各自配套不等于编写了新效果。

## 例三：新增第七征服

1. 使用兼容编辑器将已能运行的BTL另存conquest7.btl，修改合法地图/国家/坐标/事件，不把JSON改扩展名当BTL。
2. 原资源ZIP放该BTL；新地图/国家/文字同步提供对应完整文件。标题可用stringtable里的text_conquest_age_7。
3. 通过原资源流程导入，导入器并入scenarios并追加菜单页；核对选国和实际新局。
4. 卡片排版配置见[frontend说明](../data/frontend/README.md)。进阶覆盖包修改含新场景的导出结果时，注意CLI pack默认采用基础scenarios，不自动扩充。开发接口`ModStore.Seal(..., scenarios:完整清单)`才可显式指定；当前CLI无该参数。普通作者建议维护原资源输入并重导入，不手改校验清单。

## 例四：追加战役大章

def_battleline.xml的line顺序决定章号，第七条对应campaign7_XX。def_battlelist.xml的file对应实际BTL，name对应文字，age/commander/centerx/centery决定介绍/战略标记。line的name、每关title_/desc_文字和国旗资源需配套。

前六章布局保留；新增章由`Menus.open()`走custom-chapter，右栏底图由`openChapter()`回退现有图。当前没有大量大章的自动分页保证，不能把通用按钮回退理解成无限布局已就绪。

## 例五：修改训练资金费

复制完整runtime_rule_tables.json到作者覆盖包`legacy/runtime_rule_tables.json`，只改training_money对应项，保留schema/source和其他表。pack/verify后新局检查确认框与实际扣款，保存重载；级别行/经验表也变化时须配套，不能只改按钮文字。

## 发布前实测

核对将领、进入征服/战役、实际移动/攻击/招募/回合、保存重载、切回原版看原档。导入完成不等于长期玩法兼容；不要删掉失败剧本或回退原图后仍称原Mod完整兼容。错误定位见[排错表](../docs/TROUBLESHOOTING.md)。
