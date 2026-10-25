# 势力创建、复国与改名换旗（2026-10-21 增补）

国家默认色、事件新增势力色以及 API 3 阵营地图配色的完整入口与优先级见[地块颜色指南](MAP_COLORS.md)。

事件创建后可选择转为扮演新势力，并调整胜负条件或指定结局，见[角色与结局接口](SCENARIO_CONTROL.md)。始终只有同一个玩家，切换的是所扮演的势力。

这些操作均使用现有 scenarioEvents 的条件与结果接口，无需新增触发器或定时系统。faction_create 是普通结果：既可放在事件 effects 中直接执行，也可放在某个 popup.choices[].effects 中，选择该按钮才执行；其他按钮可以不创建势力。注册方式见[剧本事件接口](SCENARIO_EVENTS.md)。原有一次性/周期、弹窗和整体事务规则保持适用。

## 创建新势力

```json
{
  "type": "faction_create",
  "faction": "civil.rebels",
  "name": "独立共和国",
  "camp": "civil.rebels",
  "unitVariant": "rus",
  "flag": "rus",
  "color": "#237A4D"
}
```

| 字段 | 规则 |
| --- | --- |
| faction | 新运行 ID，1～128 个英文字母、数字、点、下划线或短横线；不可使用 @player/@active，不可与开局国家或其他创建声明重复 |
| name / camp | 必填名称及阵营；name 最多 128 字，不接受空白或控制字符；camp 最多 128 字 |
| templateFaction | 可选，开局已有势力运行 ID 或 @player；不是必选，不能引用另一个待创建势力或 @active |
| unitVariant | 可选，运行目录 legacy/catalog.json 的 variants 键；可以引用 Mod 新增的变体，未登记的变体拒绝开局。指定后独立决定招募兵种；不指定则使用模板兵种，无模板则使用 others |
| recruitment | 可选招募卡授权字典，如 {"ew4.card.0":{"veterancy":"0","transport":false}}；显式空字典表示不能招募。省略时沿用模板授权；指定 unitVariant 或无模板时默认授权该变体所有可用招募卡 |
| flag / flagImage / color | 可独立指定，覆盖模板的对应显示；无模板时必须指定旗帜和颜色。规则见下一节 |

创建结果执行前，该势力不在世界国家列表、回合和胜利统计中；执行后作为 AI 国家追加在当前国家顺序末尾，按正常回合交接行动，不强制跳转。不会自动获得城市、部队、将领或初始资源。无模板时经济倍率为 1，无额外收入或闪避，部队或有效建筑均可维持存续；使用模板时继承其经济倍率、收入加成、闪避、存续规则及未覆盖的显示和招募配置，但不继承资源余额、击杀记录、缺粮和本轮使用次数。

可继续在同一结果数组调用 money/industry/food、unit_owner、cells_owner 和 relation。先创建，再给它转交部队，最后转交对应地块；地块上存在敌对占军时不允许直接改主。cells_owner 不自动扩张城市辖区，需把希望转交的地块逐项列入 cells。任职将领随部队转交，现有部队不替换为新兵种。unitVariant 影响后续招募，新增单位定义及显示资源仍按[移植指南](README.md)登记。

每个新 ID 只声明一处 faction_create；其他事件可提前引用该 ID，但创建前执行结果会报 scenario.faction_missing。重复创建报 scenario.faction_exists，周期事件应结合 not(faction_exists) 防止重建；已灭亡国家应复活。faction_exists 包括已灭亡国家，faction_active 表示存在且未灭亡，active_faction 才表示正在行动。未创建国家的资源、数量、所有权、阵营、灭亡和关系条件均为假。

## 修改名称、国土颜色与旗帜

```json
{
  "type": "faction_identity",
  "faction": "@player",
  "name": "新国号",
  "color": "#C43D4D",
  "flagImage": "rewrite/images/republic.png"
}
```

name、color、flag、flagImage 至少提供一项，省略的项目保持原值。适用于原有国家、事件新建国家及已灭亡国家。不改变 faction 运行 ID、玩家归属、camp、外交、领土/部队归属或招募兵种。

- color 接受 #RRGGBB 或 #RRGGBBAA；AA 为透明度，省略时不透明。国土填色与海岸颜色同步刷新，不改变敌友徽标的蓝/黑/红含义。
- flag 是旗帜图集名称前缀，例如 rus；可用 Mod 新增前缀。需提供 HUD 的 client/ui/image_flag_hd/<flag>1.png，以及 buildings 图集中的 <flag>1.png～<flag>4.png；使用原有前缀无需添加文件。
- flagImage 为包内 rewrite/ 下的独立 PNG 路径，单图不超过 4 MiB；例如 rewrite/images/republic.png。无需改旗帜图集。用于部队、城市/港口国旗及 HUD 国旗控件（沿用既有可见性）；战场旗面按 26×15 显示，建议使用相同比例。此模式为静态图片，港口不播放四帧旗面动画。
- 同一次操作 flag 与 flagImage 互斥；之后指定 flag 会切回图集模式，指定 flagImage 会切到独立图片。远程 URL、目录越界和非 PNG 路径不支持。

变化随 WorldSave4/SessionSave2 保存。旧存档缺少 Appearance 时沿用原剧本显示，未使用扩展的原剧本指纹保持兼容。事件逻辑变更仍参与规则指纹，不能直接给旧规则存档套用改过的事件文件。旧 0.2.6 及更早发行包没有本增补，须使用包含本接口的新宿主。

## 直接结果与选择结果

直接触发的结构如下；1234 仅示意，须替换为实际城市地块 ID：

```json
{
  "id": "city.rebellion",
  "condition": {"type":"cells_owned","faction":"@player","cells":[1234]},
  "effects": [
    {"type":"faction_create","faction":"civil.rebels","name":"地方叛军","camp":"rebels","unitVariant":"rus","flag":"rus","color":"#237A4D"},
    {"type":"money","faction":"civil.rebels","value":500}
  ]
}
```

需要选择时，把 effects 移入某个按钮的 effects，并让另一按钮 effects 为 []。不要把有条件执行的创建操作放在事件公共 effects 中，因为公共结果会在任一按钮被选中后执行。双分支及转交驻军示例见[势力事件示例](../../examples/scenario-factions/README.md)。

## 复活已灭亡势力

```json
{"type":"faction_revive","faction":"conquest1.faction.2.koi"}
```

ID 仅展示格式，需替换为目标国家运行 ID。国家必须存在且已灭亡；未创建或仍存续会拒绝事务。恢复 Defeated=false，清空缺粮及本轮使用次数，保留国名、显示、阵营、外交、资源和击杀记录，在原顺序位置重新行动。不恢复旧部队、死亡将领或已转交地块；请用后续结果明确配置。已经结束的对局不会被此操作重新打开。

## 原子性与入口

创建、复国、改名换旗及同数组的扣款、转交和外交属于一次事务，全部成功才提交。资源不足兜底会撤回新势力、身份修改和通知；等待按钮和读档不会提前创建或重复创建。

核心入口：Ew4.Core/ScenarioRules.cs、ScenarioFactions.cs；兵种/显示接入：ClientContent.Start、ClientGame.View；绘制：app.js 的 refreshHud 和 battlefield.js 的 Renderer.flag。专项包括 ScenarioFactionChecks，运行 `dotnet run --project rewrite/Ew4.Verify -- <仓库根目录> --scenario-only`。
