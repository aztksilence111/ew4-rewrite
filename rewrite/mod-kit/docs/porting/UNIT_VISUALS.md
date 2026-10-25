# 国家兵模 JSON 接口

本接口用于选择已有或自定义兵模，不改变兵种战斗属性、国旗或回合顺序。Web 与 Android 共用；需要包含本接口的新宿主，旧 0.2.9 APK 不包含本次修正。

## 修改文件

内容包根目录 `client/unit-visuals.json` 是独立配置。源码中的基础文件位于 `rewrite/data/client/unit-visuals.json`。原包的 877 个动画定义保存在 `client/effects.json` 的 `motions` 中，本文件用名称引用它们，避免复制动画定义。

```json
{
  "schema": 1,
  "factions": {"my.republic": "my.skin"},
  "profiles": {},
  "variants": {
    "my.skin": {
      "militia.0": "Militia hre 1",
      "line_infantry.0": "Line Infantry fra 1"
    }
  },
  "defaults": {},
  "definitions": {}
}
```

- `factions`：势力运行 ID → 兵模变体。事件新增国家使用 `faction_create.faction`，如 `my.republic`；原有势力使用 API 返回的 `factions[].id`，如 `conquest1.faction.3.hre`。省略时先使用全局国家表的 `visualVariant`，再使用该国的 `unitVariant` 或原 BTL `Variant`；事件新增势力由引用国家或模板提供默认值，无模板则为 `others`。这里不读取国旗编号。
- `variants`：变体 → `兵种标识.编制` → 动画定义名。编制从 0 开始，0/1/2 对应一、二、三编。标识如 `militia`、`line_infantry`、`heavy_artillery`，可对照基础文件；同一国家可为不同兵种混用不同国家的原模型。
- `profiles`：完整战斗兵种 ID → 动画定义名，优先于国家变体。适合专门新增的兵种；例如 `"my.unit.militia": "Militia hre 1"`。共享战斗 ID 会影响所有使用该 ID 的国家，因此按国家换皮应写 `variants`。
- `defaults`：各 `兵种标识.编制` 的通用模型。没有国家专属映射时使用。基础文件已列出全部原版映射；制作完整覆盖包时，推荐从基础文件修改，以保留其余国家。
- `definitions`：可选自定义动作定义。可以新增定义名，也可以覆盖 `effects.json.motions` 中的同名定义；更换名称时同步修改引用。

选择顺序：`profiles` 精确覆盖 → `variants` 国家与兵种覆盖 → 当前动画库中的原版国家命名项 → `defaults` → 原版通用编制项 → 原版通用兵种项。没有势力变体字段的旧前端状态沿用旧 `effects.bindings`。显式配置引用错误会报错，不悄悄换成其他国家。

## 添加自定义兵模

在 `definitions` 下新增动作定义，例如：

```json
{
  "my.militia": {
    "res": "my_army",
    "x": -34,
    "y": -47,
    "scale": 1,
    "motions": [
      {"type":"ready", "dir":"all", "name":"my_ready", "speed":1},
      {"type":"move", "dir":"all", "name":"my_move", "speed":1},
      {"type":"attack", "dir":"all", "name":"my_attack", "speed":1}
    ]
  }
}
```

将对应绑定值改为 `my.militia`。同时提供 `client/animations/my_army.json` 和它引用的 `client/images/` 贴图；动画 JSON 使用现有 BILE 转换格式，不是仅有名称的空文件。`x/y` 是原版锚点坐标，`scale` 与既有绘制约定一致；`motions` 可配置 `ready/move/attack/reload/finish/build`，`dir` 为 `all/left/right`，`speed` 为正数，`effect` 可引用已有特效时轴。要塞施工使用 `build`，陆军下水仍使用运输船外观。

静止、移动、招募和攻击演出共用解析后的模型、图集与锚点。方向动作通过同一定义选择，不应只修改 `manifest.profiles[].motions.ready`。导入校验会检查定义、动作、动画文件、贴图、路径与数值；动画叶裁片的原包已知缺口仍应参考资源审计，不能用相似图猜补。

把覆盖文件放入作者目录的 `client/`，用 ModTool `pack` 打包为 `.ew4mod` 后导入启用。修改 JSON 后重新打包并启用更新版本，或在开发预览中重新载入页面。直接导入原格式 APK/XML 时，转换器会从该包自己的 `def_motion.xml` 生成这份映射，并转换专属模型使用的动画库。

## 新国家、国旗与战斗属性

可以为新势力独立组合三套数据。例如事件创建 `my.republic` 时 `unitVariant:"rus"` 决定后续招募的战斗属性，`flag:"fra"` 决定旗帜；上面的 `factions` 映射让它使用 `my.skin` 外观。现有部队转交后保留战斗属性，外观按新所属势力解析。

| 内容 | 可修改 JSON 与入口 |
| --- | --- |
| 国名、国旗、领土色及默认变体 | 全局 `client/countries.json`，见[国家表](GLOBAL_COUNTRIES.md)；局中 `faction_create` / `faction_identity` 可覆盖 `name/flag/flagImage/color`，支持自定义 PNG，见[势力接口](SCENARIO_FACTIONS.md) |
| 兵种基础数值 | `legacy/units.json.units` 的 `max_health/movement/minimum_attack/maximum_attack/minimum_range/maximum_range/food_consumption/damage_multipliers/traits`；`legacy/unit-variants.json.variants` 绑定国家兵种配置，由 `LegacyCatalogFiles` 合成后交给运行模块 |
| 属性面板文字 | `client/manifest.json.profiles` 的 `stats`；修改底层战斗目录时同步相应显示数据，不能只改面板就认为战斗数值改变 |
| 技能效果 | `rewrite/extension.json` 注册技能定义、绑定和原技能参数，见[规则 API](RULE_EXTENSIONS.md)与[原技能参数示例](../../examples/legacy-skill-parameters/README.md) |
| 兵模 | 本文的 `client/unit-visuals.json`，不改变上面任何规则 |

战斗目录 JSON 已能修改和新增已支持类别的兵种/变体，但它是完整的底层目录，并非简化的国家属性覆盖表。新战斗兵种还必须提供同 ID 的 `manifest.profiles`，招募/创建引用也要登记。原始 XML 导入会重新生成目录；不要先改转换产物再重新导入旧 XML。新规则、全新兵种行为仍须使用技能/Lua 扩展或新增实现，不能靠任意 JSON 字段自动获得。

## 国家名单与行动排序

目前开局参战势力仍在每个 `original/*.btl` 的势力记录中；`unit-visuals.json` 不是国家注册表，也不会创建势力。局中新增国家使用已有 JSON 事件接口。

实际顺序为玩家先行动，其余国家保持 BTL 记录顺序；事件创建的势力追加到当前序列末尾。跳过演出和普通 AI 回合使用相同顺序，已灭亡国家按现有规则略过。改变国旗、兵模或国家的属性定义不会改排序；重排 BTL 势力记录会改排序，并且必须同步地块归属索引等引用。

独立于剧本的全局国家表已实现为 `client/countries.json`，`visualVariant` 明确选模型，`unitVariant` 只选战斗配置；前端优先使用 `visualVariant`。国家 ID 可为长字符串或中文，完整 ID 可通过 BTL 的 CNTR v1 块保存，也可用 `aliases` 兼容旧短引用。参见[完整接口与工具](GLOBAL_COUNTRIES.md)。新增全局国家定义不等于参战，国家字典的排列不参与回合排序。
