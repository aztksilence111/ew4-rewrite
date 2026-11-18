# 将领头像图库与 Mod 皮肤

本功能需包含头像图库的 R 版宿主（API 3）。设定中的“头像图库”可按姓名/编号搜索，按阵营、招募星区、拥有情况及是否有上传/Mod皮肤筛选，并按阵营、姓名、星级或拥有优先排列。指挥部将领详情页的“皮肤”按钮直接打开该将领。

## 玩家上传

支持 **PNG、JPG/JPEG、WebP**，单张最多 **10 MiB**，最长边最多 **4096 像素**。建议至少 **512×512**；长方形图片可上传后缩放、调整横纵取景。请使用自己有权使用的合法图片。这些要求在上传按钮上方常驻显示。

裁剪界面同时预览方形立绘和圆形小头像，确认后生成384×384与128×128 PNG，指挥部、立绘和局内小头像同步。每个档案的上传图库最多64 MiB。可恢复原始头像、重新选用已上传图片或移除上传；每位将领保留一组上传。上传不会改写安装包，文件随既有存档导出/导入及档案备份携带；原版与各 Mod 的存档身份分别保存。

## 作者配置

在包根目录提供固定文件 **`rewrite/commander-skins.json`**。`rewrite/extension.json` 需为 API 3，不需增加指向皮肤的字段。最小可运行源码见[将领皮肤示例](../../examples/commander-skins/README.md)。

```json
{
  "schema": 1,
  "skins": [
    {
      "id": "demo.expedition",
      "commander": "ew4.commander.1",
      "name": "远征服",
      "image": "rewrite/skins/expedition.png",
      "marker": "rewrite/skins/expedition-marker.png",
      "unlock": ["demo.first-commander"],
      "unlockDescription": "获得任意一位将领后解锁",
      "automatic": true,
      "priority": 10
    }
  ]
}
```

| 字段 | 要求与行为 |
| --- | --- |
| `schema` / `skins` | schema固定1；数组最多4096项 |
| `id` | 全库唯一，最多128字符；不能用`custom`、`original`、`automatic` |
| `commander` | 将领定义ID，例如`ew4.commander.1`，必须存在 |
| `name` | 名称或字符串表键，最多256字符 |
| `image` | `rewrite/`下PNG；每图≤4 MiB，宽高均为1～4096；建议方形384×384或更大 |
| `marker` | 可省略，默认复用image；独立小头像同样为PNG，建议128×128；局内按原指针圆形裁切 |
| `unlock` | 可省略/空数组为直接解锁；否则所有目标ID都须已完成，不必领取奖励 |
| `unlockDescription` | 未解锁时显示的说明或字符串键，最多1024字符；省略显示通用说明 |
| `automatic` | 默认false；true时可随目标完成自动启用 |
| `priority` | 默认0；已解锁自动皮肤中数值最高者优先，同值按id的序号顺序选择 |

任务、成就条件复用[`metagame.objectives`](OBJECTIVES.md)，例如：

```json
{
  "schema": 1,
  "objectives": [{
    "id": "demo.first-commander",
    "kind": "task",
    "name": "首位将领",
    "description": "获得任意一位将领",
    "conditions": [{"type": "commanderOwned", "count": 1}],
    "reward": {},
    "internal": true
  }]
}
```

目标文件按既有接口注册到 `extension.json` 的 `metagame`。`internal:true`隐藏任务条目但仍记录完成，适合剧情或其他条件解锁；条件名称/字段沿用目标接口，不增加可执行脚本入口。多个条件可组合，已完成状态会持久保存。

玩家选择“原始头像”、上传或某一皮肤时，手动选择优先；选择“跟随剧情自动切换”恢复自动选择。锁定皮肤显示解锁说明且不可选择；已经完成的目标不需要重复领奖。自动切换刷新外观，不改变将领战斗属性。

皮肤清单及PNG属于外观资源，不参与战斗规则指纹，单独改皮肤清单或PNG保留相同逻辑存档身份；新增/修改目标、技能或其他规则仍按既有逻辑版本隔离。更新Mod必须重新封包并正常导入，不能直接修改已安装目录。
