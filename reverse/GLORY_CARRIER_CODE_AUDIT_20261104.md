# 将荣整合版普通航母代码核查

2026-11-04。按用户要求直接检查本地《将军的荣耀：整合优化版.APK》的实现，参考对象只限40501普通航母，不以轻航母分支作为普通航母结论。静态反编译与配置交叉核对，未运行参考游戏，未改游戏、R代码、民国运行Mod或玩家资料。

2026-11-05设计决定：用户确认按本报告核实的普通航母机制制定民国方案，覆盖此前扫射需求；采用总部等级4/5/6/7格、蓝色渐变圆盘、舰载轰炸与空袭伤害路径、主攻无目标普通反击、本体受普通攻击仅1格具反击距离资格。只更新[策划](../颓垣饿鸠-策划/CARRIER_AND_FIELD_HOSPITAL_20261104.md)，尚未实装；下列代码证据与核查范围保持。

## 1. 输入与方法

原包111831802字节，SHA-256：`cf7e248f9c4b3bc93c4967b510e6634a2e0e0c4c8d6ba28da5dc8ebc0ac70bfb`。

使用GDRETools/gdsdecomp官方GitHub Release v2.7.4，将7个GDSC版本101脚本按4.5.0-stable（ebc36a7）反编译，全部成功。只运行反编译器，未运行APK或还原脚本。核查重点为assets/scripts/original_battle.gdc；hq_rules.gdc用于确认总部升级字段。其余5脚本定点搜索，未全文通读大型战斗脚本。

复核命令模式：`gdre_tools.exe --headless --decompile=<original_battle.gdc> --bytecode=4.5.0 --output=<临时目录>`。下文行号指该工具此次还原的original_battle.gd，源码临时副本清理后可从原APK按同方法复现；记录保留必要短摘录与方法名。

| APK成员 | 字节 | SHA-256 |
| --- | ---: | --- |
| assets/assets/json/ArmySetting.json | 15940 | 160195d68ffdebe7369c9e742c5b02aeb5a255877b555b2f8124e6e6db4e5052 |
| assets/assets/json/ArmyFeatureSettings.json | 234 | 48b67648a2fb35462f3b88741f74ef85178260cc43d32f1b6ccaaec2c43c65e6 |
| assets/assets/json/BuffSteeing.json | 3996 | 66a87546cbd422beb4136354d97d73d3fd7f112e00f9e9b7e90e7fd86575376f |
| assets/assets/config/def_buff.xml | 14673 | d3174f169b8ad859cbdcbfb10e40810c033126d6fbbff3108d1ccb460f4f1125 |
| assets/assets/json/AirEffectSettings.json | 3617 | ae9b80b4c9ce45cacde94754dd5336c3649233900182fa09594c434621f5cfb6 |
| assets/assets/json/CountryArmyBonusSettings.json | 21922 | 9e7cf4c763900a9d2bf4ae13b62df473630bacc94f37857714ca15c2f9e5cf5e |
| assets/scripts/original_battle.gdc | 479491 | 864de1fd189039bc49ecdc86219c3e6816591845b3e5d5dce2549d9886c6da6b |
| assets/scripts/hq_rules.gdc | 31309 | 6f5bab6f7dafd72084c640e8ca20c82dbcb54f17682d8f4ea77c0887e5f54ea4 |

还原original_battle.gd为638139字节，SHA-256为953e20f68d2554ad5afd3c5c52f1ce09a8fa9372defbf7b7cb504df94bbdeecb。

## 2. 身份与特殊射程

ArmySetting.json普通航母：Id=40501、Army=15、Model=aircraft carrier、Type=4、HP=250、minatk=2/maxatk=9、MinRange=MaxRange=1、Feature=[6]。这里只记录参考参数，不直接导入民国数值。

特殊射程完整链：

1. ArmyFeatureSettings.json：Feature6对应BuffId1091。
2. BuffSteeing.json：1091属于Type8，Range=-1。
3. def_buff.xml：Type8名称“航母空袭射程”，range_negative时将最小距离设为1，最大距离交给range_max_airport。
4. unit_air_strike_range（2467行）读取该效果，最大距离调用country_airport_range(owner)。unit_attack_range（11243行）及attack_map（10125行）使用这个扩展后的范围，而非基础MaxRange=1。

country_airport_range（11350行）实质按hq_building_level(owner, 4)决定：

| 总部对应建筑槽4的有效等级 | 航母主攻最大格距 |
| --- | ---: |
| 0至2 | 4 |
| 3至5 | 5 |
| 6至8 | 6 |
| 9及以上 | 7 |

玩家在非game_mode=2时使用HQ.active_building_level，即玩家档案building_active_levels[4]并受已解锁building_levels[4]限制；其他情况读该国家building_levels[4]，见hq_building_level（11667行）、hq_rules.gd的active_building_level。不是要求航母附近必须有地图机场，也不是固定5格。

```gdscript
var level = hq_building_level(owner, 4)
if level <= 2: return 4
if level <= 5: return 5
return 7 if level >= 9 else 6
```

## 3. 蓝色圆形区域与目标选择

draw_airport_circle（12506行）在选中带上述特殊射程效果的可见单位时，以该单位当前area为中心绘制蓝圈。不是仅进入额外空袭菜单才显示。

```gdscript
color = Color8(0, 0, 128, 79)
var c: Vector2 = screen_pos(data.areas[area_id].pos)
var r: float = (country_airport_range(owner_id) + 0.5) * 80.0 * zoom
var clear = Color(color.r, color.g, color.b, 0.0)
```

随后按10度一段绘制36个三角形，三顶点颜色为[clear, color, color]：中心透明、边缘为RGB(0,0,128)、alpha79/255（约31%）。这是蓝色渐变圆盘，不是六角格边框。世界坐标纵向格距80，显示半径额外加0.5格并随zoom缩放。

实际目标不是按屏幕点是否落在蓝圈内判断。attack_target_ok（10117行）先检查敌我、可见性、target.filter、攻击对象规则，再算grid_dist，要求min_range <= dist <= max_range；grid_dist（9909行）为六角轴/立方坐标格距。attack_map收集合格目标；点击处理在9872行检查attackable.has(area)后调用attack(selected, ui)。已行动或混乱时attack_map为空。

因此参考版的圆是视觉提示，逻辑是六角格范围，两者边缘不严格重合。例：原点(0,0)、目标(3,3)，grid_dist=5，世界坐标差(207,280)、距离约348.2；主攻R=4时显示圆半径360，目标中心虽在圆内，逻辑仍拒绝攻击。不能把该参考描述成“像素圆内所有目标必定可打”。民国版如要求提示完全对应合法目标，须额外设计目标高亮/范围提示。

## 4. 普通航母主攻实际是舰载轰炸

begin_combat（10154行）检测到特殊射程效果后直接进入空中打击路径，跳过normal_attack及其普通反击流程。普通航母Army=15，因此下面表达式取4：

```gdscript
start_bomber(a.owner, a.area, d.area, 6 if a.type == 14 else 4, false, for_ai)
```

对普通航母取air_kind=4，不使用被排除的type14分支。AirEffectSettings.json明确：4为“舰载轰炸”，对应Rows的bombing，原动画bombing，海域目标seabombing；fallback为effect_bombing.xml。start_bomber（10234行）使用bomber贴图；update_bomber（10262行）飞过目标、投弹，_bomber_land与apply_air_result完成结算。其场景飞机从目标右侧飞入、向左飞出，不是沿舰体到目标的真实航线起降。

air_strike_attack（10575行）对kind4使用bomber/Army18的航空攻击定义，叠相关招募与将领修正，非直接拿航母基础舰炮2至9当空袭伤害；普通kind4只结算选中目标，只有另一个核打击kind3才遍历周边。apply_air_result（10780行）给来源航母经验、置attacked=true、movement=0。飞机是演出，不生成地图上独立可选飞机。

本路径没有普通目标反击伤害结算，即普通航母主动轰炸不会转回normal_attack让敌方按普通射程还击；防空减伤/防空演出另有find_adjacent_antiaircraft等逻辑，不能混同为敌单位普通反击。

这与用户早先指定的民国版“飞机扫射”存在表现差异：将荣普通航母是舰载轰炸，不能拿另一型号的空袭当作普通航母证据。2026-11-05用户已确认采用本报告机制，因此现行民国设计改为舰载轰炸，并采用上述空袭伤害路径及无目标普通反击规则；具体兵种平衡数值仍单列设计。

## 5. 航母受击时为何只能近身反击

normal_attack（10470行）的防守方反击资格直接读取army_stat的基础最小/最大攻击距离，未调用unit_air_strike_range：

```gdscript
f80 = dist >= army_stat(d, "minatkrange", MIN_R[dt]) \
    and dist <= army_stat(d, "maxatkrange", MAX_R[dt])
```

普通航母基础配置1至1，因此受普通攻击时距离恰好1才有单位自身反击资格；远距不会因为主攻R变为4至7就跟着远距反击。CountryArmyBonusSettings中涉及Army15的4条修正均没有MinRange/MaxRange变化，基础1格未被这些国家修正扩大。混乱、禁反击与目标规则还会进一步过滤；设施反击f81为另一通道，不能当作航母本体射程。

总结参考机制：基础射程1管近身自卫；特殊空袭射程管远程主攻；蓝圈单独负责显示；普通航母主攻走舰载轰炸演出和空袭伤害路径。这四层分开，不能只改一个MaxRange字段复现。

## 6. 民国策划与核验边界

已核实参考版的选中蓝圈、4/5/6/7射程、距离判断、普通航母轰炸分支、主动轰炸无普通反击路径和受击本体1格反击机制。未做Android运行或截图复核，静态结论仅适用于本地该摘要版本；若游戏使用外部覆盖配置，覆盖后的规则另核。2026-11-05用户确认将这些机制用于民国设计；43主体/12特色/18专项与医院限制保持，舰体生命、价格等平衡数值仍待设计，尚未接入运行Mod。

静态交叉核验20项通过、失败0：普通型号/基础射程/Feature链、XML特殊范围、等级分段、蓝圈色值/半径/渐变、格距目标/点击分发、kind4轰炸、基础反击/无国家射程修正、行动消耗、7脚本还原结果。5份修改文档129处本地引用错误0。此为静态实现核对，不冒充战斗运行测试。

清理收据：已确认临时目录真实路径为E:\Workplace\game\o4mod\build\carrier-audit-20261104，无重解析点、Git跟踪文件0、活动工具消费者0，文件均为本轮白名单。删除18文件167535529字节（工具压缩包/程序、提取字节码、还原临时源码），目录残留0。原APK摘要重算与输入一致；必要证据在本报告，玩家服务和资料未操作。清理结果先存，再更新STATUS，最后追加WORK_LOG。
