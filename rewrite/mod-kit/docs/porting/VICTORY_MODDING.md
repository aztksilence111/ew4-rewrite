# 多阶段胜负条件与结算界面

此接口适用于包含本增补的宿主，旧0.2.6发行包不含结算界面绑定。电脑和Android共用规则与前端。完整可打包源码见[三阶段示例](../../examples/victory-stages/README.md)。

本地多人保留动态 `victory_rules`。战役由实际外交同盟合计目标、共享胜负，征服按单国完成 `win` 确定胜者；单人失败条件不结束整局。多人使用独立各国结果窗口，不套用本页单人 `victoryPresentation`。事件和当前席位语义见 [SCENARIO_CONTROL](SCENARIO_CONTROL.md)。

## 分阶段更新条件

沿用 `scenarioEvents`，每个阶段执行一次 `victory_rules`，完整替换当前条件。`victory_default` 恢复剧本原规则。条件树字段见[RULE_EXTENSIONS](RULE_EXTENSIONS.md)，事件和选择语义见[SCENARIO_CONTROL](SCENARIO_CONTROL.md)。

阶段使用 `event_completed` 或 `state` 记录先后顺序；事件在同一判定点可以连锁执行。一次性阶段不填 `repeat`。若需要循环修改，可使用已有 `repeat`。阶段条件、已执行事件、当前规则、待选弹窗均随存档保存。

```json
{
  "id":"stage.capture",
  "condition":{"type":"all","conditions":[
    {"type":"event_completed","key":"stage.prepare"},
    {"type":"cells_owned","faction":"@player","cells":[1234]}
  ]},
  "effects":[{"type":"victory_rules","victoryRules":{
    "win":{"type":"round_reached","round":20},
    "lose":{"type":"cells_lost","cells":[1234]},
    "descriptionKey":"守住目标至第20回合"
  }}]
}
```

`1234` 必须替换为目标剧本有效格号，`stage.prepare` 必须声明。中间阶段不要设置已能立即满足的最终胜利条件，否则本局会正常结束，不能在结束后再切阶段。可用未来回合门槛暂缓结算，或由最终事件的 `ending` 明确提交胜负。切换后的规则没有隐含原版红点限制；若仍需原条件，应显式写进新的组合树。弹窗等待玩家时暂停判定，放在按钮内的更新到确认后才提交。`requirePlayerSurvival` 默认 true，胜负同时满足默认失败优先。

示例在第1回合安装最终回合门槛；第2回合追加资源条件；第3回合确认后再替换为存续至第4回合。前两阶段均不结束对局。实际Mod可把阶段触发换成占城、击杀、外交、资源或Lua条件。

## 注册结算界面

在 `rewrite/extension.json` 增加可选路径，与技能布局 `presentation` 分开：

```json
{"schema":1,"apiVersion":1,"scenarioEvents":"rewrite/events.json","victoryPresentation":"rewrite/victory-screen.json"}
```

`victory-screen.json` 为剧本ID到界面的字典。`"*"` 是全局默认，精确剧本ID优先；普通/隐藏关ID分别配置。没有匹配配置则使用原版完整结算。规则和界面可以分别注册，也可同时注册。

每个界面配置：

| 字段 | 含义 |
| --- | --- |
| `layout` | 可选完整原生布局树；省略使用当前manifest的 `form_victory` |
| `defeatLayout` | 可选失败布局树；省略沿用 `layout`，再回退原版 |
| `bindings` | 数据和命令到控件路径的映射；省略整项使用下表原控件名，显式填写时为完整映射 |
| `victoryTitle` / `defeatTitle` | 字符串表键或文字，默认 `title_victory` / `title_failure` |
| `starImage` / `emptyStarImage` | 已登记图集的图片名，默认 `star_middle.png` / `diffcult_1.png` |

| binding键 | 默认控件 | 数据/操作 |
| --- | --- | --- |
| `name` | `text_battle` | 剧本名 |
| `round` | `text_roundval` | 结束回合 |
| `awardMedals` / `collectedMedals` | `text_award_val` / `text_gain_val` | 后端奖励/当前获得记录 |
| `minimumRatingRound` / `highestRatingRound` | `text_star` / `text_star2` | 评分回合门槛 |
| `thresholds` | `group_starlevel` | 失败/无星时隐藏的门槛组 |
| `stars` | `lbox_stars` | 五个星图 |
| `generals` | `lbox_general` | 派遣将领与空槽、军衔/爵位 |
| `continue` / `restart` / `exit` | `btn_continue` / `btn_restart` / `btn_exit` | 继续、重开、退出，三者必填 |

显式映射中，数据键可以省略或设为null；三个命令不可省略。胜利隐藏重开，失败隐藏继续。按钮仍执行宿主原操作；继续无下一关时回主菜单，重开使用最初选国。只想换原图可直接覆盖对应 `client/ui/<atlas>/<name>.png`，不需要配置新布局。

## 布局树与资源

布局使用当前 `client/manifest.json.layouts` 的数据格式，可直接内嵌到配置，免于复制整份manifest。每个节点为 `{"attributes":{...},"children":[...]}`；叶节点可省略children。所有attributes值均为字符串。根节点必填 `w` / `h`（80～2048）；子节点必填id/type。支持 `text`、`image`、`group`、`groupbox`、`listbox`、`grid`、`button`、`tmp_button`、`tmp_intitle`。最多256节点/12层。

坐标沿用原版：根窗口顶部27像素为宿主标题栏，顶层子控件y从27开始。常用字段：x/y/w/h，text/font/color/align，image的name，按钮frm1/frm2，容器children。字体和图片沿用当前manifest和内置字库；新增图片名须登记已支持图集并附PNG。节点属性和绑定区分大小写。

嵌套控件可以写 `group_id/control_id`，短ID须在整棵树中唯一。数据文本绑定到text，列表绑定到listbox/group/groupbox/grid，命令绑定到button/tmp_button。胜负两棵布局共用bindings，因此都要提供所需控件。控件不得重复绑定；会清空的星级/将领容器内不能再放其他绑定，门槛组内不能放命令。装载/打包校验会拒绝缺失、歧义、类型不符及未知图片。

替换界面只读取后端 `battleResult`，不会更改胜负、星级、奖励算法或重复结算凭据；不能在布局中注入JavaScript/HTML。当前获得字段仍为宿主已记录数量，本接口不新增随机掉落。

将领栏保持原版78:98竖卡比例，至少六格，按容器高度缩放并等距排满；超过容器宽度可横向滚动。姓名作为可访问标签和悬停标题，避免额外姓名文字改变原版卡片结构。结算数字使用内置衬线数字字形，门槛数字默认白色；显式节点color/textcolor可以覆盖默认颜色。数字宽度收进父容器，长数值自动适配，避免原XML中115宽的门槛文本越出51宽分档框。

## 制作与验证

复制[示例目录](../../examples/victory-stages/README.md)，合并你已有的extension.json后pack/import/export。已结束的存档仍显示结算，进行中的存档恢复当前阶段；新开局从初始事件开始。测试桌面/手机横竖屏、胜负按钮、关闭后不重复弹出、存读档不重复发奖。修改布局后重新pack，正式发行宿主须包含此接口。
