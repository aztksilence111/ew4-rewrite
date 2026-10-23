# 可配置编制数量（API 2）

将本目录的 `rewrite/` 合并至 Mod，在 `formation-counts.json` 的 `profiles` 中按完整兵种档位 ID 设置 1–99 的整数，例如：

```json
{"schema":1,"profiles":{"ew4.unit.others.militia.0":5}}
```

空配置保留原版各档位 1/2/3 编。配置由服务端校验并通过 catalog 的 `formationCounts` 返回，招募与部队属性共用；1–3 编使用明暗士兵标记，超过三编显示士兵图标 × 数字。未知 ID、0、负数、小数、字符串和超过 99 的数值均拒绝。

这是编制数量接口：数量与原版 grade 的三个招募档位解耦，不自动生成额外卡片，也不自动倍增生命、伤害、费用或消耗。实际战斗属性由 Mod 的兵种数据显式定义；修改后重新 seal/import，使逻辑版本和存档隔离生效。
