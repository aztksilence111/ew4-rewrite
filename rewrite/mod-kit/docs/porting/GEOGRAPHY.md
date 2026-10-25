# 通用禁行地形（API 3）

支持本更新的 rewrite 可在任意注册地图上配置禁行地块，默认素材已经随引擎提供。

| type | 基底 | 默认外观 |
| --- | --- | --- |
| `impassable_mountain` | 陆地 | 六种原版派生的增高积雪山 |
| `impassable_sea` | 海洋 | 原版海面纹理派生的深色海面 |
| `mountain_pass` | 可通行山地 | 原版单峰山体，高度降低15%，最低通行代价等于山地成本（默认6） |

两类禁行地形的默认提示统一为“不可通行”。`mountain_pass` 使用原版山地贴图与原版地形信息显示，不增加“山口”或“山地”文字；仍保留山地防御和移动消耗，最低进入代价不能被步兵或 Geography 的普通地形优惠压低。两侧河谷不自动变成山口。

默认山口使用 `mountain_1.png` 的原版单峰岩面，显示高度为85%；紧邻山口的默认禁行雪山宽度为90%，向远离山口的方向偏移最多6地图像素，露出鞍部。仅改变绘制轮廓和遮挡，不改变格子边界、地形防御或通行规则；自定义精灵不套用上述原版精灵变换。

禁行格仍可显示、点选；部队不能进入、穿越或停留。技能、桥梁、运输、额外行动力不能解除禁行。招募、部署、召唤和传送落点也不能使用这些格，不能配置城市、设施、工事、部队或目标，不传播领土和辖区。炮击、飞行经过上空没有增加视线阻挡。

## 配置

包根 `rewrite/extension.json`：

```json
{"schema":1,"apiVersion":3,"geography":"rewrite/geography.json"}
```

已有其他扩展时保留原字段，将 `apiVersion` 升至3。API 1/2仍支持，但不能声明 `geography`。

`rewrite/geography.json`：

```json
{
  "schema": 1,
  "maps": [{
    "map": "europe",
    "width": 79,
    "height": 68,
    "baseTerrainSha256": "对应基础BIN文件的完整小写SHA256",
    "terrainOverrides": [
      {"column": 14, "row": 3, "type": "impassable_mountain"},
      {"column": 2, "row": 3, "type": "impassable_sea"}
    ]
  }]
}
```

坐标从0开始，格号为 `row * width + column`。`map` 为 `original/def_map.xml` 注册的 `name`。上例仅演示，正式配置须核对所有使用该地图的剧本。

- 地图名及同图坐标不得重复，宽高与 BIN 摘要必须匹配。
- 禁行山地只能放陆地，禁行海洋只能放水格；非法类型、坐标及实体冲突拒绝加载。
- 文件为整文件替换。恢复普通地形就是移除对应记录；修改内置东亚时要保留需要的121格记录。
- 未配置的格沿用普通规则。东亚使用相同入口，没有地图名特判。

可选顶层 `visuals` 用于替换名称及图集精灵，例如：

```json
"visuals": {"impassable_sea":{"name":"深海禁航区","sprites":["my_deep_sea.png"]}}
```

每类1至16个精灵，按格号稳定选择。精灵须在 `client/manifest.json` 的 `atlases.tiles` 与 `atlases.tiles_hd` 同时存在，提供对应图集像素及 `x/y/w/h/refx/refy`；这里不是独立 PNG 文件路径。新外观在底图上方绘制，跳过该格原有山地覆盖精灵。

## 作者工具

仓库中运行，输出目录须已存在。命令默认保留已有文件，需要覆盖时显式加 `--replace`：

```powershell
dotnet run --project rewrite/Ew4.ModTool -- geography-template eastasia my-mod/rewrite/geography.json --base=rewrite/data
dotnet run --project rewrite/Ew4.ModTool -- pack my-mod --base=rewrite/data --id=my-geography --name=地理修改
dotnet run --project rewrite/Ew4.ModTool -- verify my-mod --base=rewrite/data
```

模板命令填写注册名、宽高、摘要及该图当前禁行记录。编辑后 pack、verify，校验会实际启动包内登记的剧本并检查贴图。可用[双地图示例](../../examples/impassable-geography/README.md)验证导入、启用和导出。

## 存档

地理 JSON 参与规则指纹，改地块后应开新局。世界存档写版本5，读取4/5，缺省禁行属性为普通地形；现代地图含禁行属性时写版本2，普通地图仍写版本1。读取旧格式不代表忽略内容指纹或自动迁移旧战局。

Web、Android共享加载与校验代码。旧引擎须升级；旧 Mod 需按当前基础资源重新验证。历史发行 APK 不自动包含本次更新。
