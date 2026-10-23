# 战役显示与隐藏

内置游戏的新账户直接显示全部战役，包括原来的 11 个隐藏关。导入的 Mod 默认继续遵守自己的 `def_battlelist.xml` 的 `hide`，不改变原包。此 API 2 覆盖包可恢复原版隐藏方式，也可逐关覆盖。

将本例 `rewrite/` 合并到自己的覆盖包；已有 `extension.json` 时只合并 `campaignVisibility` 引用字段。配置：

```json
{
  "schema": 1,
  "visibility": {
    "*": "original",
    "campaign5_05": "visible",
    "campaign1_05": "hidden"
  }
}
```

- `original`：读取原 XML 的 `hide`；`visible`：初始显示且可进入；`hidden`：按隐藏关解锁记录控制显示与后端入口。
- 指定入口优先于 `*`；不配置时，内置游戏全部显示，Mod 保持 XML 设置。
- 键是 `def_battlelist.xml` 中的战役入口 ID，不带 `.btl`；国家分支共用主入口，不能用 `campaign3_08b` 单独配置。拒绝未知 ID、征服 ID、非法模式、重复键和未知字段。
- 隐藏关沿用胜利并清空前置关 AI 黄圈目标的规则；俄土分支分别解锁各自隐藏关。将普通第一关设为隐藏前，应确保有可达前置或已存在解锁记录。
- 修改配置后重新打包、导入、启用，配置纳入 Mod 逻辑指纹及原有存档隔离。内置旧解锁记录保留。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\campaign-visibility" --id=campaign-visibility --name="战役隐藏设置" --version=1.0
```
