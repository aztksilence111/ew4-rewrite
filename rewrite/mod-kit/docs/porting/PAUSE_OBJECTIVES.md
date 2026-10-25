# 暂停界面战局目标栏

包含本功能的 R 版在战役/征服暂停时显示左侧目标栏。暂停菜单始终居中，目标栏按其实际边界缩窄或收短，内容纵向滚动。目标栏不留摄像头安全区，顶栏和底栏直接从屏幕左边缘开始。此处是本局胜负及评价提示，与指挥部 `objectives` 任务/成就分别注册。

## 接入

在包根 `rewrite/extension.json` 中使用 API 2 或 3，增加：

```json
{"schema":1,"apiVersion":2,"pauseObjectivesPresentation":"rewrite/pause-objectives.json"}
```

`pause-objectives.json` 为剧本 ID 到配置的字典；精确实际 ID 优先，`*` 为全局默认。隐藏关/分支按实际 ID 匹配。省略入口使用内置样式。

```json
{
  "*": {
    "title":"战局目标",
    "width":370,"maxHeight":662,"fontSize":16,"rowGap":3,
    "slideMilliseconds":280,
    "background":"#e9e7e1","headerBackground":"#414442",
    "textColor":"#393b39","accentColor":"#58735e",
    "starImage":"star_middle.png","emptyStarImage":"star_campaign_gray.png",
    "winTitle":"胜利条件","limitsTitle":"失败限制","ratingsTitle":"胜利评价",
    "sections":["win","limits","ratings"],
    "conditionText":{}
  }
}
```

## UI 字段

| 字段 | 范围或行为 |
| --- | --- |
| `title`、`winTitle`、`limitsTitle`、`ratingsTitle` | 文字或字符串表键，最多256字符 |
| `width` | 240～480，首选像素宽，实际宽度避让暂停菜单 |
| `maxHeight` | 240～1000，首选高度上限，实际高度受屏幕、暂停框及安全区限制 |
| `fontSize` | 14～24，正文像素字号 |
| `rowGap` | 0～16，目标行上下留白 |
| `slideMilliseconds` | 0～800，滑入时长，遵守减少动画偏好 |
| `background`、`headerBackground`、`textColor`、`accentColor` | 六位十六进制颜色，例如 `#414442` |
| `backgroundImage` | 可选已登记图集图片名，替换背板图 |
| `starImage`、`emptyStarImage` | 已登记图集图片名，分别表示实星/空星 |
| `sections` | `win`、`limits`、`ratings` 各一次，可改变分区顺序 |
| `conditionText` | 条件 ID 到说明文字/字符串键的映射，最多1024项，每项最多2048字符；不修改逻辑 |

这是约束式 UI 配置接口，不接受 HTML、JavaScript、任意 CSS 或完整原生窗口树。宿主管理递归分组、状态、自动换行和滚动容器，保证关键条件关系完整。新增图像须登记图集并提供 PNG；也可覆盖现有同名图片。错误数值、颜色、未知图片、重复键、未知字段、路径越界及未知剧本会被校验拒绝。

自定义条件 ID 为 `win`、`win.0`、`win.1.0` 等，失败树为 `lose` 及数字子路径。原版分支为 `win.enemies`、`win.objectives`，限制为 `limits.survival`、`limits.targets`、`limits.round`。多人另有 `win.survivor`、`win.targets`、`limits.neutral`。例如 `"conditionText":{"win.0":"占领全部战略要地"}`；覆盖前须核对该剧本当前树，阶段替换后按新树路径匹配。

## 规则与刷新

- 从当前 `ScenarioVictory`、自定义规则或原版规则生成只读视图，保留全部/任一分组、失败树、胜负优先级和当前人类玩家视角。
- 星级区间直接调用结算函数生成，并与实际通关回合上限相交；满星期限当回合可满星，下一回合过期，过期不等于本局失败。
- 征服按宿主真实星级参数显示，没有有效满星区间时不伪造5星。征服勋章另有算法，本栏不估算奖励。多人不套单人星级/超时/失败树，按当前国家独立或同盟胜利展示。
- Lua 条件显示特殊条件及规则描述或作者文字，`met:null` 表示未知；不额外执行脚本，不消耗随机数。
- 打开暂停时核对最新视图；隐藏期间不轮询、不计算。可见期间在途动作提交后核对。展示相同则版本不变，前端不修改内容节点、不重置滚动。资源默认显示门槛与真假；没有逐回合倒计时。
- `POST /api/pause-objectives` 沿用 `q` JSON 参数，接受可选 `knownVersion`；返回 `context`、会话 `revision`、独立内容 `version` 与 `snapshot`。版本相同返回 `snapshot:null`，客户端保留缓存；没有缓存时省略 `knownVersion`。
- 快照包含剧本、国家、`model.win`、`model.limits`、`model.ratings`、描述、注记与展示配置。失败树的真假表示失败条件成立，正向限制的真假表示仍满足限制。
- 缓存不写存档；读档、重开、国家切换按 battleId/玩家上下文区分。配置参与现有扩展指纹及 Mod 资源流程，遵循现有存档兼容规则。

示例：[pause-objectives](../../examples/pause-objectives/README.md)。核验：`dotnet run --project rewrite/Ew4.Verify -c Release -- <root> --pause-objectives-only`；浏览器：`node tools/verify_pause_objectives.cjs <URL> <证据目录>`。
