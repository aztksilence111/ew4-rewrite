# 事件增删部队与将领任免

2026-10-31 源码新增，需使用含本接口的游戏构建；既有 0.2.28 发行包不包含本次能力。沿用 `rewrite/extension.json` 的 `scenarioEvents` 注册方式和 API 1 事件结构，Windows Web 与 Android 共用后端。最小源码见[示例](../../examples/scenario-armies/README.md)。

创建国家、转交原部队、任命将领和生成新部队是独立操作。`unit_spawn` 可直接给既有国家增援，也可在同一结果数组中接在 `faction_create` 后面；创建本身不会附送部队。目标国家必须存在且未灭亡，已灭亡国家先显式 `faction_revive`。

## 新建部队：unit_spawn

```json
{
  "type": "unit_spawn",
  "faction": "@player",
  "unit": "reinforcement.first",
  "profile": "ew4.unit.fra.line_infantry.0",
  "cells": [494, 495],
  "commander": "scenario.general.first",
  "commanderDefinition": 1,
  "experience": 0,
  "cost": {"money": 100, "industry": 0, "food": 50}
}
```

格号仅示范字段，实际配置须核对目标剧本；完整可运行配置以示例为准。

| 字段 | 规则 |
| --- | --- |
| `faction` | 接收国运行 ID，默认 `@player`，也接受 `@active`；允许给非当前行动国及 AI 国家增兵 |
| `unit` | 必填、唯一的部队实例 ID；1～128 位字母、数字、点、下划线、短横线。每支新军只声明一处 `unit_spawn` |
| `profile` | 必填，运行兵种表的完整键，如 `legacy/units.json` 导出的 `ew4.unit.fra.line_infantry.0`；决定属性和显示兵模，不随接收国自动换皮 |
| `cells` | 非空且无重复的候选格，依次选择第一个合法位置；使用完整地图的零基索引 |
| `commander` | 可选将领运行实例 ID；省略即无将部队 |
| `commanderDefinition` | 首次创建人物时填写原 XML 数字 ID／运行将领表数字键，不是 `ew4.commander.N` 字符串；属性、军衔、爵位、技能和头像均从该定义解析 |
| `health` | 可选正整数，表示任命将领后的实际初始血量，不能超过该军最终上限；省略为满血 |
| `experience` | 可选非负整数，部队初始经验，默认 0 |
| `cost` | 可选三资源非负对象；省略为免费，成功后扣除接收国资源 |

陆军只能生成于接收国陆地，海军只能生成于本国或无主海格；不允许在他国领土、敌军占格、满格、禁行地形生成，也不突破国家部队上限。炮台还检查建筑辖区的筑垒资格。生成不自动夺取领土；新国家需先明确取得落点。事件新增军默认本回合已行动、剩余移动为 0，之后进入正常回合与 AI 流程。

## 原军任免

```json
[
  {"type":"commander_detach", "faction":"local", "expectedOwner":"local", "unit":"local.army", "commander":"old.general", "disposition":"withdrawn"},
  {"type":"commander_assign", "faction":"local", "unit":"local.army", "commander":"new.general", "commanderDefinition":2}
]
```

`commander_assign` 保留原部队 ID、格子、经验和战损，仅给无将部队任命。已有指挥官时必须先显式卸任；不会默默顶替。首次创建人物需要 `commanderDefinition`；同一实例后续任命可省略，由先前声明解析，也能引用开局原有将领。将领必须归目标国、未退场且未在其他军任职。兵种能力和移动使用定义实际非负整数，保留超过五星的数值。

`commander_detach` 必须明确 `unit` 和预期 `commander`，防止卸错人。`disposition:"available"`（省略也如此）表示留在该国待命，后续可再任命；`"withdrawn"` 表示活着退场，本接口不自动恢复。任免导致血量上限变化时按比例换算伤兵，保留已经消耗的移动，不刷新行动次数。换将应在同一结果数组里完成，失败整体回滚。

`unit_owner` 沿用原部队及其任职将领转属，不生成新兵、不移动、不补血；新增可选 `expectedOwner` 校验交接前所属国家。该字段也用于 `unit_remove/commander_assign/commander_detach`，支持实际国 ID、`@player`、`@active`。任免和移除的 `faction` 必须等于目标部队当前所属国；若需改旗，先 `unit_owner`，再任命。

## 和平撤编与战毁

```json
{"type":"unit_remove", "faction":"@player", "unit":"reinforcement.first", "disposition":"available"}
```

`unit_remove` 移除现存部队，记入和平撤编历史。有将军队必须明确 `disposition`：`available` 留人待命，`withdrawn` 将领活着退场。无将军队可省略。目标不存在或归属变化时报错，可用 `unit_alive` 等条件保护；不会把缺失目标当作一支新军补回。

`destroy_unit` 仍走原有战毁、将领死亡、死亡触发器和国家存续管线。和平撤编不触发死亡、不记入战毁史；国家已无存续资产时仍会正常出局。撤编引起的国家存续检查在本段结果数组执行完后进行，因此同段“撤旧军＋生成替代军”不会中途灭国。`play_story` 是提交边界，进入剧情前结算此前撤编；替代军须在该边界前生成，或把整组操作放入剧情选项的结果中。

| 条件 | 含义 |
| --- | --- |
| `unit_alive` / `unit_at` / `unit_health` | 既有和事件新增部队均可引用 |
| `unit_removed` | 指定实例曾被 `unit_remove` 和平移除；投降、解散、战毁不冒充显式撤编 |
| `unit_destroyed` | 指定实例真正战毁，和平撤编不满足 |
| `commander_alive` | 当前附在现存部队上；待命不满足此旧条件 |
| `commander_available` | 人物已入场、未退场且未任职 |
| `commander_withdrawn` | 人物活着退场，已死亡不满足 |
| `commander_dead` | 人物经历真实死亡管线，卸任或撤军不满足 |

## 事务、后续引用与存档

候选格全堵或达到部队上限时，直接事件本次整体回滚且不消耗触发次数，后续事件检查可重试；弹窗选择保留原弹窗并返回 `scenario.army_blocked`。创建国家、土地、费用、将领及部队不会留下半份结果。剧情选项同样校验完整结果，阻塞时原选项状态保留；选项结果在该段剧情结束后提交。费用不足走已有 `insufficientResourcesEffects`，未配置则报错并保留原状态。

后续事件及胜负规则可以提前引用声明的新部队、将领 ID。未出现时生存／任职条件为假。开局部队 ID 与新军声明不能相撞。已生成部队的 ID 在战毁、撤编或投降后仍不可重用；同一人物定义也不能改用另一个实例 ID 重复创建。此人物检查覆盖已登记的剧本将领和酒馆候选；学院人物仍遵循独立的账户派遣规则，作者应避免两套人物池重复。

胜负规则的 `units_destroyed/units_lost` 沿用原有“已入场部队消失”语义，包含撤编和投降移除，但未生成的声明不会提前算作损失。需严格区分战毁时，用事件条件 `unit_destroyed` 驱动阶段目标。总部队上限在开局事件前加载，开局增援同样受配置容量限制。

周期无将增援可用 `unit:"wave.{occurrence}"`，按来源事件累计次数展开为 `wave.1`、`wave.2`。同一段结果中的 `unit`／`commander` 字段可使用该占位符，条件直接引用具体实例，例如 `wave.2`。占位符需来源事件的周期进度；有名将领不能靠占位符每期复制。不同声明也应避免使用能展开为相同 ID 的模式；执行时会拒绝重复实例。

部队、将领身份、生成史、撤编史、待命／退场状态、费用与事件进度随战局保存。动态人物可查详情并显示定义头像；重载不重复发军、不复活人物。旧存档缺省的新历史字段视为空，不改写原字节。`unitTemplate`／`commanderTemplate` 属引擎解析及存档数据，作者配置使用 `profile` 和 `commanderDefinition` 即可。
