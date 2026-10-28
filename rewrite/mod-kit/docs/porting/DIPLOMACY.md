# 征服外交、核心地区与首都国库

仅支持包含本次实现的新引擎。外交窗口、宣战、随机劝降及首都缴获只在征服启用，战役不能通过此配置开启。AI不能主动随机劝降，但可以军事攻陷首都，也可通过作者事件模拟投降。

覆盖包根路径为`rewrite/diplomacy.json`，不加`data/`。可以只提供修改的字段。下面的组合与格号仅作格式示例，须按实际剧本修改。

```json
{
  "schema": 1,
  "rules": {
    "lossWeight": 75, "occupationWeight": 25, "maximumChance": 95,
    "coreWeight": 25, "capitalLootPercent": 20, "coreLootBonus": 20
  },
  "scenarioLocks": {},
  "scenarioPairs": {
    "conquest1": [{"actor":"fra", "target":"rus", "surrenderBonus":15, "lootBonus":10}]
  },
  "scenarioCapitals": {"conquest1":{"fra":1234}},
  "presentation": {"title":"外交", "width":440, "rowHeight":96, "buttonSize":30}
}
```

`scenarioLocks`、`scenarioPairs`、`scenarioCapitals`的剧本键只能是现有征服ID或`*`。配置中的势力选择器支持运行势力ID、国家ID或`@player`；同一国家有多个运行势力时展开匹配。组合有方向，A对B不自动影响B对A。组合修正为百分点，分别允许-100..100；逆方向请另写一条。未知组合选择器不匹配，未知首都选择器拒绝开局；事件新建势力应通过创建／后续事件指定首都。

配置组合按`rules.pairModifiers`、`scenarioPairs["*"]`、当前剧本的顺序覆盖同一有向组合。规则和配置组合随新局冻结，更新文件不会重写旧局；事件修正按对应字段覆盖该局配置，未修改的另一字段保留。设`value:0`表示把该字段的组合修正设为零。

## 核心地区与劝降

界面中的“本土建筑”统一改称“核心地区”。默认在开局剧情及初始归属完成后，记录每个势力持有的城市、工厂、马厩、港口；每座计1，不按等级加权。农田、要塞、空地不计。普通占领、改名、换旗、改色、切换玩家和迁都都不重算核心，核心可以重叠且独立绑定势力ID。

- N：目标核心建筑总数，默认来自开局；事件修改核心后以新集合为准。
- R：目标仍持有的核心数。
- K：发起国目前占领的目标核心数。
- S：发起国与目标的核心集合交集数，**无需发起国实际占领**这些重叠核心。

```text
劝降概率 = clamp(0, maximumChance,
  roundAway((lossWeight*(N-R) + occupationWeight*K + coreWeight*S)/N + surrenderBonus))
默认 = clamp(0, 95, roundAway((75*(N-R) + 25*K + 25*S)/N + 组合修正))
```

`roundAway`表示中点远离零取整。六个`rules`数值均允许0..100。默认核心完全重叠额外增加25个百分点；例如N=20、R=10、K=5、S=10、组合+10，概率为66%。系数是可调整的初值，未宣称完成整局平衡验证。

双方敌对、目标为存续AI势力、N>0、K至少1且玩家本回合可操作时才可劝降。核心声明和组合加成不绕过实际占领要求。失败后须军事攻占此前从未攻占过、当前仍属于目标核心的建筑，并在重试时持有；重复夺城、事件赠地、增删核心、农田或等回合不解锁。每次消耗全部新增占领资格；历史和独立随机种子存档，重载不重新抽取。

```json
[
  {"type":"core_add", "faction":"@player", "cells":[1234,1250]},
  {"type":"core_remove", "faction":"@player", "cells":[1250]},
  {"type":"faction_core", "faction":"civil.rebels", "cells":[1234,1260]},
  {"type":"surrender_modifier", "faction":"@player", "otherFaction":"civil.rebels", "value":-15},
  {"type":"loot_modifier", "faction":"@player", "otherFaction":"civil.rebels", "value":10}
]
```

`core_add`并入、`core_remove`移除；`faction_core`完整替换，`[]`清空。旧接口`faction_homeland`继续作为完整替换别名，存档字段保留兼容。允许指定未被自己控制的建筑，不转移领土，也不修改其他势力的核心、名称、旗帜、颜色或资源。事件使用运行势力ID、`@player`、`@active`，不能直接用配置中的国家选择器。格号必须是当前地图有效建筑；重复、越界、农田和空地拒绝。

事件可用`{"type":"faction_surrender","faction":"loser.id","otherFaction":"winner.id"}`模拟AI或剧情投降。双方须已创建且存续；这直接执行投降，不抽概率、不要求玩家回合，也不产生军事占领资格或首都缴获。它是作者显式结果，能够影响玩家，须由事件条件约束。实际投降移交目标现有领土、撤销目标部队并退出回合，不移交资源余额，不删除第三方部队。

## 首都与一次性国库缴获

开局首都依次采用事件显式指定、配置指定、原剧本首都标记；都没有则取所持最高等级城市，仍无城市则取其他有效建筑，等级相同取较小格号。完全无有效建筑的势力显示“未设首都”，可在获得建筑后由事件设置。事件`faction_capital`的`cells`必须恰好一个有效建筑，可以迁都。

```json
{"type":"faction_capital", "faction":"civil.rebels", "cells":[1234]}
```

军事行动将首都从该势力手中攻陷时，按攻陷前比例转移其当时金币、工业、粮食余额，玩家和AI使用相同规则：

```text
缴获比例 = clamp(0, 100, roundAway(capitalLootPercent + coreLootBonus*S/N + lootBonus))
各项缴获量 = floor(max(0, 对方该项余额) * 缴获比例 / 100)
```

目标N=0时核心加成为0。默认基础20%，完全重叠核心额外20个百分点；组合还能提高或降低，最终绝不超过100%。三项资源等额从败方扣除、转给进攻方，不凭空生成。负债不转移，不扣成更大的负债；整数向下取整。

每个势力ID终身仅触发一次，空国库或0%也消耗机会。夺回再丢、复国、改名、变旗、换色和迁都都不重置。记录随存档保存；触发缴获的移动不能撤回。判定发生在军事归属变化时、自动灭亡和事件结果之前，因此最后一城被攻陷也能正常缴获。事件赠地和劝降接收首都不触发或消耗此机会；第三国占领的首都被另一国夺走不从原势力国库扣钱。

变身生成**新势力ID**时有独立机会和初始国库，不复制模板余额或旧缴获记录：

```json
[
  {"type":"faction_create", "faction":"civil.republic", "name":"新共和国", "camp":"republic",
   "templateFaction":"@player", "initialResources":{"money":500,"industry":200,"food":300}, "capital":1234},
  {"type":"faction_core", "faction":"civil.republic", "cells":[1234,1250]}
]
```

`initialResources`为三个非负整数，省略时三项为0；`capital`为单个建筑格号。这两个字段仅用于`faction_create`，转交驻军／地盘、玩家控制仍需显式事件，见[势力接口](SCENARIO_FACTIONS.md)。开局创建的势力若未显式给核心／首都，按开局结束时领土初始化；局中创建后须显式给核心和首都，不继承模板核心。

## 界面与兼容

盟友与中立可直接双边宣战，不牵连第三方。窗口仅显示本国／盟友资源，敌对／中立无余额；首都名称、缴获比例和已触发标记不泄露实际国库。列表显示核心剩余／总数、我方占领和资格；劝降确认额外显示重叠核心及组合修正。

| 表现字段 | 默认或范围 |
| --- | --- |
| `width` / `rowHeight` | 440 / 96，范围320..640 / 74..140；窄屏约束 |
| `fontSize` / `titleFontSize` / `font` | 14 / 18 / `font_text_2_cn_hd`；尺寸12..22 / 14..28 |
| `gap` / `entryGap` | 8 / 8；范围2..20 / 4..32 |
| `buttonSize` / `entrySize` | 30 / 27；范围24..64 / 20..64，触控区最低44 |
| `textColor` / `headerColor` / `headerTextColor` | `#41413e` / `#414442` / `#ffffff` |
| `rowColor` / `borderColor` | `#d5d5cf` / `#999b94`；六位HEX色 |
| `titleImage` / `closeImage` / `patternImage` | 原版`form_title.png` / `button_close.png` / `common_pattern.png` |
| `entryIcon` / `warIcon` / `demandIcon` | `client/ui/diplomacy/`下`handshake.png` / `swords.png` / `scroll-text.png` |
| `entryBackground` / `warBackground` / `demandBackground` | 灰工具底图 / 原版红按钮 / 原版绿按钮 |
| `backgroundImage` | 默认原版标记以灰底和1像素灰边呈现；自定义PNG才完整缩放 |

PNG须在`client/`内，最大4MiB；禁止越界和非PNG。文字包括原宣战／劝降／结果／原因，以及`coreText`、`capitalText`、`capturedText`、`noCapitalText`、`lootedText`、`lostTreasuryText`；可使用对应的`{name}`、`{remaining}`、`{initial}`、`{count}`、`{chance}`、`{bonus}`、`{pair}`、`{percent}`、`{used}`、`{money}`、`{industry}`、`{food}`。`relations`固定四项，`reasons`可部分覆盖。

旧征服档缺少开局数据时只可宣战，不伪造劝降基线；旧档缺少首都时不根据当前领土补造历史首都／缴获记录，可由后续事件指定。已有旧版外交规则快照缺少新系数时使用25／20／20默认值。更新配置及UI不会改变账户／存档目录，规则随局保存，表现随当前Mod读取。事件逻辑的修改仍参与规则指纹，不能给旧规则存档直接换一份事件文件。
