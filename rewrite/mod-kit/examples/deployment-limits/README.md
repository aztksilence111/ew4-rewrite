# 上将名额接口

本目录是可打包的 API 2 覆盖包。模板的 `limits` 为空，保持各剧本当前上将数量限制；修改这个 JSON 后重新打包、导入、启用即可，无需修改游戏代码。旧版引擎需要升级后才能识别 `deploymentLimits`。

已有 `rewrite/extension.json` 时合并 `deploymentLimits` 字段，指向 `rewrite/definitions/deployment-limits.json`，不要覆盖其他扩展。配置示例：

```json
{
  "schema": 1,
  "limits": {
    "*": 12,
    "conquest1": 24,
    "campaign1_01": 0,
    "tutorials1": null
  }
}
```

- `*` 是全部剧本的默认覆盖；指定剧本 ID 优先。未命中任何配置时保留原剧本值。剧本键为无 `.btl` 后缀的 ID，必须存在于此 Mod 的剧本清单。
- 非负整数指定该国本局可派遣的账户将领总数；`0` 禁用账户将领派遣，`null` 表示不限名额。拒绝负数、小数、字符串、重复键及未知字段。
- 单人和本地多人共用；多人各人类国家分别计数，剧本切换控制国家时沿用原有名额和历史继承规则。上将界面显示和后端实际任命共用同一战局名额。
- 只影响账户将领派遣，不影响剧本自带将领、酒馆候选或局外指挥部的每星区20位容量。已派遣的历史不会因为阵亡、转移或退出任职而返还名额。
- 名额在新建战局时写入，随存档保存。改配置后需要重新打包导入；逻辑指纹变化会按既有规则隔离 Mod 存档，不会直接改写旧档或原版档案。

帽子图标可通过覆盖包中的 `client/ui/image_ui_hd/generalnumber.png` 替换，默认显示为26×19逻辑像素，保留透明背景即可。需要改图片名或显示尺寸时，修改 `client/manifest.json` 的 `layouts.form_deploygeneral.children` 中 `attributes.id` 为 `image_depnums` 的节点：`name` 指定已登记在 `manifest.atlases` 的图片，`w`、`h` 指定显示宽高；省略尺寸沿用26×19。由原始资源导入时，对应 `layout-568h.xml` 的同名节点。帽子和已用/上限整体居中，名册槽位与名额独立，使用指挥部容量和已存标签，两排横向滑动，不新增标签按钮。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\deployment-limits" --id=deployment-limits --name="上将名额" --version=1.0
.\Ew4.ModTool.exe verify "D:\mods\deployment-limits"
.\Ew4.ModTool.exe import "D:\mods\deployment-limits"
```
