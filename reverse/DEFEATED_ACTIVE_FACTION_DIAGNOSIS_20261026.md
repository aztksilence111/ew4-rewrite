# 当前国家灭亡后卡回合：原因检查

后续状态：用户已授权修复，源码修改与验证完成，见[修复记录](DEFEATED_ACTIVE_FACTION_FIX_20261026.md)。下文保留诊断阶段的历史结论。

2026-10-26，依据用户提供的 1798 征服瑞士回合截图检查。截图提示为 `Active faction has been defeated`。本轮只诊断，未修改游戏实现、玩家资料或发行包；未取得玩家实际存档，以下为源码分析与最小场景复现，不等同于已逐步还原该玩家完整战局。

## 结论与触发条件

发现可稳定复现的回合状态缺陷：当前 AI 国家进攻，被反击杀死最后的存续部队，且没有其他有效存续建筑时，该国正常灭亡，但当前国家和回合阶段未切换。只要整局尚未结束，下一步 AI 行动就会报截图中的英文错误并中止。该机制与玩家描述高度吻合，不要求中立国专属逻辑。

“当前国家已灭亡”指当前正在行动的瑞士，并非必然指玩家败北。灭亡条件仍按国家的 SurvivalRules 判断；不是任意一名将领死亡都会触发。

## 代码路径

1. `rewrite/Ew4.Core/WorldBattle.cs` 的 `Transaction.Resolve` 先结算反击对进攻方的伤害；`Damage` 调用 `WorldDeaths.Remove`。
2. `rewrite/Ew4.Core/WorldDeaths.cs` 检查存续资产，调用 `WorldEvents.Surrender`；后者在 `WorldEvents.cs:138` 设置 `Defeated = true`，移除部队并转移领地，保留原 ActiveFaction/Phase。
3. 战斗结束会检查全局胜负并提交，但在战斗继续时未把已灭亡的当前国家转入回合过渡。可得到 `ActiveFaction=swiss`、`Phase=Active`、`Defeated=true`、`Outcome=Continue`。
4. `WorldAiSteps.cs:12` 进入下一次规划前调用 `WorldActions.Ready`，在 `WorldActions.cs:68` 抛出 `action.defeated`。批处理会返回已提交的战斗状态和此错误；普通单步同样失败。
5. 手动结束回合也会在 `WorldTurns.cs:69` 被拒绝。虽然 `WorldTurns.Continue` 内部已有跳过灭亡国家的处理，正常入口却到不了这里：`Resume` 在 `WorldTurns.cs:101` 拒绝 Active 阶段，`EndTurn` 先拒绝已灭亡国家。
6. 保存会通过校验并保留此状态；`ClientGame.Load` 解码后未规范化这一组合，前端加载后继续运行 AI，再次得到同一错误。因此重载持续报错是卡住状态被保存的结果。

## 验证

使用当前 Core/Persistence 源码，通过公开的 `WorldBattle.Preview/Attack`、`WorldAi.PlanStep`、`WorldTurns.EndTurn/Resume` 和 `WorldSave.Encode/Decode` 构造独立四格、三国场景；保留第三国以避免整局胜利提前掩盖缺陷。瑞士最后部队 1 HP，双方计算伤害均为 20，玩家防守部队 100 HP。

- 11 项检查，错误 0；诊断程序构建运行成功。
- 战斗后瑞士灭亡，阶段仍为 Active，当前国家仍为 swiss，整局 Continue；存在 faction.defeated 通知，无 faction.ended 通知。
- 下一步 AI 和结束回合均返回 `action.defeated`。
- 存档编码、解码、重编码字节一致；重载后 AI 同样返回 `action.defeated`，普通 Resume 返回 `turn.phase`。
- 另有瑞士部队、另有有效城市两个对照均保持国家存续，不触发该灭亡状态。
- 仅在内存诊断副本中将阶段规范化为 EndFactionEvents 后 Resume，可推进到玩家的 Active 回合。这验证已有过渡逻辑能跳过灭亡国家，不构成已实现的游戏修复或对玩家存档的处理。

## 修复方向

战斗和事件提交后统一处理“当前国家已灭亡但对局继续”的状态，使其进入既有回合过渡；同时为旧存档增加同样的恢复入口。须保留待确认对白、剧情事件、胜负优先级和随机数边界，避免重复结算已灭亡国家的回合末效果。不能仅屏蔽报错，否则回合指针仍然卡住。

后续修复应覆盖反击导致进攻国灭亡、普通击杀非当前国、双方死亡、消耗型兵器、脚本/状态效果、带对白的过渡、批量/普通 AI 及旧卡死档恢复。本轮未实施或打包。

## 清理

完成后核对 Git、活动进程及真实路径，删除本轮 `build/defeated-turn-diagnosis-20261026/` 临时诊断夹具及全部编译产物，共 76 文件、5,621,833 字节，删除成功，错误 0。未启动预览服务，未生成 APK/ZIP，未访问或修改玩家存档。保留本报告作为诊断结论。
