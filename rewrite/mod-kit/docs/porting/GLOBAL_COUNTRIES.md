# 全局国家表与 BTL 国家 ID 扩展

国家地块颜色可用本表 `color` 设置；新增势力与阵营地图各自的配色入口见[地块颜色指南](MAP_COLORS.md)。

全局定义位于 `client/countries.json`，独立于所有剧本。源码位置为 `rewrite/data/client/countries.json`。国家 ID 是唯一、非空的字符串，可以用中文、数字、空格、标点或长名称，不限三个字母，也没有 15 字节或 128 字符上限；不接受控制字符，整个 JSON 有 2 MiB 预算。ID 按原字符串精确匹配、区分大小写，不会自动截断或规范化；配置字段名使用下述固定大小写。ID 是稳定引用，显示国名用 `name` 修改，发布后改 ID 需要同步所有引用并新开局。

## 国家定义

```json
{
  "schema": 1,
  "countries": {
    "mod.french-republic.1792": {
      "name": "法兰西共和国",
      "flag": "fra",
      "color": "#237A4D",
      "unitVariant": "fra",
      "visualVariant": "hre"
    }
  },
  "aliases": {}
}
```

| 字段 | 用途 |
| --- | --- |
| `countries` 的键 | 全局国家 ID，不是旗帜文件名、兵种名或行动顺序 |
| `name` | 显示国名，必填，最多 128 字符 |
| `flag` | 已有国旗资源键，例如 `fra`；HUD 图片及建筑旗帜四帧必须存在 |
| `flagImage` | 与 `flag` 二选一，自定义包内 PNG，例如 `rewrite/images/republic.png` |
| `color` | 可选领土颜色，`#RRGGBB` 或 `#RRGGBBAA` |
| `unitVariant` | `legacy/unit-variants.json.variants` 中已有的战斗兵种配置；更改会影响开局部队及招募 |
| `visualVariant` | `client/unit-visuals.json.variants` 的兵模外观键，独立于战斗属性 |
| `aliases` | 可选的旧 ID 到规范 ID 映射；目标必须直接存在，禁止链式或覆盖现有国家键 |

国家定义省略的外观/兵种项沿用剧本或模板默认值。旗帜、战斗变体与兵模变体是资源引用，仍需满足各自资源格式；国家 ID 可以自由命名，不需要将它们也改成同一个名称。战斗数值在 `legacy/units.json.units`，面板显示在 `client/manifest.json.profiles`，修改数值时同步显示配置。

内置表含 43 个原版身份。覆盖包中的 `countries.json` 替换整张表，需要保留正在使用的国家定义；不是增量合并。推荐复制内置表再增改。重复键、无效资源和不存在的战斗配置会在加载或打包时失败。

内置及原资源转换器生成的表可能含 `sourceSha256`，绑定 `def_army.xml` 和 `stringtable_cn.ini`。源哈希不符时忽略这张自动生成表，防止把原版默认身份强加给旧 Mod。作者自行维护的表可以移除 `sourceSha256`；保留时应在重导入后更新。不要在旧 XML 重新转换后期望保留手工修改过的转换产物。

## 完整 ID 写入 BTL：CNTR v1

原版 BTL 主体的 country 字段固定 16 字节，只能存 15 字节 UTF-8 文本和结束符。本次不扩大原记录，不移动地块/兵力/设施段，而是在同一个 `.btl` 文件尾部增加 **CNTR v1 必需块**。完整 ID 实际保存在文件里，不依赖外部别名；国家名称及默认资源仍从全局国家表读取。

在工具包根运行，源文件保持原剧本名：

```powershell
.\Ew4.ModTool.exe country-export "D:\mods\source\conquest1.btl" "D:\mods\work\countries-btl.json"
# 编辑导出的 factions 数组，并在自己的全局国家表中添加相应定义。
.\Ew4.ModTool.exe country-apply "D:\mods\source\conquest1.btl" "D:\mods\work\countries-btl.json" "D:\mods\release\original\conquest1.btl" --content="D:\mods\work\converted"
.\Ew4.ModTool.exe country-strip "D:\mods\release\original\conquest1.btl" "D:\mods\stripped\conquest1.btl"
```

源码运行可使用 `dotnet run --project rewrite/Ew4.ModTool --`，并指定 `--base=rewrite/data`。`--content` 指向包含 `client/countries.json`、`original/`、`legacy/` 等目录的作者资源覆盖目录；缺少的文件使用基础资源。命令不覆盖源文件或已存在的输出，apply 校验后才写入；单人剧本还会执行真实开局验证。

导出的编辑文档如下，`factions` 必须保留所有势力，以下仅为两势力格式示意：

```json
{
  "schema": 1,
  "baseSha256": "工具生成的原BTL主体SHA256",
  "factions": ["mod.french-republic.1792", "英国/联合王国"],
  "eventCountries": {"0": "mod.french-republic.1792"}
}
```

`factions[i]` 对应原 BTL 第 i 条势力记录，必须完整、非空且唯一。这里只修改 ID，不重排数组；原有阵营、资源、领土归属索引与兵力记录保留。`eventCountries` 可省略，键是 **原事件记录下标，从 0 开始**，不是事件 ID；值须为本剧本的某个完整国家 ID。默认把旧事件的短 country 引用同步映射到对应完整 ID；旧记录有重名而无法确定目标时，要求显式填写该事件行。原版找不到国家时的事件回退行为保留。

已有 CNTR 时 export 导出现有完整表；apply 替换该块；strip 只移除 CNTR，保留 JURI、VICT 和未知可选块。所有命令保留原主体字节。主体修改后，旧 `baseSha256` 会使加载失败，应重新导出、核对并写入。旧原版 APK、旧 rewrite 和旧编辑器不支持此必需块；不要依赖它们保存尾部。原版资源未自动批量重写。

二进制容器沿用 [JURI 协议](BTL_JURISDICTIONS.md)：原 BTL v2 主体后接 `EW4XBTL\0` 容器，块 tag 为 `CNTR`，版本为 1，flags 为 1，payload 为上述 UTF-8 JSON，最多 2 MiB。源哈希排除整个扩展容器。未知版本、重复块/键、截断、主体哈希不符、缺项/重复国家和越界事件行均拒绝。

## 外部别名与事件复用

不想改旧 BTL 时，也可把旧短 ID 保留在主体，并在国家表填写 `"aliases":{"fra":"mod.french-republic.1792"}`；此时必须从 `countries` 移除冲突的 `fra` 键。别名仅作兼容引用，不改变原记录或运行势力实例 ID。CNTR 自身无需别名，但 `def_battlelist.xml` / `def_battleline.xml` 的菜单或战役分支若仍引用旧 ID，应保留相应别名或同步修改引用；国家 ID 含逗号时，可用别名接入这些旧式逗号分隔列表。

局中创建势力可直接引用全局国家，原有事件接口继续支持显式覆盖：

```json
{
  "type": "faction_create",
  "faction": "republic.instance.1",
  "country": "mod.french-republic.1792",
  "camp": "republic.camp",
  "templateFaction": "@player"
}
```

`country` 指全局国家；`faction` 是本局唯一的势力实例 ID，属于已有事件接口，仍使用字母、数字、点、横线、下划线且最多 128 字符，`@player` 等值保留为特殊引用。国家 ID 不受这个实例命名格式限制。同一个国家可创建多个不同实例。独立创建且不设模板时，还需提供或从国家表取得旗帜和领土色。事件字段优先于国家默认值，未指定的兵种/外观再沿用模板。

## 顺序、存档与验证

全局国家字典不是参战名单。新增定义不会自动参战，也不提供初始领土。开局参与者及其顺序来自 BTL 的势力记录；玩家先行动，其余保持记录顺序；局中创建势力追加末尾。普通 AI 与跳过批次都沿用本局保存的势力顺序，国家表重排和加入未用国家不改变排序。

CNTR 修改或国家规则配置变更应重新 pack/import 并新开局，Mod 逻辑指纹会隔离新旧版本存档。普通无 CNTR 的原版 BTL 与原 ID 继续支持，新增内置国家表不改原版开局指纹。API：`/api/country-registry` 返回 `schema/countries/aliases`；`/api/catalog` 含国家定义和别名；`/api/countries` 仍只返回所选剧本的可选国家。

参考[最小示例](../../examples/global-countries/README.md)及[兵模接口](UNIT_VISUALS.md)。
