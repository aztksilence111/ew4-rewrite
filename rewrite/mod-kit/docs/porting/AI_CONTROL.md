# 玩家 AI 设定接口

适用：含本功能的 rewrite 客户端，Mod API 3；Android 与 Web 共用。此功能未随旧 0.2.20 正式包发布。

在 `rewrite/extension.json` 中加入 `"aiControl": "rewrite/ai-control.json"`。文件直接包含呈现字段，省略字段使用内置默认值。可从 [最小源码示例](../../examples/ai-control/rewrite/ai-control.json) 起步。

## 文字与按键

| 字段 | 默认值/含义 |
| --- | --- |
| title | AI设定 |
| buttons | 依次为指定战斗方针、框选单位、指定行动目标、开始执行、停止执行，共5项 |
| policies | 玩家指挥、自由行动、积极进攻、消极进攻、原地防守、逃离战场，共6项 |
| confirmText / cancelText | 确认 / 取消；确认键始终保留独立文字，不使用圆形对勾 |
| cancelSelectionText / autoBattleText | 地图取消、自动战斗文案；旧 cancelDraftText / clearGoalText 仍接受但不显示独立操作行 |
| unitCountText / goalText | `{count} 支部队` / `目标 {cell}`，替换相应占位符 |
| noPolicyText / noGoalText | 未设方针 / 未设目标 |
| clearSelectionText | 取消全部选中；框选地图的一键清空按钮 |
| goalOrderText / goalCountText | 目标优先顺序 / `{count} 个目标` |
| raisePriorityText / lowerPriorityText / removeTargetText | 排序上移/下移/移除按钮的提示文案 |
| targetLimitText | 最多设置32个目标 |

修改文案不修改语义。方针协议固定使用 `manual/free/aggressive/cautious/hold/retreat`，前三步骤确认及开始/停止执行不能通过改名或调整图片绕过。

## 图像、颜色与布局

| 字段 | 范围/默认值 |
| --- | --- |
| panelImage / buttonImage | 默认 null（纯色无阴影面板）/ 绿色按键 PNG；图片使用 `client/` 开始的资源路径 |
| pressedButtonImage / confirmImage | 可选按下态 / 文字确认键底图，省略时复用 buttonImage |
| width / buttonHeight / gap | 120-320 / 28-64 / 0-24；默认224 / 40 / 10逻辑像素 |
| fontSize / titleFontSize | 10-24 / 10-30；默认16 / 20 |
| targetListHeight | 72-360；默认180，侧栏摘要和目标排序列表的最大内容高度 |
| confirmWidth / confirmHeight / cancelWidth | 80-240 / 36-96 / 80-200；默认112 / 46 / 100 |
| policyWidth / policyButtonHeight | 180-480 / 28-64；默认236 / 36 |
| offsetRight / offsetY | 右边距0-80 / 垂直偏移-120至120；默认0 / 0，侧栏贴屏幕最右边 |
| panelColor / headerColor / textColor / buttonTextColor / selectionColor | `#RRGGBB`，分别控制面板、标题栏、正文、按钮文字及选中轮廓 |
| badgeText / badgeImage | 默认 AI；可选 PNG 替换默认文字兵牌 |
| badgeWidth / badgeHeight / badgeX / badgeY | 8-64 / 8-48 / -80至80 / -80至80；默认20 / 12 / -10 / -27 |
| badgeColor / badgeTextColor / badgeBorderColor | 默认标记背景、文字、边框颜色；PNG 替换后由图片决定 |
| arrowImage | `client/manifest.json` 的 buildings_hd 图集中已存在的精灵名，默认 arrow_red.png |
| lineColor / lineWidth / dashLength | 高亮虚线颜色 / 宽1-8 / 节长2-32；默认 #ffdb65 / 2 / 8 |

图像可在作者资源包中替换或新增合法 `client/` PNG，再修改配置路径。拒绝路径穿越、非 PNG、过大文件、未知字段和超出范围的值。窄屏仍执行防重叠约束，尺寸不是强制挤出屏幕的绝对大小；共用布局由 `ai-control.css` 维护，Mod 作者使用本表中的呈现字段调整。

暂停时两侧栏在任务内容准备好后同步开始伸出；AI栏由右向左，任务栏由左向右，共用任务目标呈现接口的 `slideMilliseconds`（默认280ms）。重复暂停和从地图选择返回也会同步播放；减少动态效果模式同时关闭两栏动画。紧凑布局展开AI栏时，任务栏仍显示在左上方。

## 执行规则

方针、单位、目标必须分别实际设置并确认后，才能点击开始执行。未启动及停止后，结束回合直接轮转；自动战斗选项也不能单独启动。普通托管在结束回合时执行，自动战斗可连续推进；两者都只操作当前授权的人类席位，不招募、派将、买船或使用道具。

更换所选单位后必须重新确认目标。在目标地图再次点击已选目标格会取消选中，按「确认」同时清除所选本国单位已保存的目标和草稿目标；在框选地图将单位全部取消选中再确认，重置本次设定草稿。旁边「取消选择」只返回并保留进入前的设定。设置步骤不提前启用托管，执行中先停止才能编辑。

选择记录在行动提交和读档时自动剔除已死亡、解散或转属的单位，框选界面再次按存活本国单位过滤。计数仅包含有效单位；剩余0支时单位确认失效，仍可框选新部队。框选地图「取消全部选中」仅清空当前预览，确认后才重置草稿，取消返回则保留原设定。

目标最多32个、不重复，点击地图累加，再点已选目标移除。排序列表序号就是优先级和执行先后，使用上下箭头调整。确认时 `player_ai_config` 的 `goals: [格号1, 格号2, ...]` 按原顺序保存；旧 `cell` 单目标入口继续兼容。每支所选部队独立保存剩余目标，依次抵达并弹出队首；第一目标受阻时保留等待，不擅自跳到后续目标。目标被本国单位占据时，抵达相邻格也视为集合完成。原移动力、玩家本回合已操作跳过等规则保持；全队列完成后继续既定方针。

单位命令与原版 Behavior/RestoreBehavior/BehaviorExpiry 分开。存档世界版本7保存目标顺序、执行开关、配置、队列和玩家操作记录，仍读取版本4/5/6；6版单目标和执行设定完整保留，4/5载入当回合保守跳过玩家补行动。暂停等待当前提交后暂停，停止取消后续玩家托管和自动轮转，已完成动作不倒回。
