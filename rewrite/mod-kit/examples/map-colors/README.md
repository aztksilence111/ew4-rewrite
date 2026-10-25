# 阵营地块配色示例

API 3 可打包覆盖目录，修改阵营地图的默认色池及指定阵营颜色，征服 1 单独覆盖阵营 1。`civil.rebels` 演示提前配置事件新增阵营，本示例不创建势力。

已有扩展时，将 `mapColors` 字段和配置合入自己的包，保留其他注册项。国家与新增势力的普通地块色继续用国家表 `color`、`faction_create.color` 或 `faction_identity.color`。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\map-colors" --id=map-colors --name="阵营配色" --version=1.0
```

字段与优先级见[地块颜色指南](../../docs/porting/MAP_COLORS.md)。
