# 自定义将领能力栏（API3）

在 `rewrite/extension.json` 中指定 `"commanderAbilities":"rewrite/commander-abilities.json"`。能力栏独立于技能栏，最多新增32栏；本体不启用扩展。原八栏和新增栏都可排序及修改名称、图标，固定区域内滚动，支持桌面及手机。只调整原栏位时可不声明新能力。

```json
{
  "schema": 1,
  "order": ["infantry", "my.air", "cavalry", "artillery", "naval", "fort", "commerce", "movement", "training"],
  "presentation": {"infantry":{"nameKey":"步兵指挥","icon":"upgrade_tag_infantry.png"}},
  "abilities": [
    {"id":"my.air","nameKey":"航空能力","icon":"upgrade_tag_ship.png","defaultValue":0}
  ],
  "commanders": {"ew4.commander.1":{"my.air":8}},
  "units": {"ew4.unit.fra.light_infantry.0":"my.air"},
  "inheritance": [{"skill":"my.teach.air","ability":"my.air","gain":2}]
}
```

- `id`：带命名空间的稳定ID，仅ASCII字母、数字、点、下划线及连字符，最长128。不得覆盖原能力。显示名称不能代替ID。
- `order`：可选显示顺序，允许原能力和新能力混排。未列出的能力按原八栏和新能力声明顺序接在后面；不隐藏或删除能力。重复或未知ID在导入时拒绝。原ID为 `infantry/cavalry/artillery/naval/fort/commerce/movement/training`。
- `presentation`：按稳定ID覆盖原能力或新能力的 `nameKey`、`icon`；省略的字段保留原值。只影响名称和图片，不改变数值、培养规则或战斗意义。排序与改名不会使存档串值。
- `nameKey`：使用当前内容字符串表；无对应键时直接显示此文本。名称可长，界面单行适配。
- `icon`：原UI PNG文件名，或 `rewrite/icons/air.png` 等随Mod提供的PNG。扩展图片必须存在且不超过4 MiB。
- `defaultValue`：所有将领的新能力初值，默认0；`commanders`按将领定义ID覆盖部分新能力。值保留非负32位整数，超过五星显示星图及实际数量，不截断。
- `units`：按兵种完整profile ID指定读取哪一项新能力。该兵种任命将领后，用新能力替代原类别的攻击星级，沿用原攻击/反击星级公式；原类别、地形、兵模、海陆规则及其他能力不随之改变。不配置的兵种沿用原能力。
- `inheritance`：可选教学规则。`skill`必须已存在（可用自定义被动技能），`ability`必须在本文件声明；`gain`为非负整数，`maximum`可选且默认无上限。消耗携带教学技能的将领时，整编预览和实际结果均增加对应能力，不自动转移捐献者的全部能力值。原技能被替换后沿用原教学排除规则。

账户、战局、将领模板和Mod导入导出均保留新数值。原版二进制将领记录不能携带新字段；导入时从当前Mod定义补上新能力默认值，原七项二进制能力仍取原记录。配置改变会改变内容指纹及Mod逻辑版本，已有Mod存档不能跨指纹强行解释；无配置的原版账户及战局格式保持兼容。

本接口提供独立能力数据、展示、教学和兵种攻击星级绑定。射程、免粮等技能效果仍使用相应通用技能接口，能力值不会自动触发未声明的效果。

可运行的[最小示例](../../examples/commander-abilities/README.md)。
