# 事件切换玩家势力、胜负条件与结局

沿用 [scenarioEvents](SCENARIO_EVENTS.md)，可由条件直接执行，也可放在弹窗所选按钮的 `effects` 中。电脑与 Android 共用后端，旧 0.2.6 发行包不包含本增补。

本地多人在开局锁定名单，`player_control` 不替换名单；`@player` 随当前人类席位变化，`@active` 为当前行动国。战役按实际外交同盟共享胜负、合计目标，征服维持单国胜负。事件 `victory`/胜利 `ending` 的 `faction` 指定代表胜者，战役包括其盟友；单人失败结局不终止多人整局。自动胜利等待弹窗完成，动态 `win` 对各存活国家计算，战役共享盟友目标；忽略单人 `lose` 和时间失败条件。外交不因多人选择改变。

## 切换所扮演的势力

```json
{"type":"player_control","faction":"civil.rebels"}
```

始终是同一个玩家改为控制另一个势力，不增加玩家。目标须已经创建、尚未灭亡，且对局未结束；可以是本国分离势力、敌国内叛乱势力或其他已有势力，不要求与原国同阵营。原国转为 AI，目标成为唯一人类控制势力；会话权限、HUD、敌友视角、后续 `@player`、名册派遣及存档同步切换。

不跳转当前回合，不重置部队行动/移动、收入或资源。若当前仍是原国回合，AI 完成剩余行动，按正常顺序轮到目标国时玩家操作；目标正处于行动回合则由玩家接着操作。不会自动转交领土、军队、将领或外交，个人账户保留。名册派遣历史保留，不可重复派遣已使用的实例；目标没有派遣记录时沿用切换前国家的名额上限，已有记录则继续使用其原名额及历史。

`{"type":"player_faction","faction":"civil.rebels"}` 判断玩家当前是否扮演该国；`active_faction` 仍判断谁正在行动。普通结果中的 `@player` 在每一步执行时解析为当前控制国，切换后再写 `money` 会给新控制国。`faction_create.templateFaction:"@player"` 是开局玩家模板，装载剧本时绑定原国，之后切换不改变模板来源。重新开始回到最初选国及初始事件状态。

## 替换胜负条件

多阶段顺序控制、结算界面替换与可打包示例见[结算扩展指南](VICTORY_MODDING.md)。

```json
{
  "type":"victory_rules",
  "victoryRules":{
    "win":{"type":"cells_owned","cells":[1234]},
    "lose":{"type":"round_reached","round":30},
    "priority":"defeat",
    "requirePlayerSurvival":true,
    "descriptionKey":"夺取首都"
  }
}
```

`victoryRules` 使用 [自定义胜负规则](RULE_EXTENSIONS.md) 的同一结构和条件树，支持占领/失去目标、消灭指定国家/部队、回合、资源及 Lua。完整替换本局当前规则，不叠加原版红点或原回合限制；省略 `roundLimit` 即没有该项限制，`requirePlayerSurvival` 默认 true。描述字段为元数据，当前没有独立自定义任务面板。满足新规则时按正常判定点结束本局，弹窗待选期间暂停胜负判定。

`{"type":"victory_default"}` 撤销事件覆盖，恢复剧本原本安装的规则，沿用 BTL VICT / Mod victoryRules / 原版规则的既有优先顺序。原始规则不被改写。可多次替换或恢复，结果随存档保存，移动、战斗、AI、回合与宿主共用同一判定。

占领、资源及敌方判断自动使用当前控制国的视角。`factions` 数组使用实际势力 ID，可引用同一事件文件声明的待创建势力；尚未创建不算被消灭。`cells` 必须有效，`units` 在开局须引用已有单位。嵌套 Lua 脚本自动加载校验，不必重复列入 scripts。切换后若原版红点不适合新角色，应在同一事务中替换规则。

## 指定结局

```json
{"type":"ending","key":"rebellion.independence","outcome":"victory"}
```

立即以当前控制国视角结束本局并保存分支标识。`outcome` 必须为 `victory` / `defeat`，`key` 为非空、无控制字符、最多128字的ID。客户端状态 `ending` 返回ID，结果面板沿用现有胜负结算；ID不自动生成剧情文案或额外奖励。可先用事件弹窗展示结局叙述，再在确认按钮内提交 `ending`。原有 `victory` / `defeat` 仍可直接结束。

切换、胜负规则、结局和同数组其他结果原子提交，扣款不足兜底会撤回整组修改。已结束对局不能借此重开，结算沿用原有防重复机制。

## 叛乱选择示例

`1234` 和 `army.rebel` 须换为实际地块和驻军ID：

```json
{
  "id":"rebellion.choice",
  "condition":{"type":"cells_owned","faction":"@player","cells":[1234]},
  "effects":[
    {"type":"faction_create","faction":"civil.rebels","name":"地方共和国","camp":"rebels","unitVariant":"rus","flag":"rus","color":"#237A4D"},
    {"type":"unit_owner","faction":"civil.rebels","unit":"army.rebel"},
    {"type":"cells_owner","faction":"civil.rebels","cells":[1234]}
  ],
  "popup":{
    "title":"地方独立","text":"是否转为扮演新成立的国家？",
    "choices":[
      {"id":"join","text":"扮演新国家","effects":[
        {"type":"player_control","faction":"civil.rebels"},
        {"type":"victory_rules","victoryRules":{"win":{"type":"round_reached","round":20},"requirePlayerSurvival":true}},
        {"type":"money","faction":"@player","value":1000}
      ]},
      {"id":"stay","text":"继续扮演原国家","effects":[]}
    ]
  }
}
```

情况一：条件按本国领土/阶段设置，驻军和地块来自原玩家国。两个按钮都会成立新国，只有 join 转换控制权。若拒绝后应完全不产生叛乱，把公共创建/转交结果移入 join 按钮。

情况二：触发条件改为进攻方攻占某格、阶段回合或 Lua 战争查询，驻军/地块改用被进攻国家的实际目标。仍用同一个 `player_control`，不要求叛军先成为进攻方友军。

可封装的第一征服实例见[角色与胜负示例](../../examples/scenario-control/README.md)。
