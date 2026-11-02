# 被动光环与通用技能接口补齐

## 对照结论

用户截图所述限制与当前代码对应：ExtensionLoader 原来只允许主动技能施加状态；被动 turn.start 即使已有范围选取也会被校验拒绝。技能缺少最大生命、普通攻击/反击射程、免粮和士气操作；战斗查询缺少真实距离及地形/建筑上下文。图片中的限制是待核对问题陈述，不是工程执行指令。

截图 1004 战略大师、1005 炮兵专家、1006 骑兵专家、1007 步兵专家、1008 舰船专家及 1019 自由美利坚可以改用本次通用接口表达。这里只补引擎及作者接口，未取得该用户的原始 Mod 定义，未代填六项技能的完整数值或改动本地其他 Mod。

## 已实现

- API 3 普通被动事件开放 apply_status/remove_status，支持 source/target/area、半径、外交友军和逐目标条件。状态自身禁止递归施加/移除状态。
- unit.attributes 提供 max_health_add、range_add、food_exempt、ignore_terrain。独立保存修正，不改基础兵种数据；生命保留缺失量，重复刷新不重复加血，状态到期和将领更换恢复有效值。
- 有效生命/射程接通战斗、反击、AI、治疗、整编、回合、事件生成与客户端；免粮接实际收支及原后勤缺粮士气豁免，地形移动仍尊重禁行/山口最低代价/渡河/海陆限制。
- morale 操作设定 -3～1 士气基线，复用持续时间、领导力、局部惩罚和缺粮结算；支持范围。
- 通用 when 为纯 JSON：sourceCategories/targetCategories、sourceTerrains/targetTerrains、sourceBuildings/targetBuildings、sourceInBuilding/targetInBuilding、minDistance/maxDistance。字段之间 AND，数组内 OR。属性/移动上限查询拒绝不具备上下文的位置条件。
- Lua 只供复杂扩展，可选读取实际距离及双方位置/地形/建筑/士气/有效属性。普通战斗、反击与溅射共用判定，JSON 和同义 Lua 的伤害与随机状态一致。
- API 3 新开局中已进入 Active 的首个回合补触发 turn.start；加载存档不重放事件。API 1/2 沿原触发顺序。

## 作者入口

[接口说明](../rewrite/mod-kit/docs/porting/RULE_EXTENSIONS.md)与[最小示例](../rewrite/mod-kit/examples/passive-auras/README.md)。示例默认不加载 Lua，额外脚本仅演示复杂扩展路径。声明 schema:1/apiVersion:3，并使用包含本次实现的客户端；旧版 API 3 客户端仍可能不认识新操作。

光环按触发时位置取样，非实时跟随。duration 在受益方回合结束递减，当前回合 duration:1 会在本回合结束到期；跨敌方行动的支援可用 duration:2。相同状态 ID 的多来源按 stacking 处理。民兵等细分类别可沿用 targetTag 与兵种 traits，主要兵种类别直接用 when.targetCategories。

## 验证

- 核心及相关回归 49,269 项，错误 0：新增专项 48，含真实剧本绑定/首回合、范围敌我/兵种/距离筛选、生命刷新/到期/读档、实际移动地形限制、收支免粮、士气及叠层设定、纯 JSON 条件、可选 Lua 等价、射程攻击/反击、将领派遣/更换/卸任、事件卸任和非法条件拒绝；另覆盖旧通用技能、将领、回合、战斗、移动、事件军队、上下文、AI、道具、整编与旧档。
- 实际 ModStore 封包/验证/导入/启用/开局/存读档/导出再导入 9 项，错误 0；原版槽原始字节保持。
- Web Release、Android ARM64 Compile 通过，零警告/错误。Android 使用已有 .codex/tools/dotnet-android、android-sdk、android-jdk 显式路径；系统 SDK 缺工作负载的初次尝试不计成功验证。
- git diff --check 无空白错误。清理凭据保存在 build/maintenance-20261102/passive-skill-interfaces-cleanup.json。
- 已核绝对路径、重解析点、活动消费者和 Git；清理本轮隔离构建/导入测试/道具回归夹具及报告 3,482 文件、339,708,963 字节，剩余 0、错误 0、Git 状态保持。源码测试与最小示例保留。

未制作 APK/Windows ZIP、未更新 GitHub Release、未做 Android 真机验收或实际浏览器交互验收。当前在线玩家服务/账户/存档/已安装 Mod 未操作。正式发行仍需按项目双端打包和存档导出回归要求执行。

复跑：Ew4.Verify 和 Ew4.ModChecks 均支持参数 `<仓库根> --passive-interfaces-only`，源码测试保留；测试运行资料在检查后清理。
