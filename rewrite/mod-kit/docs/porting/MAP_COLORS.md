# 国家、势力与阵营地块颜色

普通地图、辖区地图使用国家/势力颜色；阵营地图使用共享阵营颜色。颜色字段都接受 `#RRGGBB` 或 `#RRGGBBAA`，末两位为透明度，省略时完全不透明；`60` 为原阵营地图默认透明度，`00` 为完全透明。

## 国家默认颜色（已有接口）

源码入口：`rewrite/data/client/countries.json`。覆盖包入口：`client/countries.json`。

给对应国家添加或修改 `color`，例如 `countries.fra.color`：

```json
"color": "#237A4D80"
```

国家表作用于使用该国家 ID 的所有剧本，包括引用该国家的新势力。优先级为：势力显式颜色 > 国家表默认颜色 > 原 BTL 颜色（新势力可继承模板）。完整格式和 `sourceSha256` 处理见[国家表](GLOBAL_COUNTRIES.md)。覆盖包替换整张国家表，建议从当前完整表复制后修改。

## 新增势力和局中改色（已有接口）

`faction_create` 的 `color` 指定创建时的地块颜色；`faction_identity` 修改任意现有或事件新增势力的颜色，结果写入存档。两者都使用现有 `scenarioEvents` 注册方式。

```json
{
  "type": "faction_create",
  "faction": "civil.rebels",
  "name": "独立共和国",
  "camp": "civil.rebels",
  "flag": "rus",
  "unitVariant": "rus",
  "color": "#237A4D80"
}
```

```json
{
  "type": "faction_identity",
  "faction": "civil.rebels",
  "color": "#C43D4D80"
}
```

详见[势力事件接口](SCENARIO_FACTIONS.md)。同一国家的不同势力实例可分别改色。

## 阵营地图颜色（API 3）

源码可直接编辑 `rewrite/data/rewrite/map-colors.json`，内置已注册空配置，默认外观保持原样。覆盖包通过 `rewrite/extension.json` 注册：

```json
{
  "schema": 1,
  "apiVersion": 3,
  "mapColors": "rewrite/map-colors.json"
}
```

有其他扩展时，在原注册文件中追加 `mapColors` 并保留其他字段。配色文件示例：

```json
{
  "*": {
    "campPalette": ["#79AADD60", "#D67B7B60", "#DCB36A60"],
    "camps": {
      "ew4.camp.1": "#4080C080",
      "ew4.camp.2": "#C0505080",
      "civil.rebels": "#237A4D80"
    }
  },
  "conquest1": {
    "camps": {
      "ew4.camp.1": "#70904080"
    }
  }
}
```

| 字段 | 规则 |
| --- | --- |
| `*` | 全剧本默认配置 |
| 其他顶层键 | 实际剧本 ID，例如 `conquest1`；不存在的剧本拒绝加载 |
| `camps` | 阵营 ID → 颜色；剧本配置按键覆盖全局，未覆盖的阵营继续继承 |
| `campPalette` | 可选，1～64 个颜色；整个列表按剧本优先、全局其次、内置最后选取 |

原 BTL 阵营数字在 R 版中对应 `ew4.camp.1`、`ew4.camp.2` 等；事件创建的阵营使用其 `camp` 原值。可提前配置尚未创建的阵营。实际值可在 `/api/start` 或 `/api/action` 返回的 `factions[].camp` 核对，按完整 ID 区分大小写；国家 ID、势力 ID、敌友关系都不是阵营 ID。

未指定颜色的阵营按当前稳定顺序从色池循环取色；显式配置某阵营不会挤占其他阵营原来的色池位置。局中新增阵营、变更阵营、存读档均使用同一配置。要保证不同进入顺序、存读档或剧本中某阵营恒定同色，应在 `camps` 指定它的 ID。

此配置影响阵营地图的地块及海岸渐变，普通/辖区地图仍按国家色绘制。国界继续按显示模式分组；敌友徽标与外交、归属和胜负规则不受颜色影响。阵营颜色配置不会修改自动生成的兵牌字母。

修改源配置后需重启读取资源的预览/游戏，发布时重新打包；Windows Web 和 Android 共用内容加载链。配色参与内容指纹，启用新配色包时遵循现有 Mod 账户/存档隔离规则，不能把正在进行的对局当作热更新目标。

可打包目录见[阵营配色示例](../../examples/map-colors/README.md)。
