# API 3 被动光环与属性示例

将本目录 `rewrite/` 合入作者包，合并已有 extension.json 的入口后用 ModTool 封包。示例给法国二级重炮增加四项演示技能，数值仅用于展示接口，不是截图中六个技能的平衡方案。可将 unitBindings 改为 commanderBindings 并填写实际将领 ID。

- `example.aura`：本方回合开始给距离 1 内友军（含自身）施加 2 回合状态；包含外交盟友，排除敌人。`scope:area` 的圆心为技能来源所在格。
- `example.capacity`：自身最大生命 +100、最大射程 +1、免粮、忽略普通地形额外移动消耗。
- `example.morale`：周围友军士气设为高昂，沿用领导力、持续时间、局部惩罚与缺粮结算。
- `example.conditions`：来源在平原、距离为 2、目标位于 city 建筑时伤害 ×1.25，使用 JSON `when`，不需要 Lua。

五类兵种专家可各自定义状态，再在施加状态效果上以 `when.targetCategories:["infantry"]` 等筛选受益兵种；可使用 infantry/cavalry/artillery/naval/fort 或自定义类别。状态内的伤害效果不加条件时，对受益者攻击的所有目标生效；对骑兵增伤 25% 则在伤害效果上写 `when:{"targetCategories":["cavalry"]}`、value:1.25。`targetTag` 仍可筛选自定义细分标签。

`when` 的各字段同时满足，单个数组中任一值匹配即可；空数组不限制。支持 sourceCategories/targetCategories、sourceTerrains/targetTerrains、sourceBuildings/targetBuildings、sourceInBuilding/targetInBuilding、minDistance/maxDistance。地形名沿用实际 def_terraintype：land/sea/plant/forest/desert/hill/mountain。建筑名沿用实际 BuildingKind，如 city/farmland；自定义地形和建筑也可匹配。unit.attributes/movement.modify 仅接受稳定的兵种条件，拒绝位置条件。

仅复杂自定义规则需要 Lua。`rewrite/scripts/conditions.lua` 保留同一条件的可选写法：将 example.conditions 的效果改为 `script:"rewrite/scripts/conditions.lua",function:"conditional_damage"` 并去掉 when；加载器会自动收集被引用的脚本。

光环在回合事件时取样，不是跟随移动实时重算。状态在受益单位所属方回合结束后减 1；duration:2 可覆盖该方本回合结束后的敌方行动。refresh 同名不叠加，多个来源共享同名状态时刷新持续时间；不同 ID 的状态分别叠加。状态不再递归施加或移除状态。

最大生命改变时保留缺失生命量，降低上限最低保留 1 HP；反复刷新不重复加血。固定上限加成和光环加成分别计算，存读档不再累加。忽略地形不突破禁行格、山口最低代价、渡河、上下船或海陆限制。

详见 [通用技能接口](../../docs/porting/RULE_EXTENSIONS.md)。旧版客户端不支持这些新增 API 3 操作，需使用包含本次实现的新客户端。
