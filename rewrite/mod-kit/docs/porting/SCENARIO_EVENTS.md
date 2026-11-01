# 可编程剧本事件（API 1 增补，2026-10-21）

电脑与 Android 共用实现。两种流程：`condition → effects`，或 `condition → popup → 所选按钮 effects`。原 BTL 事件保留；本接口在其上追加，不改写原 BTL 段。规则存于 Mod 包的 `rewrite/` 中。

## 注册与最小格式

`rewrite/extension.json`：

```json
{"schema":1,"apiVersion":1,"scenarioEvents":"rewrite/events.json"}
```

`rewrite/events.json` 按剧本 ID 分组，每组按数组顺序检查。默认一个事件在一局中触发一次；添加 `repeat` 可按整轮回合周期触发。多个阶段可用 `event_completed`、`event_count` 或 `state` 串联。

```json
{
  "conquest1": [
    {
      "id": "supply",
      "condition": {"type":"round","value":3},
      "effects": [{"type":"food","value":100}]
    },
    {
      "id": "council",
      "condition": {
        "type":"all",
        "conditions":[
          {"type":"city_count","compare":"ge","value":5},
          {"type":"money","compare":"ge","value":200}
        ]
      },
      "popup": {
        "title":"军议",
        "text":"请决定下一步行动。",
        "choices":[
          {"id":"provisions","text":"拨款征粮","effects":[
            {"type":"money","value":-200}, {"type":"food","value":500}
          ]},
          {"id":"industry","text":"扩充军工","effects":[
            {"type":"money","value":-200}, {"type":"industry","value":60}
          ]},
          {"id":"wait","text":"暂缓决定","effects":[]}
        ]
      }
    }
  ]
}
```

`title`、`text` 和按钮 `text` 可直接写文字，也可写已在文字表中定义的 key。按钮数由 `choices` 数组决定，至少一个；界面自动换行并滚动。省略 `choices` 时提供一个 `OK` 按钮。每个按钮可有独立 `condition`，不满足时禁用。弹窗触发时必须至少有一个可选按钮。

### 图片与长文本呈现

弹窗主体显示图片、文字栏或图文组合，下方为独立的选项区。主体与选项均可上下滚动；阅读长文本不会滚动选项，浏览选项也不会带走正文。横屏选项为双列，竖屏为单列。图片保持比例完整显示。

`popup.image` 为可选 PNG 路径，例如 `rewrite/images/council.png`，图片放入 Mod 包同名位置，单图不超过 4 MiB。`imageAlt` 为可选图片描述，也支持文字表 key；省略时界面用标题作为描述。只支持包内 `rewrite/` 图片，不接受网络 URL 或目录越界。导入时检查文件存在与 PNG 内容。

```json
"popup": {
  "title": "出征军议",
  "image": "rewrite/images/council.png",
  "imageAlt": "军团集结图",
  "text": "第一段军情。\n\n第二段军情，可以继续写长文本。",
  "choices": [{"id":"continue","text":"继续出征","effects":[]}]
}
```

仅文字时省略 `image`；仅图片时省略 `text` 或设为空字符串；两项同时填写即为图文组合。旧文字事件配置无需修改。图片路径与描述随等待选择的事件一起保存。

有 `popup` 时，事件自身的 `effects` 作为公共结果，在选择后先执行，再执行所选按钮的 `effects`。打开弹窗时不扣资源、不提前执行结果。选择不可撤回；缺省没有关闭或取消按钮，需要取消选项时把它写成一个按钮。

## 条件字段与完整类型

通用字段：`type`、`faction`（默认 `@player`）、`otherFaction`、`relation`、`camp`、`unit`、`commander`、`cells`、`key`、`profile`、`category`、`compare`、`value`、`conditions`、`script`、`function`。未用字段通常省略。

`faction` 接受当前剧本的国家运行 ID、事件中声明的待创建势力 ID、`@player`（玩家当前控制国）或 `@active`（当前行动国家）。国家 ID 不是阵营 ID，也不一定是 `fra` 等选国代码。用开局/状态 API 的 `factions[].id`。`camp` 是阵营字符串；不要把国家和阵营混用。

数值条件默认 `compare:"ge", value:1`，比较支持 `eq/ne/gt/ge/lt/le`（等于、不等于、大于、大于等于、小于、小于等于）。资源可比较不足、等于、区间，不局限于“至少”。区间用 `all` 组合上下界。

| type | 判定与参数 |
| --- | --- |
| `always` | 恒真，用于开局事件 |
| `all` / `any` / `not` | `conditions` 全满足/任一满足/取反；not 只含一个子条件 |
| `money` / `industry` / `food` | 指定国家当前金币/工业零件/粮食 |
| `round` | 当前整轮回合数，从 1 开始 |
| `city_count` | 当前拥有的城市数；含城市建筑等级所表达的首都，不含农田/港口/工业/马厩 |
| `territory_count` | 当前拥有的有效地块数 |
| `objective_count` | 当前拥有的地块黄圈数；单位黄圈用 Lua 查询 `unit.objective` |
| `unit_count` | 现存部队数，可再用 `profile` 兵种 ID 或 `category` 类别筛选 |
| `kill_count` | 国家累计击杀；省略 key 汇总所有类别，指定 key 只查该统计键 |
| `unit_health` | `unit` 当前血量；单位不存在时为 0 |
| `building_level` | `cells[0]` 的建筑等级 |
| `state` | `key` 对应的事件/扩展整数变量，未设置时为 0 |
| `cells_owned` | `cells` 所列全部地块归指定国家/阵营；指定城市用该城市地块 ID |
| `unit_at` | `unit` 位于 `cells` 中任一格 |
| `unit_alive` | 指定 unit 实例仍存在 |
| `unit_destroyed` | 指定 unit 实例经过死亡管线；撤军/解散/投降移除不冒充死亡 |
| `unit_removed` | 指定 unit 实例经 unit_remove 显式和平撤编 |
| `commander_alive` | 指定 commander 实例目前任职于现存部队 |
| `commander_dead` | 指定 commander 实例曾随部队死亡；卸任/投降退场不算死亡 |
| `commander_available` | 指定 commander 已入场、未退场且未在任何部队任职 |
| `commander_withdrawn` | 指定 commander 活着退场，不包含真正死亡 |
| `faction_defeated` | 指定国家已出局 |
| `faction_exists` | 指定国家已经创建；已灭亡仍算存在 |
| `faction_active` | 指定国家已经创建且未灭亡，不表示正在行动 |
| `faction_camp` | 指定国家的当前阵营等于 camp |
| `active_faction` | 指定国家正在行动 |
| `player_faction` | 玩家当前扮演指定国家，与该国是否正在行动无关 |
| `relation` | faction 与 otherFaction 的当前关系等于 relation：allied / neutral / hostile |
| `event_completed` | key 指定事件已执行完；弹窗事件必须已经完成选择 |
| `event_count` | key 指定事件累计触发次数；支持 compare/value；弹窗打开时计数 |
| `script` | 自定义 Lua 布尔/数值查询；非零/true 为满足，script 与 function 必填 |

`city_count/territory_count/objective_count/unit_count/cells_owned` 可用 `camp` 查询整个阵营；省略时只查 `faction`。其他条件按表说明。单位和将领使用 `units[].id/commanderId`，不直接使用 XML 的编号；内置初始部队例如 `conquest1.unit.N`，将领通常为 `conquest1.unit.N.commander`。地块为完整地图数组的零基索引，不能用裁剪后屏幕序号。加载会拒绝未知剧本、国家、未声明的单位/将领和无效地块引用；允许提前引用事件或剧情选项中声明的新增对象，见[事件军队接口](SCENARIO_ARMIES.md)。

## 结果字段与完整类型

通用字段：`type`、`faction`（默认 `@player`）、`otherFaction`、`relation`、`camp`、`unit`、`cells`、`key`、`value`（默认 0）、可选 `script/function`。一个结果数组按顺序执行。全部成功才提交；错误保留原状态。脚本结果也必须满足对应操作的取值范围。

| type | 行为 |
| --- | --- |
| `money` / `industry` / `food` | 指定国家资源增减 value，负数扣除；不允许结果透支 |
| `set_money` / `set_industry` / `set_food` | 设置指定国家对应资源总额 |
| `camp` | 把指定国家的阵营设置为 camp；该国部队自动按新阵营判定敌友 |
| `relation` | 只设置 faction 与 otherFaction 之间的关系；allied / neutral / hostile / default，详见下文 |
| `faction_create` | 创建新的 AI 势力，可独立配置兵种、名称、国土颜色和旗帜；继承原势力可选 |
| `faction_revive` | 恢复已灭亡势力的行动资格；不恢复已消失的部队和将领 |
| `faction_identity` | 修改指定势力名称、国土颜色及旗帜，可只改其中一项 |
| `cells_owner` | 把 cells 所列地块转交指定国家；存在敌对占军时应先明确转交部队 |
| `unit_owner` | unit 转交指定国家，其任职将领随部队转交；原名册派遣历史仍占原国名额 |
| `unit_spawn` | 独立生成有将或无将部队，可给已有国家增援或与创建国家同批执行 |
| `unit_remove` | 和平移除部队；有将军队须明确将领待命或活着退场，不冒充战毁 |
| `commander_assign` / `commander_detach` | 原部队任命／卸任；显式先卸后任可换将，保留战损比例 |
| `morale` | 指定国家部队士气设为 value（-3 至 1），保留领导力负士气豁免 |
| `environment` | cells 所列地块环境值设置为非负 value |
| `environment_spread` | cells 各格执行原环境扩散，随机状态随存档保存 |
| `heal` | unit 恢复 value 血量，不超过最大血量，value 非负 |
| `destroy_unit` | unit 经共享死亡/将领退场/国家存续/原死亡对白处理；已不存在时略过 |
| `state` / `add_state` | key 变量设置/增加 value；写入键必须以 `scenario/` 开头，以免覆盖技能冷却 |
| `victory` / `defeat` | 结束本局，当前玩家控制国胜利/失败 |
| `player_control` | 切换同一玩家所扮演的势力，原国交给AI；不跳转回合 |
| `victory_rules` / `victory_default` | 用 victoryRules 替换本局胜负条件，或恢复安装规则 |
| `ending` | key为结局分支ID，outcome为victory/defeat，结束本局并保存标识 |

角色切换及动态胜负见[角色与结局接口](SCENARIO_CONTROL.md)，含本国分离和转投敌国叛军示例。`player_faction` 条件判断当前控制国，普通结果中的 `@player` 随控制权变化。

数值结果可附加 `script:"rewrite/xxx.lua", function:"xxx"`，用 Lua 返回值代替 value；可根据局势计算奖励、惩罚、环境或变量值。返回有限且精确的整数，绝对值不超过 2^53-1。创建/身份、角色切换、胜负规则、结局和新增军队/任免结果不接受该数值脚本字段；胜负规则自身可嵌套 Lua 条件。所有权和阵营由声明字段决定。此接口没有任意托管代码执行、任意世界对象写入或新建建筑命令。

2026-10-31 新增军队结果的 `profile/commander/commanderDefinition/health/experience/cost/disposition`、原军转属来源保护 `expectedOwner`、落点阻塞重试及读档行为见[事件军队接口](SCENARIO_ARMIES.md)，附[最小示例](../../examples/scenario-armies/README.md)。需包含本能力的新构建，既有 0.2.28 发行版不支持。

势力操作的字符串参数直接声明；`faction_create/faction_identity` 不接受 script/function。具体字段、两种触发流程、独立兵种/PNG 旗帜及复国行为见[势力事件接口](SCENARIO_FACTIONS.md)。新势力创建前，针对它的资源/数量/所有权/关系等条件为假；可用 `not(faction_exists)` 判断尚未出现。给它执行其他结果前，必须先创建；接收部队前若它已灭亡，必须先复活。

## 周期事件与军团补给

事件顶层增加可选 `repeat`，直接结果和选择弹窗都支持：

| 字段 | 含义 |
| --- | --- |
| `everyRounds` | 周期，正整数，默认 1；按整轮计数，不按每个国家行动计数 |
| `startRound` | 第一个计划回合，正整数，默认 1 |
| `endRound` | 可选最后回合，包含该回合，不能早于 startRound |
| `maxOccurrences` | 可选最多触发次数，正整数；弹窗展示即占一次 |

例如每 5 回合收取一次出征费用，从第 6 回合开始，在 6、11、16……回合检查：

```json
{
  "id": "legion.upkeep",
  "repeat": {"everyRounds":5,"startRound":6},
  "condition": {"type":"state","key":"scenario/expedition","value":1},
  "effects": [
    {"type":"money","value":-100},
    {"type":"food","value":-200},
    {"type":"industry","value":-5}
  ],
  "insufficientResourcesEffects": [
    {"type":"add_state","key":"scenario/shortages","value":1},
    {"type":"state","key":"scenario/expedition","value":0}
  ]
}
```

在出征/返营事件或选项中设置 `scenario/expedition` 为 1/0；费用的 `faction` 可写实际国家 ID，所以既能扣玩家的军费，也能扣 AI 军团自己的资源。可在结果里增减或设置任一 AI 国家三种资源；资源类型和玩家相同。

固定计划公式为 `(round - startRound) % everyRounds == 0`。计划回合内的每次事件检查都可触发，但同一事件每整轮最多一次；该计划回合一直不满足条件就跳过，不补扣。暂停后恢复仍沿用原计划，不从恢复回合重新计时。`endRound` 和 `maxOccurrences` 同时存在时，先达到哪个就停止。

默认资源不足仍拒绝整个事务。配置 `insufficientResourcesEffects` 后，任一资源结果将透支时，撤回本次全部结果，再从原状态执行短缺结果；不会只扣金币却没扣粮食，也不会保留已执行的死亡或外交结果。弹窗的公共结果和所选结果同属一个事务；短缺结果成功后，本次选择仍算完成。短缺结果自身错误会正常报错，不继续递归兜底。它也可用于一次性事件；不处理其他配置或执行错误。

重复弹窗会在下次展示时清除上一次选择记录，允许重新选择。只保留最近一次选择及累计次数；`event_completed` 在新弹窗待选期间为假，选完为真。需区分具体次数或路线时，组合 `event_count`、`state` 和 `event_completed`。存档保留次数、最近触发回合、待选弹窗与最近选择，读档不会重收同轮费用。

## 独立双边外交

`camp` 仍表示阵营分组；`relation` 对指定两国建立优先级更高的双边关系，不改变国家的 camp，也不牵动第三国。关系双向对称，不沿盟友链传递。同阵营可敌对，不同阵营可结盟。

```json
{
  "type":"relation",
  "faction":"@player",
  "otherFaction":"conquest1.faction.2.koi",
  "relation":"hostile"
}
```

两项国家字段均支持实际国家 ID、`@player`、`@active`；不能对自己设置关系。上例中的 ID 来自内置第一征服，作者需换为目标剧本的实际国家 ID，且不能等于当前玩家。可把结果放在城市条件、消灭势力条件、回合条件或弹窗按钮里，实现多阶段外交和分支路线。

| relation | 行为 |
| --- | --- |
| `allied` | 友好/同盟，不能互相攻击，移动通行、治疗支援和胜利目标按盟友判断 |
| `neutral` | 中立，AI 不主动攻击对方；玩家可像原中立机制一样主动进攻，首次攻击后两国关系变为 hostile |
| `hostile` | 敌对，两国可交战，AI 和胜利判定将对方视为敌人 |
| `default` | 仅结果可用；移除该对关系覆盖，并清除两国之间旧的中立开战记录，重新使用当前阵营/中立规则 |

显式双边关系持续保留，后续修改 `camp` 不会覆盖它；要恢复阵营关系，先用 `default`。界面国旗的敌友颜色和攻击目标也以当前双边关系为准。自定义胜利的敌国/敌方目标及友方占领条件遵循关系；显式敌对优先于中立阵营排除。按 `camp` 编写的普通数量条件仍按字面阵营统计，查询双边关系使用 `relation` 条件或 Lua。

## Lua 查询

脚本同技能扩展离线运行，自动预载并检查函数，有 VM 指令预算。条件查询无副作用，不能通过 Lua 直接改世界；结果通过上述声明式操作提交。查询用的随机对象不推进战斗 RNG，建议条件不使用随机数。返回 `{value=...,state=...}` 的 state 不作为剧本变量写回；使用 `state/add_state` 结果显式写入。

```lua
function condition_a(ctx)
  local player = ctx:faction(ctx.playerFaction)
  local allied_cities = 0
  for cell = 0, ctx.width * ctx.height - 1 do
    local site = ctx:cell(cell)
    if site.active and site.kind == 'city' and site.camp == player.camp then
      allied_cities = allied_cities + 1
    end
  end
  return allied_cities >= 5 and player.money >= 200
end

function reward(ctx)
  return ctx.round * 10
end
```

| 上下文 | 可读内容 |
| --- | --- |
| ctx | event、round、value、state、width/height、activeFaction、playerFaction、playerCamp |
| ctx.unitIds/factionIds/commanderIds | Lua 从 1 开始的 ID 数组，可枚举当前对象 |
| ctx:cell(零基地块) | id、owner、camp、kind、level、objective、active、sea、name、environment、installation |
| ctx:unit(id) | 不存在返回 nil；id、owner、cell、camp、profile、category、health、maxHealth、atSea、has_tag、commander、morale、movement、acted、objective、transport、experience、burning |
| ctx:faction(id) | 不存在返回 nil；id、camp、defeated、human、money、industry、food |
| ctx:commander(id) | 不存在返回 nil；id、owner、withdrawn、rank、title |
| ctx:unit_destroyed(id) / ctx:commander_dead(id) | 死亡历史布尔查询，读档后保留 |
| ctx:relation(firstId, secondId) | 当前两国关系 allied/neutral/hostile；使用实际国家 ID，未知国家返回 nil |
| ctx:event_count(eventId) | 累计触发次数，未触发为 0；一次性事件触发后为 1 |

## 执行、保存与调试

开局、移动/占领、战斗、技能/招募/升级/资源交换等客户端提交，以及回合交接前后会检查条件。直接结果可引出后续事件，执行后从列表首项重查。一次性事件只执行一次，周期事件同轮最多一次，避免无限触发链。

弹窗期间复用对白阻塞机制，禁止正常战场动作及 AI 继续。原对白在先时先完成原对白再检查新事件；事件产生原死亡对白时也按队列等待。跨回合弹窗保存原结算阶段，选择后从该阶段续接，不重复收入/治疗。事件造成的变化不可用移动撤销回退。

WorldSave 4 增加可选事件定义、触发记录、选择记录、周期进度、双边关系及死亡历史，原无扩展存档缺省为空；扩展 JSON/Lua 内容参与现有规则指纹，不同逻辑版本存档仍隔离。已存在的旧手机/旧 Web 包不支持这些新增字段，需使用包含本增补的宿主。

主入口：`Ew4.Core/ScenarioRules.cs` 的 ValidateDefinitions/Validate/Match/Evaluate/Choose；`Ew4.Legacy/ExtensionLoader.cs` 的 Load；`Ew4.Client/ClientContent.cs` 的 Start；`ClientGame.cs` 的 Action/Commit；`ClientScenarioEvents.cs` 的 ScenarioPopupView；`wwwroot/app.js` 的 refreshHud/event_choice。校验命令：`dotnet run --project rewrite/Ew4.Verify -- <仓库根目录> --scenario-only`。

可直接打包的中文示例见 [scenario-events](../../examples/scenario-events/README.md)。

周期军费、AI 补给与分阶段双边外交示例见 [scenario-recurrence-diplomacy](../../examples/scenario-recurrence-diplomacy/README.md)。
