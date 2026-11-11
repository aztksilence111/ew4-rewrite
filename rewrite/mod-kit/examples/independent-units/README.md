# 独立兵种示例

需要含 2026-11-11 `unitRecruitment` 增补的宿主，旧 0.2.38 不支持。生成器从作者自己的完整基础数据复制文件，额外添加独立步兵、巡洋舰，不覆盖原兵种，不分发游戏贴图。两种兵种使用原民兵、护卫舰美术，满兵力300，仅法国 conquest1 可招募。

```powershell
node build.mjs "D:/R/data" "D:/mods/independent-units"
.\Ew4.ModTool.exe pack "D:/mods/independent-units" --id=independent-units --name="独立兵种示例" --version=1.0
.\Ew4.ModTool.exe verify "D:/mods/independent-units"
.\Ew4.ModTool.exe import "D:/mods/independent-units"
```

在源码树使用时，data 参数指向 `rewrite/data`。输出目录须不存在。生成独立覆盖包后再按标准工具导出 `.ew4mod`，启用并开法国 conquest1 新局，在本国空城市及海面港口分别查看新卡。原兵种和其他国家报价保留。

已有 Mod 应合并 `extension.json` 注册及其他配置，不能用本例最小清单替换自己的扩展。制作新兵模或开局增援见 [完整接口](../../docs/porting/INDEPENDENT_UNITS.md)。
