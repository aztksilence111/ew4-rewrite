# 剧本AI部队协防区域

适用于 0.2.42 及之后的客户端、Mod API 3。默认无配置，原版及其他部队继续沿用现有AI。`aiOrders`是敌我非玩家单位的原生AI规划约束，与玩家的`aiControl`界面配置独立。

在`rewrite/extension.json`加入`"aiOrders":"rewrite/ai-orders.json"`，文件结构如下：

```json
{
  "schema": 1,
  "scenarios": {
    "campaign7_02": {
      "campaign7_02.unit.31": {"center": 306, "radius": 3},
      "rc02.enemy.naval.yingrui": {"center": 306, "radius": 3}
    }
  }
}
```

`center`为当前剧本的零起点格号，必须在地图内；`radius`为六角距离0至6，含中心。键为稳定部队ID，可预先覆盖事件增援ID，单位尚未入场时不会凭空生成部队。

区域内保留正常射击和移动后射击，战术移动终点限在区域内；不再生成全图战略追击。区域外优先按真实移动成本寻找返航路径，分回合返回，正常遵守地形、占位、行动力；没有路径则留待后续回合，不传送、不自动购买运输。海军返航终点必须是可通行水格，中心可以是陆上的防守据点。

规则属于加载的剧本配置，计入内容修订。存读后重新应用，不把位置写死或重置已有进度。变更配置会改变Mod逻辑版本及存档命名空间，现有局迁移须使用正式存档校验与重新编码；不能直接编辑校验和或把旧局重新开场。

实现入口：`ExtensionLoader`、`ClientContent.Start`、`WorldAi.TacticalMoveCandidates`和`WorldAi.StrategicCandidates`。最小配置即可使用，无需Lua脚本或复制测试Mod。
