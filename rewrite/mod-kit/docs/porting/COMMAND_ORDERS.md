# 可选将领指令（API 3）

技能扩展通过 `ExtensionSkill.Order` 声明指令；未配置时沿用原游戏行为。接口类型及校验以 `Ew4.Core/CommandOrders.cs`、`CommandOrderValidation.cs` 为准。

`Mode` 支持 `grant`、`heal`、`morale`、`swap`、`reactivate`、`assault`、`guard`、`plan`、`summon`、`income`、`discount`、`followup`。`Event` 默认为 `activate`。`Source` 和 `Recipients` 使用 `OrderSelector`，按类别、能力、地形、建筑、条件、兵力、阵营、半径、数量和排序筛选单位；`IncludesSelf`、`RequiresFresh`、`SelectSelf` 控制目标选择。

`Bonus` 配置攻防反击、治疗士气、移动、消耗豁免、资源折扣和攻击上下界。`Duration`、`Interval`、`Uses` 控制持续及使用次数；`Followup`、`Plan`、`Profiles`、`Resource`、`Income`、`IncomeCap`、`Price`、`SourceLimit`、`Unlock` 等字段由各指令模式校验。AI 可使用合法指令，相关状态随战局存档保存。应先通过 Mod 检查器验证配置，再验证具体战局效果。

将领奖励资格可通过 `CommanderRewardUnlocks` 配置：未列出的将领保持原规则，值为 `null` 时锁定，值为目标 ID 时要求账户已完成对应目标。目标可设置内部标记及剧本、阵营范围；具体角色与平衡数值属于 Mod 内容。
