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

### 兵牌与国家身份

`client/manifest.json.profiles`可指定`mapMarker`（地图兵牌名），`mapMarkerAtlas`可指定独立图集，省略为`buildings`。图集可带`_hd`版本，sprite的`pixelRatio`决定逻辑大小；省略标志时使用原`mark_unit_{unitIndex}.png`。地图标志应不带圆框，生命环仍由原绘制器负责，远景缓存按图集和实际标志区分。`cardMarker`仍指向招募UI图集中的带框兵牌名；不改`unitIndex`或兵种属性。

单个profile可附加`presentations`，按`factions`完整势力ID、`countries`国家ID、`unitVariants`战斗变体依次匹配。值只覆盖`name`、`description`、`mapMarker`、`mapMarkerAtlas`、`cardMarker`，不能改变兵种数值或动作。示例：`"presentations":{"countries":{"my.country":{"name":"惩戒营","mapMarker":"my_penal.png","cardMarker":"my_penal_badge.png"}}}`。阵营对应关系由Mod配置，不把国家或图案写死在宿主；模型`visualVariant`不决定身份。地图、远景、招募与部队详情使用同一解析结果。

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

动作可配置布尔 `loop:true`，按库fps循环播放，适用于待机、移动和施工；缺省沿用原单次行为。移动优先使用 `move`，自定义 `die` 随退场进度播放。建筑施工通知可播放工兵一次 `build`，与建筑自身回合倒计时独立。`x/y` 在0.5的画面缩放前应用，160×112帧可用x=-40对齐中心。

动作可配置布尔 `static:true` 固定第一帧并结束其播放状态，优先于 `loop`；适用于需要完全静止的航空待机。选中及取消选中仍使用该静态动作，不自动切到攻击帧。航空只提供 `ready/move/attack` 绑定即可保留路径移动与攻击，省略其他非必要动作；不要用扭曲机头或裁缩包含机翼的区域模拟螺旋桨。

选中准备动作也可配置 `rest:"ready"`：不设置 `loop` 时仅播放一次，结束后保持 `ready` 第一帧；省略该字段时沿用原版准备后转攻击姿态的规则。可另外定义 `select`（点击时一次整备）、`selected`（整备后固定姿态）和 `undoready`（取消时一次收势），`select`配置`rest:"selected"`，`selected`配置`static:true`。普通未选中`ready`仍可静止，`rest`也可引用其他已配置动作。

《颓垣饿鸠》[0.1.27完整示例](../../../../颓垣饿鸠/art/units/animation-v7/README.md)覆盖73角色、39装备、186军服及直升机地面部件，共299部分。21部分复用大鏖战原生准备帧，其余按原模型裁片制作局部整备：手臂带动武器、炮管调整、舰炮就位、螺旋桨或旋翼起转。选中播放一次后保持准备末帧，取消反序收势；普通待机、攻击和移动图块保持原样。制作时固定脚底、底盘和船壳，不能用整个模型上下拉伸代替关节动作；选择帧不能烘焙火光或触发发射效果。复合部件按最长准备时长结束，避免静止机体提前终止步兵动作。

复合部队各部件共享起始时间；较短部件先停在自己的 `rest`，全部可见部件完成后才结束共享准备状态，避免静止机体提前终止步兵举枪。原资源声明多帧不代表有动作，制作时须核对实际唯一画面；无匹配动作时保留固定待命姿态，不用全图上下缩放替代整备。

空层移动提示保留航空交互点，同时以引线连接到目标地格中心的菱形落点；两端点选解析到同一个逻辑地格。地面部队沿用原移动点。

Mod 的 `groundShadow` 可引用建筑图集中的透明阴影图，绘制在地表格中心并随移动路径推进，空军机体仍位于右上方。可选 `groundShadowDirectional:true` 使右向投影随部队朝向水平翻转；省略时保留固定投影。空层默认没有地面移动扬尘；复合部队的surface部件在陆地仍可触发地面尾迹，渡海隐藏。喷气式视觉定义或profile可显式配置`airTrail:{effect:"my_jet.xml",x:-34,y:-8,scale:0.3,rotation:0}`，effect须已存在于effects定义中；x按标准右向机尾定位并随朝向翻转，y相对空中位置，尾气在air层绘制。转向重建发射器，旧粒子自然消散。螺旋桨机不应配置喷气尾气。

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
