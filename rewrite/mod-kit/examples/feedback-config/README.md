# 商店、刷新条件与战役说明示例

API 2 可打包覆盖包，字段见[指南](../../docs/porting/METAGAME_AND_BRIEFING.md)。默认保留两种货币初始各2000万及无限免费刷新；示例首关沿用原版窗口、左侧将领头像和两栏评分目标，在说明区追加将领头像和地图图片，超出后上下滚动；实际目标为存续至第3回合。仅用于作者测试，不覆盖原版账户。已有扩展清单时合并字段。`rewrite/images/briefing.png` 为原版战役背景示意，可替换成自己的PNG。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\feedback-config" --id=feedback-config --name="商店与任务说明" --version=1.0
.\Ew4.ModTool.exe verify "D:\mods\feedback-config"
```

有限次数和固定商品位示例，替换包内metagame.json即可。商店每10分钟恢复1次，一星学院每赢2局恢复1次；其他学院保持无限制。0号位不随刷新补货，1号位恢复3份：

```json
{
  "schema": 1,
  "initialMedals": 20000000,
  "initialBadges": 20000000,
  "commanderSectionCapacities": { "0": 30, "1": 25, "2": 40 },
  "refresh": {
    "shop": { "limit": 3, "recovery": { "intervalSeconds": 600, "amount": 1 } },
    "academy.0": { "limit": 2, "recovery": { "victories": 2, "amount": 1 } }
  },
  "shopSlots": [
    { "slot": 0, "pool": "consumable", "items": ["ew4.item.11"], "refresh": false, "quantity": 2 },
    { "slot": 1, "pool": "consumable", "items": ["ew4.item.12"], "refresh": true, "quantity": 3 }
  ]
}
```
