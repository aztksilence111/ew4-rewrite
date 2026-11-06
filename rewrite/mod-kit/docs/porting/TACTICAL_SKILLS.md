# 可选战役机动、领土光环与恢复网络

API3技能定义可声明 `maneuver`、`territoryAura` 或 `recoveryNetwork`。未声明时维持原技能结算；具体人物、兵种选择和数值由Mod制作源决定。启用这些新增字段需要包含本接口的宿主，旧0.2.31宿主不能直接读取。通用实现位于 `Ew4.Core/TacticalSkills.cs`，不包含人物或阵营ID。

`maneuver` 用于 `kind: active, target: self, endsAction: false`。额外目标通过 `/api/action` 的 `units` 数组提交，本部自动计入；不提交或空数组表示只作用本部。接口返回 `multiple`、`maximumTargets`、`includesSelf`、可选对象和冷却。服务端拒绝重复、自身、非本国、超距、已移动/攻击、运输态及不符合兵种配置的额外目标；客户端限制不替代服务端验证。

可配字段为 `maximumOthers`（0–16）、`movement`（0–2）、`protectionTurns`（本接口固定2）、`netDistance`、`attackPenalty`、`counteroffensive`、`evasion`、`evasionCeiling`、`ignoreRetention`、`recipientCategories`、`excludedAbilities`。百分比用0至1小数。首次行动前发动；本部须地面机动队，普通移动路线和行动限制继续生效。新增移动与既有扩展移动预算取高。保护与反攻即时检查来源距离，首轮进攻惩罚不会通过离开范围消除。

固定受益名单、授予格、首轮结束格、反攻资格与首次攻击履历保存在单位的 `maneuvers`。来源或受益者换将撤销；来源退场/解绑不再生效；升级保留，预览不消费，合法攻击提交才消费反攻。将领技能冷却按将领与实例保存，不能靠换部队刷新。移动/攻击履历阻止连击保留行动时再补开技能。第三个本方回合开始保护到期。

新增回避仅防地面机动队普通主攻，排除反击、溅射、航空、舰艇和固定火力。按原始回避计算可补部分；在原破回避分支后仅保留配置的份额，忽略建筑不抹去该非建筑保护。等价新增减伤与领土防护取高并共用专属25%/总40%预算；专属增伤封35%、与通用合计50%，首轮负修正另乘。原地形/建筑回避及原版技能触发继续使用原结算。

`territoryAura` 用于被动，可配 `radius`、`attack`、`defense`。判断来源实际控制者，缺失时查辖区中心；本国和外交友方不触发，未知归属不触发。受益者限本部及指定半径内本国地面单位，同格其他单位不因共格受益。同名/同类光环取高；普通主攻与反击有效，溅射通道不接入。

`recoveryNetwork` 用于被动，可配 `buildings`、`categories`、`excludedAbilities`、`facilityRadius`、`perFacility`、`facilityHealing`、`adjacentRadius`、`adjacentHealing`、`facilityEffect`、`adjacentEffect`。每个本国有效设施独立选人，按生命比例、距离、稳定ID排序；用同一恢复前快照选全部名单，不因重叠重新补选。近身恢复不含本部或同格其他单位，可包含非步骑地面队。

按单位与效果类别取最高，再把不同类别相加，无额外总量封顶。类别名为Mod自定义稳定ID；设施类型、位置或来源不同不自动成为新类别。同类多个来源同一快照合并；所属国/类别/轮次履历防止读档或换将重复恢复。独立恢复技能保留各自效果，最终不超损失生命。

新增选项不得同时混入该技能的普通 `effects`。API版本、技能种类、范围、百分比和恢复类别均在导入时校验；旧档缺失新字段采用空列表/假值，不生成技能加成。`--tactical-skills-only` 验证真实Mod定义、战斗、回合、AI、客户端命令、存读档及无配置隔离；原战斗/回合/移动/AI回归继续通过。
