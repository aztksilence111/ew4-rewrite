# 通用禁行地形示例

API 3 作者覆盖包，欧洲与东亚各配置一格禁行山地、一格禁行海洋，验证能力不依赖地图名。附东亚检查剧本 `conquest7`；不是历史战役或正式远海划区。

本包整文件替换内置地理配置，东亚仅保留示例的一格雪山。正式喜马拉雅121格请从 `rewrite/data/rewrite/geography.json` 或 `geography-template eastasia` 获取，合并 `maps` 数组及扩展清单。

```powershell
dotnet run --project rewrite/Ew4.ModTool -- pack rewrite/mod-kit/examples/impassable-geography --base=rewrite/data --id=impassable-geography --name=通用禁行地形示例
dotnet run --project rewrite/Ew4.ModTool -- verify rewrite/mod-kit/examples/impassable-geography --base=rewrite/data
```

默认素材由引擎提供，无须复制图集。字段及存档边界见[接口文档](../../docs/porting/GEOGRAPHY.md)。
