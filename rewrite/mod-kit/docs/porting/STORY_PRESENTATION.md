# 剧情播放器（API 3）

电脑和 Android 共用播放器及 JSON。支持开局、事件结果和局末调用，多人同屏、整图／分层、分支选项、历史、逐字／整句、自动、1×／2×／4×／8×和跳过。背景铺满；底部半透明对话区贴住左右及底边；顶部保留紧凑工具，“自动”显示图标与文字。

可运行源码见[最小示例](../../examples/story-presentation/README.md)。既有 API 3 不代表所有旧版本已经包含此增补接口，旧引擎需升级。本接口为可选项，未注册时保持原关卡流程。

## 注册与资源

```json
{
  "schema": 1,
  "apiVersion": 3,
  "storyPresentation": "rewrite/story/catalog.json",
  "scenarioEvents": "rewrite/story/events.json"
}
```

内置文件放 `rewrite/data/rewrite/story/`，APK 对应 `assets/rewrite/story/`。Mod 放自身的 `rewrite/story/`。JSON 引用用 `/`，无需填写 `assets/` 或磁盘绝对路径。`rewrite/` 指向包内自有素材；`client/` 可复用现有游戏素材。当前仅接受 PNG，单图不超过 4096×4096、16 MiB；不开放路径穿越、URL 或可执行资源。

```json
{
  "schema": 1,
  "sceneFiles": ["rewrite/story/opening.json", "rewrite/story/ending.json"],
  "backgrounds": {"room": "rewrite/story/backgrounds/room.png"},
  "characters": {
    "officer": {
      "name": "参谋",
      "poses": {"standing": {"width": 800, "height": 1200,
        "image": "rewrite/story/characters/officer/standing.png"}}
    }
  },
  "bindings": {
    "conquest1": {
      "openingStory": {"key": "opening", "scene": "opening"},
      "endingStories": [
        {"key": "ending.negotiated", "scene": "ending", "outcome": "victory", "ending": "victory.negotiated"},
        {"key": "ending.victory", "scene": "ending", "outcome": "victory"},
        {"key": "ending.special", "scene": "ending", "outcome": "defeat", "ending": "failure.commander_lost"}
      ]
    }
  },
  "theme": {"panel": "#0b1013", "text": "#f2f1e9", "accent": "#dac690", "opacity": 0.88, "dialoguePercent": 26, "fontSize": 24}
}
```

`scenes` 也可以直接内嵌场景字典；它与 `sceneFiles` 加载的场景 ID 不得重复。`texts` 是可选的共享文字字典，通过步骤 `textKey` 引用。角色定义、表情映射目前写在目录 JSON 内。

`theme` 色值为六位 HEX；透明度 0.5–1，对话区高度 22–40%，基础字号 16–36。手机有独立字号与最小高度约束。当前背景固定居中裁切，未实现自定义焦点、面板贴图、字体文件及转场遮罩。

## 台词与分支

每个场景文件：

```json
{
  "schema": 1,
  "id": "opening",
  "steps": [
    {"id": "hello", "background": "room",
      "actors": [{"id": "a", "character": "officer", "pose": "standing", "slot": "left"}],
      "speaker": "a", "text": "补给已经抵达。"},
    {"id": "decision", "speaker": "a", "text": "现在出发吗？", "choices": [
      {"id": "go", "text": "出发", "next": "depart"},
      {"id": "wait", "text": "继续休整", "next": "rest"}
    ]},
    {"id": "depart", "text": "部队开始集结。", "next": "done"},
    {"id": "rest", "text": "营地恢复了安静。", "next": "done"},
    {"id": "done", "end": true, "result": "ready"}
  ]
}
```

- 从数组第一步开始。普通步骤未写 `next` 时顺序进入下一步；`end: true` 或数组结尾结束。分支建议显式 `next`，避免误入其他路线。
- `text` 或 `textKey` 显示正文。`speaker` 引用舞台演员 ID，自动读取姓名并提亮该角色；`name` 可覆盖姓名。未设置二者时为旁白。
- 背景和演员会继承；`clearActors: true` 清场。`actors`、`updateActors` 均按演员 ID 合并。移除演员使用 `hidden: true`，重新显示用 `hidden: false`；换角色时请同时指定 pose／expression／parts，避免继承旧角色配置。
- `choices` 为单选，支持多个连续选择节点。每个选项有 `id`、`text`、`next`，可选 `condition`、`effects`。条件和结果复用[事件 API](SCENARIO_EVENTS.md)，条件不满足时禁用。作者必须确保进入选择节点时至少有一个选项可用，建议提供一个无条件后备选项。
- 选择在后端重新校验，只记录所选路线和待生效结果；失效、重复请求或资源不足均不记录选择。播放期间战局冻结，不改变地图、部队和资源，也不运行战局更新／普通事件重评。到达场景结尾后才一次提交实际选择路线的效果，再恢复事件续接和战局。
- 连续选择的条件会在隔离的预判状态中计入此前待生效结果，例如前一个选项预留的金币，避免重复花费；预判不提交战局。带脚本的结果也会在预判和完成提交时求值，脚本应保持确定性，不依赖外部副作用。
- 长台词自动分页；点击先补全文字／结束等待动画，再翻页或进入下一步骤。自动与跳过遇到选择均停止，不替玩家决定。
- `waitMs` 为自动阅读等待，0 使用文字长度估算。自动、倍速、字号、逐字和开局跳过保存在本机；读档恢复时暂停自动推进，避免刚载入便跨过剧情。
- 历史保留本次调用的已到达步骤、当时姓名及选择，不提供倒回并重新执行结果。
- 对话区、姓名行和文字行数固定，正文用离屏 Canvas 测宽并缓存分页，切句不反复改写正文试排版。仅在视口、字号或主题改变时重新取得布局参数；Mod 主题在进入游戏前加载。

ID 使用字母、数字、`.`、`-`、`_`，长度 1–128。目录最多 128 场景，单场景最多 1000 步；步骤及场景相互调用循环会拒绝，一局最多 2048 次调用。节点最多 16 次演员变更、32 动画、32 选项；正文最多 16000 字符，选项最多 2000 字符、64 个结果。

## 整图、拆分与动画

`pose.image` 为整图，可同时叠加 `layers`。分层使用统一画布坐标；层的 `x/y/width/height` 单位为画布像素，`order` 越大越靠前。

```json
{
  "width": 800, "height": 1200,
  "layers": {
    "body": {"default": "normal", "order": 0,
      "parts": {"normal": "rewrite/story/characters/a/body.png"}},
    "face": {"default": "calm", "order": 1, "x": 260, "y": 90, "width": 240, "height": 240,
      "parts": {"calm": "rewrite/story/characters/a/calm.png", "smile": "rewrite/story/characters/a/smile.png"}}
  },
  "expressions": {"happy": {"face": "smile"}}
}
```

演员可设置 `expression: "happy"`，或 `parts: {"face": "smile"}` 覆盖单层。不同动作可拆成多个 `poses`，每个 pose 有自己的整图或分层。

`slot` 支持 `left/midLeft/center/midRight/right`；也可指定归一化 `x/y`，默认 `y=1` 表示底边，`scale=1` 约为屏高 92%，支持 `mirror` 和 `order`。窄屏会按在场人数约束宽度，保持图片比例。单场景当前继承的角色应与每次更新的 pose、expression、parts 对应。

```json
{"id":"reply", "speaker":"a", "text":"遵命。",
 "updateActors":[{"id":"a", "expression":"happy"}],
 "animations":[{"actor":"a", "type":"hop", "durationMs":300, "amplitude":12, "wait":true}]}
```

动画支持 `hop`、`shake`、`move`、`fade`。`move` 用 `x/y` 指定相对终态的进场偏移；最终位置由演员坐标决定。`wait` 默认 true，倍速缩短动画时间。为同一角色同时配置多段 transform 动画会覆盖，作者应拆步播放；无骨骼／Live2D、配音、口型和动画时间轴。

## 调用与条件

开局由 `bindings.<剧本ID>.openingStory` 调用，在原关卡对白之前。事件结果：

```json
{"type":"play_story", "key":"council", "scene":"opening"}
```

事件结果遇到 `play_story` 会暂停，该剧情结束后继续执行剩余结果；调用事件在待播期间不视为 `event_completed`。剧情选项也可调用其他场景，`play_story` 必须是该选项最后一个结果，后继场景排在当前场景之后；依赖后继完成的效果写在完成条件事件内。

| 条件 type | 字段与含义 |
| --- | --- |
| `story_started` | `key` 对应调用已经开始 |
| `story_completed` | 调用完成，取消的调用不算 |
| `story_choice` | `key/node/choice` 命中已选选项 |
| `story_result` | `key/result` 命中完成步骤的 result，未指定时为 `completed` |
| `story_count` | `key/compare/value` 比较本局完成次数 |

`key` 是调用键，不是场景 ID。同一场景可以用不同 key 调用。除 count 外默认查该 key 最新实例；`scope: "any"` 查本局任意历史实例。条件不跨新局。普通事件链会在剧情释放等待后重评，剧情内即时分支使用选项 `next`。

## 局末与存档

固定顺序：胜利／特殊失败目标达成 → 锁定胜负与结局 ID → 完成当前已选路线 → 对应局末剧情 → 后端进度及奖励结算 → 结算窗口。`endingStories` 优先匹配 `outcome+ending`，再使用同 outcome 无 ending 的后备项，无匹配则不新增剧情。

结局 ID 来自既有事件 `{"type":"ending","key":"victory.negotiated","outcome":"victory"}` 或目标裁决。不同胜利结局可绑定不同 scene，特殊失败使用 `outcome: "defeat"`。终局后不再恢复战局和普通事件续接，取消普通待播剧情；既有活动对白可以完成。已播放自定义失败结尾时不重复通用失败对白。

终局期间的选项只允许 `state`、`add_state`、`play_story`，用于台词分支和后继演出。影响资源、胜负或评分的选项应放在终局之前；能够触发终局的普通选项，后继路线也必须只含安全的呈现／变量选项，以免在锁定结果后仍要求支付资源。当前通过冻结战局和限制终局效果锁定结算数据，没有单独复制一份评分世界。

自动存档只在剧情播放结束并释放等待后写一次，普通切句、翻页、选项选择和开场进入均不写自动存档；紧接其他剧情或局末剧情时继续延后，直到连续播放结束。播放中退出或崩溃会回到上次存档。手动保存仍可用。

存档包含剧情目录、调用实例、到达路线、选择、待生效结果、完成结果、待播队列和事件续接；播放中手动存档会保留未提交结果，恢复只重建画面，到场景结束才生效一次。旧剧情存档缺少待生效字段时视为空，不补执行旧引擎已经生效的结果。播放中不提前结算，已结算读档不重播或重复发奖。不改旧存档版本；无剧情的世界省略该可选字段，含剧情的新存档不能交给不识别此接口的旧引擎读取。

媒体必须在导入时通过检查。运行时图片解码失败会隐藏该图并允许继续；当前没有独立的损坏媒体重试面板。制作前请先检查导入校验和桌面／手机实际画面。
