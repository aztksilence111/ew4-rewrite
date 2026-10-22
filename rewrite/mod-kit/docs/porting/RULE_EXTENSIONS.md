# 技能、特性与胜负规则扩展（API 1/2）

2026-10-22 增补：[局外配置与战役说明](METAGAME_AND_BRIEFING.md)，API 2 的 `metagame` 支持初始资源、商品位/库存及刷新恢复条件；`stageIntroPresentation` 支持任务窗口布局和目标文字，实际目标沿用胜负规则。

上将名额可通过 API 2 的 `deploymentLimits` 引用 JSON，支持全局默认与指定剧本覆盖；不配置保持当前限制，示例及存档规则见[上将名额模板](../../examples/deployment-limits/README.md)。

物品与装备现在可通过 API 2 的 `itemBindings` 复用本指南的技能、事件和 Lua，支持保留/替换原效果、装备主动/被动技能及消耗品使用效果，见[物品接口](ITEM_EFFECTS.md)及[模板](../../examples/item-effects/README.md)。

将领专属台词可用 `apiVersion:2` 的 `commanderDialogues` 引用 JSON，见[字段、条件规则及可打包模板](../../examples/commander-dialogues/README.md)。killRules支持目标将领、兵种、剧本、回合、血量条件、概率和固定/随机选句；未配置时保持原喊话行为。占领、退场数组仅预留。

原版技能的倍率、概率、移动/攻击加成、折扣、经验/经济及七种教学数值可通过 `apiVersion:2` 的 `legacySkillParameters` 引用参数 JSON，见[完整字段、范围和可打包模板](../../examples/legacy-skill-parameters/README.md)。这只调整原版机制的数值；同 ID 的完整扩展定义仍优先替换原机制。未配置时保留旧规则和存档指纹；旧引擎需要升级后才识别该字段。

2026-10-21 增补：[多阶段胜负与结算界面](VICTORY_MODDING.md)，复用 `victory_rules` 动态更新，新增 `victoryPresentation` 布局及数据/命令绑定。

2026-10-21 增补：[剧本条件、直接结果与多按钮弹窗](SCENARIO_EVENTS.md)，入口 `scenarioEvents`；包括资源、城市、死亡、回合、阵营、组合条件及 Lua。

本指南对应 `rewrite/extension.json` 的 `schema:1 / apiVersion:1或2`。API 1继续兼容；使用下文通用技能新字段时须声明API 2，旧发行版会拒绝该版本。电脑与 Android 共用 C# 加载器和 MoonSharp Lua 解释器；制作好离线包后两端都能导入，不需要服务器。原 APK 的 SO、DEX、DLL 不能作为此接口的脚本执行，需要将行为移植到下列事件和操作。

注意路径：下文 `rewrite/definitions/skills.json` 是 **Mod 包内部路径**。源码同名的顶层 `rewrite/` 则是项目目录，两者不是同一层。不要把扩展放进 `client/` 或工具的 `data/`。

## 1. 最小入口与目录

```text
作者覆盖包/
  rewrite/
    extension.json
    definitions/
      skills.json
      traits.json
      unit-bindings.json
      commander-bindings.json
      victory-rules.json
    scripts/abilities.lua
    presentation/layout.json
    presentation/icons/repair.png
```

扩展入口示例：

```json
{
  "schema": 1,
  "apiVersion": 1,
  "skills": ["rewrite/definitions/skills.json"],
  "traits": ["rewrite/definitions/traits.json"],
  "unitBindings": "rewrite/definitions/unit-bindings.json",
  "commanderBindings": "rewrite/definitions/commander-bindings.json",
  "victoryRules": "rewrite/definitions/victory-rules.json",
  "presentation": "rewrite/presentation/layout.json",
  "scripts": ["rewrite/scripts/abilities.lua"]
}
```

除 schema/apiVersion 外均可省略；未提供入口时完全沿用原规则。所有引用路径从包根开始，大小写精确匹配，必须以 `rewrite/` 开头；只支持 JSON、Lua、PNG，不接受 `..`、反斜线或绝对路径。JSON 单文件不超过 2 MiB，拒绝重复键、未知字段和过深结构；不要通过增加猜测的字段启用功能。

`skills` 和 `traits` 指向定义数组，使用同一结构与注册表。`scripts` 用于显式预载脚本，尤其是仅被 BTL 的 VICT 引用的脚本；技能和 JSON 胜利条件直接引用的脚本也会自动预载。脚本语法、模块初始化预算及处理函数存在性会在加载时检查。

## 2. 技能定义与兵种特性

```json
[
  {
    "id": "my.repair", "nameKey": "name_my_repair", "descriptionKey": "desc_my_repair",
    "icon": "rewrite/presentation/icons/repair.png",
    "kind": "active", "target": "self", "range": 0,
    "cooldown": 2, "endsAction": false,
    "cost": {"money": 3, "industry": 2, "food": 1},
    "effects": [{"event": "skill.activate", "operation": "heal", "value": 12}]
  },
  {
    "id": "my.anti_fort", "nameKey": "攻坚", "descriptionKey": "对指定特性目标提高伤害",
    "kind": "passive",
    "effects": [{"event": "combat.modify_damage", "operation": "damage_multiplier",
                 "value": 1.25, "targetTag": "my.armored"}]
  },
  {"id": "my.armored", "nameKey": "装甲", "descriptionKey": "自定义兵种标签", "kind": "tag"}
]
```

| 字段 | 语义与默认值 |
| --- | --- |
| id | 跨定义唯一、最长128字符，含点；字符为ASCII字母/数字/点/下划线/横线。推荐作者命名空间 |
| nameKey / descriptionKey | 当前文字表键；查不到时按原字符串显示，因而也可写直接文本。字体仍由rewrite统一绘制 |
| icon | 可选PNG；最大4 MiB。未指定显示信息图标。图标经包版本资源地址加载 |
| kind | passive 被动；active 主动；tag 仅用于分类和描述。tag 不自动执行effects |
| target / range | 主动技能 self/ally/enemy；默认self、距离1。ally包含己方及同阵营，enemy包含可被主动挑衅的中立；距离按六角格计算，不自动检查视线 |
| cooldown | 非负整数，按完整回合Round计时，默认0。同一实例一回合最多一次，即0也不能在当回合无限重复 |
| endsAction | 默认true；使用后消耗该单位行动和剩余移动。false仍受单位已行动、回合和冷却限制 |
| cost | money/industry/food，默认均0；必须非负。执行前后端检查资金并扣除 |
| effects | 有序效果数组，默认空；每个效果独立执行 |
| targetTag | 可选，对方profile的traits必须含此ID才执行该效果；不读取贴图或名称判断兵种 |

同一ID可引用多次，用instanceId区分等级/冷却/状态。兵种原特性可绑定：`no_retaliation`、`line_splash`、`siege_gun`、`coastal_gun`、`consumed_on_attack`、`ai.rocket`、`ai.direct_artillery`、`ai.counter_mounted`。自定义标签先以kind=tag注册；标签自身不会凭名称产生行为。

### 支持的事件与操作

| event | operation | 执行位置 |
| --- | --- | --- |
| combat.modify_damage | damage_multiplier / damage_add | 发起伤害的一方，被动修正每次伤害；反击同样经过此入口 |
| combat.counterattack | counterattack | 进攻方限制对方能否反击，0禁止，1保留原资格；不能强制已死或原本不合格的目标反击 |
| combat.attack_count | attack_count_add | 进攻方的伤害取样次数追加，非新增可点击攻击次数 |
| scenario.start | heal / money / industry / food | 新局初始化后，每个持有技能的初始单位各执行一次 |
| turn.start / turn.end | heal / money / industry / food | 该单位所属方开始/结束回合 |
| unit.move_end / unit.captured | heal / money / industry / food | 移动完成；目的格归属变化后的移动单位 |
| combat.after | heal / money / industry / food | 参战且仍存活的单位，战斗结算后 |
| unit.recruited | heal / money / industry / food | 客户端共享事务中新出现的单位，包括玩家及AI招募/事件生成 |
| unit.damaged / unit.destroyed | heal / money / industry / food | 客户端共享事务的伤害/死亡通知；死亡可读取死亡前来源，但heal不会复活 |
| skill.activate | heal / damage / money / industry / food | 后端验证通过的主动技能实例 |

效果value默认1、必须为有限非负数；heal/damage/资源操作取整。heal封顶最大生命，damage经过统一死亡处理，包括将领退场、事件及势力存活判定。资源奖励给技能来源的国家；heal/damage用于所选目标。战斗数值修改顺序为damage_multiplier后damage_add，不跟随effects中跨操作的排列；同类效果按绑定和数组顺序处理。最终值需在0～Int32.MaxValue之间，越界拒绝事务。

伤害与死亡始终以后端结算为准，界面只能发出请求。自定义伤害也不能让已被主动攻击杀死的受击方反击。没有声明的事件/操作会拒绝导入；API 2提供下文受校验的召唤等操作，仍不提供任意世界对象写入、改建造资格或替代AI规划的Lua接口。

## 3. 给单位与将领绑定，移除四技能限制

unit-bindings示例：

```json
[
  {"target": "ew4.unit.fra.heavy_artillery.1", "mode": "append",
   "traits": ["my.armored"],
   "skills": [{"id": "my.repair", "instanceId": "unit.repair", "level": 2, "order": 0}]}
]
```

commander-bindings示例：

```json
[
  {"target": "ew4.commander.1", "mode": "replace", "skills": [
    {"id": "my.anti_fort", "instanceId": "commander.siege", "level": 1, "order": 1, "x": 0, "y": 0},
    {"id": "my.repair", "instanceId": "commander.repair", "level": 3, "order": 0, "x": 0, "y": 28}
  ]}
]
```

目标ID从当前Mod转换得到的 `legacy/catalog.json` 的profiles/commanders读取，不能以显示名称替代。原格式将领编号、国家兵种编号不等于本接口完整ID。

- append追加，replace替换该目标技能列表，remove按instanceId移除。traits按ID操作；将领没有traits绑定字段的行为。
- 将领技能数量不固定；未配扩展时沿用原四格。level为正整数、order控制显示排序，缺省分别1和0；排序不改变后端效果执行顺序。
- instanceId必填，最长64，不含`/`；同一单位的兵种技能和已任命将领技能合并后不得重复。建议分别用`unit.`、`commander.`前缀。原将领槽ID为`legacy.0`起。
- x/y必须同时出现且为有限数；单位为原版568×320逻辑界面像素，由前端整体缩放。只在positioned模式使用。
- 新旧技能可以混用。原技能引用为`ew4.skill.0`～`ew4.skill.39`。要替代原效果，用同ID注册定义；对应原生技能判定及经验/继承加成停用，再执行此扩展定义。不是在原效果上偷偷再叠一次。
- 旧XML四技能字段仍用于兼容原包；超过原格式的数量和布局写在绑定JSON。源SO中的未知技能不会被自动解释，须自行定义并绑定。
- 移植原XML的技能40等新原生编号：先在作者副本把该skill字段设为-1，再注册如my.skill40的定义及Lua，按将领完整ID绑定；不能保留旧解析器不支持的编号并期待加载后才补救。

### 自定义显示位置

presentation引用的JSON：

```json
{
  "commander": {"mode": "grid", "columns": 2, "gap": 4, "iconSize": 19, "maxHeight": 74},
  "unit": {"mode": "list", "columns": 1, "gap": 4, "iconSize": 19, "maxHeight": 130}
}
```

键只支持commander/unit。mode=list/grid/positioned；columns 1～16，gap 0～32，iconSize 12～64，maxHeight 40～400。可选left/top/width/height覆盖容器位置与尺寸，范围分别0～560/0～300/40～560/20～300。设置height时也作为最大高度。位置相对于弹窗内容区；不要覆盖肖像、属性或确认按钮，需在手机横屏验证。list使用单列；grid按columns；positioned按每个技能的x/y并提供滚动范围。将领默认容器74px，高度变更可显式指定height。

兵种信息窗预留标签/技能列表及说明入口；主动技能显示于底部工具栏，图标可横向滚动，执行前显示目标和费用。AI会应用相同被动规则，**本版本AI不会主动选择自定义主动技能**。

## 4. 外置Lua后端逻辑

在effect上同时填写script和function，用脚本返回的数替代该效果的value：

```json
{"event": "skill.activate", "operation": "heal", "value": 12,
 "script": "rewrite/scripts/abilities.lua", "function": "repair"}
```

```lua
function repair(ctx)
    local uses = (ctx.state.uses or 0) + 1
    return { value = ctx.value * ctx.level, state = { uses = uses } }
end

function anti_fort(ctx)
    if ctx.target.category == "fort" then return 1.5 end
    return 1
end
```

| ctx字段/调用 | 可读内容 |
| --- | --- |
| event / level / round / value | 事件名、实例等级、回合、当前值。数值修正事件round=0，value为当前累计伤害/次数；普通事件value为本effect.value |
| source / target | health、maxHealth、category、profile、atSea；`ctx.target:has_tag("my.armored")`查profile标签。缺少来源时为空表 |
| sourceId / targetId | 普通事件和主动技能的单位ID；数值修正及胜利检查为nil |
| state | 此单位/此技能实例的已保存整数字典；首次为空 |
| `ctx:random(bound)` | 0～bound-1，bound为1～Int32.MaxValue整数，使用后端确定性随机序列 |
| `ctx:cell(index)` | 绝对格ID；返回id/owner/camp/kind/level/objective/active/sea快照；越界拒绝 |
| `ctx:unit(id)` | 单位不存在返回nil；否则为source类型字段加id/owner/cell/camp |
| `ctx:faction(id)` | 不存在返回nil；否则id/camp/defeated/human/money/industry/food |
| playerCamp | 仅胜利检查提供玩家阵营 |

cell/unit/faction仅在带world的普通事件及胜利检查可用，伤害取样/反击资格/次数修正中不可用。快照修改不会改写世界；要改变实际值需返回允许的effect操作结果。

每次执行创建新VM；Lua全局变量不跨调用保留。返回number，或`{value=number,state={uses=1}}`；胜利条件也允许boolean。普通事件返回的state合并保存到`WorldState.ExtensionState[单位ID/实例ID/键]`，随战局保存；数值修正和胜利查询为无状态计算，返回state不落盘。state最多128个整数键值，键长1～64且不含`/`，数值绝对值不超过2^53-1。`ready`为冷却保留键，请用自己的键名。

每脚本不超过256 KiB，模块初始化和处理函数合用200000条VM指令预算。无文件、网络、系统或CLR对象接口；禁用load/require、math.random/randomseed及部分原生批量字符串函数。Lua标准库并非完整开放，不应依赖string.rep/format/gsub或table.concat。指令预算不是进程级内存配额，避免构造巨型字符串/表。运行异常使当前动作不提交，原资金和原世界保留；修脚本并重新打包后再试。

## API 2 通用技能

### 入口与新增字段

```json
{
  "schema": 1, "apiVersion": 2,
  "skills": ["rewrite/definitions/skills.json"],
  "statuses": ["rewrite/definitions/statuses.json"],
  "unitBindings": "rewrite/definitions/unit-bindings.json",
  "commanderBindings": "rewrite/definitions/commander-bindings.json",
  "scripts": ["rewrite/scripts/abilities.lua"]
}
```

这些路径按实际文件选择，未使用的字段应省略。`statuses` 是状态定义数组，技能/特性/状态ID共用唯一命名空间。`target` 新增 `cell`，仅主动技能可用。前端从后端提供的 `cells` 中选择坐标；请求为 `{op:"skill",unit:"单位实例ID",offer:"技能实例ID",cell:绝对格ID,revision:当前版本}`，不同时传 `target`。原单位目标请求继续使用 `target`。

| effect字段 | 默认值与限制 |
| --- | --- |
| scope | `target`作用于选定单位或选定格上的单位；`source`作用于施法者；`area`以所选单位/格为中心选取范围单位。未单独选目标时中心为自身 |
| radius | `area`专用，0～32，包含中心及距离不超过该值的单位；施法距离由技能的range控制，范围可延伸到range之外 |
| relation | `any/ally/enemy`，默认any；相对技能来源单位判断，按当前双边外交。enemy包含未结盟的中立，伤害或施加状态会触发相应外交 |
| profile | summon专用，完整catalog profiles ID，加载时检查存在 |
| status | apply_status/remove_status专用，引用statuses中的ID |
| duration | apply_status专用，1～1000个受影响单位所属方回合结束；不按全图各国轮流行动次数递减 |

`scope/radius/relation`对治疗、伤害、状态、当前移动力有效。金钱/工业/粮食只能用默认scope和relation，每条效果给来源国家奖励一次；不能按范围单位重复发放。战斗数值和移动上限修正也使用默认scope和relation，按当前参与计算的单位工作。

### 新操作与触发点

| event | 新增operation | 说明 |
| --- | --- | --- |
| skill.activate | summon / teleport | 仅active且target=cell；默认scope/radius/relation |
| skill.activate | apply_status / remove_status / movement | 支持单目标、自身和范围 |
| 原有开局/回合/移动/占领/战后/招募/受伤/死亡事件 | damage / movement | 在原heal和资源操作之外开放；死亡来源不会被治疗复活 |
| combat.modify_incoming_damage | damage_multiplier / damage_add | 普通战斗伤害的承受方被动修正；此时ctx.source是防守方，ctx.target是伤害来源 |
| movement.modify | movement_add | 被动技能或状态修改移动上限，支持负数，最终下限0；不自动赠送当前移动力 |

效果按数组顺序执行，每条范围效果按单位ID的序数顺序固定目标；每个受影响单位分别运行Lua。每条效果开始时选取目标快照，执行时已不存在的单位跳过。一次主动施法只扣一次资源、写一次冷却、提交一次revision；任何计算/校验异常均不提交。施法者死亡后不再执行余下主动效果。

`damage`为直接扣血，不走普通攻击护甲、减伤、反击或攻击次数；`combat.modify_incoming_damage`只影响普通战斗伤害。直接致死共用将领退场、死亡事件、势力存活处理。伤害通知不会无限递归触发新的伤害通知。

召唤每条效果固定生成一支部队，`value`必须为1；Lua也必须返回1。模板取自完整兵种ID，包含基础兵种属性、兵种绑定技能和粮食消耗，不复制将领、装备或当前单位状态，不额外调用普通招募费用/建筑资格。落点必须有效、海陆合法、有容量且属于友方或无主；不支持召唤要塞。新单位本回合已行动且移动为0，之后按普通单位存活，不自动消失。召唤受势力单位上限约束。

传送移动施法者自身，绕过路径消耗；仍检查range、地形、容量、外交与海陆适配，不能传送要塞、不能落在敌方领土或原格。无主或友方目的地不被占领，沿途没有领土变化；到达后剩余移动为0，更新海上/设施/局部士气并触发unit.move_end。`endsAction:false`可保留攻击资格。技能动作会清理此前移动撤回记录，防止撤回绕过费用和状态。

### 持续状态

`statuses.json`示例：

```json
[
  {
    "id": "my.haste", "nameKey": "急行军", "descriptionKey": "移动力上限增加3。",
    "stacking": "refresh", "maxStacks": 1,
    "effects": [{"event":"movement.modify","operation":"movement_add","value":3}]
  },
  {
    "id": "my.poison", "nameKey": "中毒", "descriptionKey": "每层每回合受到4点伤害。",
    "stacking": "stack", "maxStacks": 3,
    "effects": [{"event":"turn.start","operation":"damage","value":4}]
  }
]
```

技能效果用 `{"event":"skill.activate","operation":"apply_status","status":"my.poison","duration":3,"value":1}` 施加一层。`value`为层数，取整后须在1～maxStacks；`maxStacks`默认为1、最大32。每单位最多64种状态。状态定义可有nameKey/descriptionKey/icon，图标路径和预算同技能。

| stacking | 同ID再次施加时 |
| --- | --- |
| refresh（默认） | 保留原层数，剩余时长取原值与新值的较大者 |
| stack | 层数相加并封顶maxStacks，剩余时长取较大者 |
| replace | 用新层数、时长替换，清除旧脚本计数 |

再次施加时更新来源和技能等级；移除状态会清除该状态脚本计数，到期同样清除。相同ID不按施法者分成多个独立状态。状态在受影响单位的turn.end效果执行后减1，到0移除；当前己方回合施加的duration=1状态在本回合结束就到期。

状态效果只允许战斗修正、反击资格、攻击取样次数、移动上限、turn.start和turn.end；不能在状态内施加新状态、召唤或传送。状态随战局保存，切换/更换将领不清除单位状态；不写入局外将领培养数据。部队信息列表中的状态名称后显示“层数/剩余回合”。

没有Lua时，固定加值/治疗/伤害按层数相乘，倍率按 `value^层数` 计算。有Lua时返回值直接使用，脚本自行按ctx.stacks计算，不再自动乘层数。状态脚本中的ctx.source是持有状态的单位，ctx.target是本次实际目标；伤害结算保留最近施加者的国家来源。

### 移动力与Lua

`movement`只增减当前剩余移动，封顶当前有效移动上限，下限0；不重置Acted，不解除混乱，也不恢复攻击次数。`movement.modify/movement_add`改变上限，参与回合刷新和寻路；施加减速或清除加速后会裁剪超出的剩余移动。要立即获得加速收益，先apply_status，再movement，见群体急行军示例。

API 2新增Lua上下文：

| 字段 | 语义 |
| --- | --- |
| ctx.targetCell | 普通事件/主动技能所选中心绝对格ID；范围效果各目标共享该中心。战斗和移动上限查询为nil |
| ctx.stacks / ctx.remainingTurns | 当前状态层数和剩余时长；普通技能分别为1和0 |
| ctx.source.statuses / ctx.target.statuses | 以状态ID为键的快照，每项有turns/stacks/level；修改快照不影响实际世界 |

普通事件及范围施法可用 `ctx:unit(id)`、`ctx:cell(id)`、unitIds等查询。战斗和movement.modify仍为无状态查询，没有世界快照，round=0，返回state不保存。移动上限查询使用独立固定随机种子，不推进战斗RNG，建议只写纯数值逻辑。

```lua
function blast(ctx)
    return { value = ctx.value * ctx.level,
             state = { hits = (ctx.state.hits or 0) + 1 } }
end

function poison_tick(ctx)
    return ctx.value * ctx.stacks
end
```

技能自身的计数按“施法单位/技能实例”共享，范围内逐目标累积；状态计数按“受影响单位/status.状态ID”分别保存。`status.`是实例ID保留前缀，作者技能实例不要使用。原Lua脚本大小、VM指令预算、禁用库和事务约束继续适用。脚本不能返回任意世界操作列表，也不加载外部DLL。

完整可打包文件见[API 2六技能示例](../../examples/common-skills/README.md)。新增通用操作只需引擎实现一次，作者随后可自行组合新技能；未列出的新操作仍需更新引擎。AI会结算被动及状态，尚不会主动选择这些自定义主动技能。

## 5. 征服、战役胜负条件

没有扩展时保留原规则：征服击败所有敌对战斗阵营国家（未被玩家挑衅的中立不计）；战役按敌方红点清除及己方关键目标保护等原判定。自定义配置可以覆盖这套胜负条件。

victory-rules.json为剧本ID到规则的对象，ID不带.btl：

```json
{
  "conquest1": {
    "descriptionKey": "坚守到第十回合",
    "win": {"type": "round_reached", "round": 10},
    "lose": {"type": "cells_lost", "cells": [1920]},
    "priority": "defeat", "roundLimit": 20, "requirePlayerSurvival": true
  }
}
```

自定义规则中的roundLimit默认不设置，不继承原剧本的回合上限；若设置，则round > roundLimit失败。requirePlayerSurvival默认true，无存活玩家国家则失败，这两项在win/lose之前判断。priority决定自定义win/lose同时满足时胜利还是失败，默认defeat。lose可省略；不会自动额外叠加原红点失败条件，要保护目标应显式列入lose。descriptionKey为接口元数据；当前战场没有额外的自定义目标说明面板。

| type | 参数与含义 |
| --- | --- |
| all / any | conditions非空子条件数组；全部/任一满足。最多12层，合计最多1024条件 |
| enemy_factions_defeated | 活着的非玩家阵营战斗国家全部失败；includeProvokedNeutrals默认true，计入被玩家挑衅的中立，false排除中立阵营 |
| factions_defeated | factions中的国家ID全部被判失败，不以空格或仅失去一座城市代替失败 |
| units_destroyed / units_lost | units中的初始单位ID全部/任一不再存在 |
| cells_owned / cells_lost | cells绝对格ID全部属玩家阵营/任一不属玩家阵营；同阵营盟友算持有 |
| enemy_objectives_cleared | 非玩家战斗阵营不再持有红点单位/设施，不要求消灭无红点部队 |
| round_reached | round正整数，当前回合达到阈值 |
| resources | resources中money/industry/food非负阈值；玩家某个人类国家同时达到三项 |
| script | script路径/function函数名；返回非0或true表示满足 |

国家和单位ID从剧本启动后的API/存档查，不是国家简称；格子编号=`y * 底图宽度 + x`，以整个地图计算，不是BTL裁切区内编号。引用的单位须开局存在，格子须在有效区域内，国家须在该剧本中，导入/开局会检查。想检测后续事件生成单位，可写Lua快照查询。所有自定义条件在开局初始化后及后续动作/回合结算时检查。

Lua胜利示例（把格子编号改成自己的）：

```lua
function win(ctx)
    local city = ctx:cell(1920)
    return city.camp == ctx.playerCamp and ctx.round >= 10
end
```

胜利Lua为只读查询，返回的状态与随机状态不会写入世界；推荐用回合/归属/已保存实例状态判断，不用随机胜负。

### BTL内嵌VICT扩展

优先级：该BTL的VICT > 当前Mod的victory-rules[剧本ID] > 原规则。JURI辖区块与VICT可共存。旧原生引擎不认识这个尾部，保留不带扩展的原BTL作为原版兼容副本。

```powershell
.\Ew4.ModTool.exe victory-export "D:\mods\conquest1.btl" "D:\mods\victory.json"
.\Ew4.ModTool.exe victory-apply "D:\mods\conquest1.btl" "D:\mods\victory.json" "D:\mods\output\conquest1.btl"
.\Ew4.ModTool.exe victory-strip "D:\mods\output\conquest1.btl" "D:\mods\restored\conquest1.btl"
```

export产生规则本身，不是按剧本ID嵌套的字典；apply/strip保留其他扩展与原主体。输出必须不存在。自定义地图/规则依赖可用`--content=作者覆盖目录`提供；BTL单独引用Lua时同时在该目录的extension.json的scripts数组登记。工具会验证实际开局，不接受只存在于编辑器但没有可加载依赖的脚本。

二进制容器与[JURI指南](BTL_JURISDICTIONS.md)相同：主体后接8字节`EW4XBTL\0`、u32 LE容器版本1、u32块数；每块16字节头为ASCII四字母tag、u32版本、u32 flags、u32 payload长度。VICT的tag=`VICT`、版本1、flags=1（必需块）。payload为UTF-8 JSON：

```json
{"schema":1,"baseSha256":"原BTL主体字节的SHA256小写十六进制","rules":{"win":{"type":"round_reached","round":10}}}
```

主体哈希不含任何尾部块；主体改变后必须重新导出/写回。拒绝未知必需块、重复块、截断、重复JSON字段、非法规则和来源不符。只删VICT不会删JURI。建议使用CLI而非手工拼接二进制。

## 6. 修改生效、隔离与验证

可直接复制[完整示例目录](../../examples/rule-extension/README.md)做覆盖包。扩展JSON/Lua参与逻辑指纹；更改后重新pack/import/启用并开新局。旧逻辑版本账户/存档保留在原隔离目录，不能把新技能表直接强塞进旧局。图标PNG按表现资源处理；只换图不会主动迁移账户。界面坐标配置也参与逻辑指纹，发布前一起定稿。

原格式APK/ZIP可在根目录或统一assets下放rewrite/；已转换覆盖包则直接放根rewrite/。手机可导入包含这些文件的ZIP/.ew4mod，制作代码不要求手机安装编译环境。主菜单Mod管理页导入后明确启用，切回原版不会继续引用Mod脚本和图片。

最少验收：新局技能数量/排序/说明；不同等级；费用不足和错误目标拒绝；冷却、保存重载；玩家和AI被动战斗；主动致死不反击；关键目标/中立及同时胜负；退出Mod回原版。不要把本示例的通过结果当作所有原包Mod已兼容。

## 7. 文件与函数对照

以下路径相对源码仓库根；发行工具包不含源码。

| 工作 | 文件与函数 |
| --- | --- |
| 扩展路径导入/封包/逻辑隔离 | `rewrite/Ew4.Mods/LegacyModImporter.cs` Convert；`ModFiles.cs` RuntimePath；`ModStore.cs` Seal/Validate/Activate |
| 清单/JSON/绑定/原技能覆盖 | `rewrite/Ew4.Legacy/ExtensionLoader.cs` Load/ParseJson/PatchCatalog/ValidateVictory/ValidateVictoryScripts |
| Lua装载/预算/快照 | `rewrite/Ew4.Legacy/ExtensionScriptRuntime.cs` ValidateHandler/Run/Execute |
| 技能槽/事件/事务 | `rewrite/Ew4.Core/RuleExtensions.cs` SkillSlot.Validate、RuleEventBus.Slots/Bound/Modify/Dispatch、WorldSkillActions.Execute |
| API 2通用操作/状态/移动力 | `rewrite/Ew4.Core/SkillWorldEffects.cs` RegisterUnitTemplates/ValidCellTarget/ApplyEffect/MovementBudget/ExpireStatuses/ChangeNotices |
| 原生伤害接入/致死判断 | `rewrite/Ew4.Core/Combat.cs` Ew4Combat.Calculate；`WorldBattle.cs` Attack/Preview |
| 移动/回合/客户端事务事件 | `rewrite/Ew4.Core/WorldActions.cs` Move；`WorldTurns.cs` EndTurn/Resume；`rewrite/Ew4.Client/ClientGame.cs` Commit |
| 动态将领与原效果禁用 | `rewrite/Ew4.Core/Commanders.cs` Ew4Commander.Attach/Resolve；`AccountCommanders.cs` Inherit |
| 胜负评估/默认回退 | `rewrite/Ew4.Core/ExtensionVictory.cs` Evaluate；`WorldVictory.cs` Evaluate；`rewrite/Ew4.Client/ClientContent.cs` Start |
| VICT二进制读写/工具 | `rewrite/Ew4.Legacy/LegacyBattleExtensions.cs` Victory/WriteVictory；`LegacyResources.cs` LegacyBattle.WithVictory；`rewrite/Ew4.ModTool/VictoryCommands.cs` Run |
| 客户端技能视图/按钮状态 | `rewrite/Ew4.Client/ClientExtensions.cs` SkillView/SkillViews/ActiveSkills；`ClientHeadquarters.cs` |
| UI布局/说明/主动请求 | `rewrite/Ew4.Web/wwwroot/skill-ui.js` Ew4SkillUI.layout/icon；`menus.js` commanderInfo/skillTip；`app.js` openUnitInfo/openSkillAction |
| 回归与离线包验证 | `rewrite/Ew4.Verify/ExtensionChecks.cs` Run；`rewrite/Ew4.ModChecks/ExtensionModChecks.cs` Run；`tools/verify_extensions_web.cjs` |

源码验证命令：`dotnet run --project rewrite/Ew4.Verify -- <仓库根> --extension-only`；再运行`Ew4.ModChecks`同参数，均包含API 1/2专项。API 1浏览器脚本使用独立5185；API 2为`tools/verify_common_skills_web.cjs`，使用独立5206及独立存档，运行前确认端口属于本次预览进程，禁止对玩家服务执行自动化动作。Android用Compile验证共享接口，实机运行需另行验收。
