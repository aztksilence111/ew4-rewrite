# 用户本地 DEF 与 R 版兼容性核验

2026-10-26。只读检查 Downloads 中用户指定的九份 `def_*(3).xml`；未改原文件、游戏实现、安装目录、账户或存档。

## 结论

1. 截图 `Imported source differs: def_army.xml` 是来源字节 SHA-256 不匹配，不是兵种字段非法。使用内置规则目录并直接替换这份 XML，调用真实 `VerifySources`，复现 `legacy.source_changed` 和完全相同的提示。重新使用真实导入转换器生成规则后，来源哈希及运行时目录检查通过。
2. `def_army(3).xml` 的 369 条兵种、33 个国家通过转换、属性和继承解析；全部国家的招募配置能生成。未发现兵种内容阻断项。
3. 确定内容阻断项为 `def_terraintype(3).xml:3`：`type="sea" movementcost="0"`。`rewrite/Ew4.Legacy/LegacyTerrain.cs:21` 要求全部地形 `movementcost >= 1`，真实读取器及客户端均报 `legacy.terrain_xml: Invalid terrain movement costs`。
4. 仅在核验进程内把海洋消耗改为 `1` 并重新转换规则，客户端目录加载通过，仓库内置 95 个场景解析及开局全部通过，错误 0。这是诊断对照，不代表已修改或交付玩家 Mod。

玩家实际安装的 `legacy/catalog.json`、独立规则文件、BTL、图像和完整 Mod 未提供。因此不能断言其安装目录仍是内置哈希，也不能将仓库场景开局结果当作玩家完整 Mod、既有存档或实际移动过程的验收。

## 逐文件结果

九份 XML 均可直接解析，无需兼容性规范化；顶层有 ID 的记录未发现重复 ID。

| 文件 | 记录 | 结果 |
| --- | --- | --- |
| def_army(3).xml | 33 国家 / 369 兵种 | 转换、运行时属性、继承和招募配置通过 |
| def_commander(3).xml | 588 将领 | 军衔/爵位索引、技能、初始装备及属性引用通过；rank 0..14、nobilityrank 0..9 在当前配套表内 |
| def_item(3).xml | 239 物品 | 运行时物品及交易目录通过 |
| def_card(3).xml | 47 卡 | 卡定义及全部国家招募配置通过 |
| def_construction(3).xml | 5 建筑种类 | 建筑等级、招募、收益、恢复/防御及升级上限读取通过 |
| def_capital(3).xml | 12 首都 | 与建筑目录共同读取通过 |
| def_warzonetech(3).xml | 6 战区 | 每战区 26 科技槽的专用读取器通过 |
| def_princess(3).xml | 8 公主 | XML 与 commanderid 引用通过；`reignyears="200"` 当前客户端不用于解锁判定，不是载入错误 |
| def_terraintype(3).xml | 7 地形 | 第 3 行海洋消耗 0 非法；其余在诊断对照中通过 |

## def_army 哈希证据

| 对象 | SHA-256 |
| --- | --- |
| 仓库内置 catalog 记录 | `2ba0fe999054c461d3812e2dbdbc10e866c71ad2145fbe06c0839dec48489da4` |
| 用户文件实际值 | `9246ffbd01a0e16d090d45e6e4dbc4543f447b37cc7e80b0fef09d6d92d49215` |
| 经转换器重新生成的 catalog 记录 | `9246ffbd01a0e16d090d45e6e4dbc4543f447b37cc7e80b0fef09d6d92d49215` |

触发代码：`rewrite/Ew4.Legacy/LegacyRuntimeCatalog.cs:38-42`；启动调用：`rewrite/Ew4.Client/ClientContent.cs:50`；导入重建：`rewrite/Ew4.Mods/LegacyModImporter.cs:68-72`。转换器对实际读入字节计算来源哈希，而非对属性值做等价比较。

处理需先把海洋消耗改为合法正整数，再以无下载后缀的标准资源名重新导入原格式资源，生成配套规则并打包/验证。不要只改 `source_sha256`，否则转换数据可能仍是旧值。下载文件名中的 `(3)` 不是内容错误，但不能作为运行时标准资源名。

## 复核与清理

可复用只读工具：`tools/DefCompatibilityAudit/`，直接调用项目真实规范化器、转换器、目录及场景加载逻辑；编译通过。运行方式：

```powershell
dotnet run --project tools/DefCompatibilityAudit/DefCompatibilityAudit.csproj --artifacts-path build/def-compatibility-audit-20261026/artifacts -- 'E:/Workplace/game/o4mod' 'C:/Users/WIN11/Downloads'
```

结束后核对 Git、绝对路径、重解析点及活动进程：独立输出目录无活动进程、无重解析点。已清 `build/def-compatibility-audit-20261026`，169 文件 / 15,652,177 字节，错误 0。只保留核验源码和本结论；未创建测试安装包、玩家档案或运行服务。
