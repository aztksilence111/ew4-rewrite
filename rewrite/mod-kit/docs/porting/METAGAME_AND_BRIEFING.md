# 局外配置与战役说明窗口

适用于包含本增补的 rewrite 宿主，电脑和 Android 共用。新增 API 2 入口 `metagame` 和 `stageIntroPresentation`，均指向包内 `rewrite/*.json`。实际胜负沿用[既有规则接口](VICTORY_MODDING.md)。[可打包示例](../../examples/feedback-config/README.md)同时修改界面和实际任务。

## 局外配置

`metagame` 文件为对象，`schema:1`，其余字段可省略：

| 字段 | 默认及含义 |
| --- | --- |
| `initialMedals` / `initialBadges` | 各20000000，非负整数，仅创建此Mod新账户时应用，不重置旧余额 |
| `shopSlots` | 省略/null保持五个消耗品位、两个装备位；数组完整替换商品位；空数组为空商店 |
| `refresh` | 空字典；未配置目标或limit:null为无限次免费刷新 |

商品位字段：`slot` 为0～13唯一格号；`pool` 为consumable（默认）/equipment/any；`items` 可省略或为有效物品ID的非空候选集；`refresh` 默认true；`quantity` 默认1，非负整数或null（不限库存）。显式候选须符合pool类型。永久装备排除已拥有及当轮重复物品，唯一性购买规则继续生效。默认原版装备位仍为不限库存。

`refresh:false` 保留物品和已消耗库存；true重新抽取并恢复配置数量。首次打开免费生成，再次打开保留库存。每次购买扣1件，0不可买。地图内商店不支持此刷新接口。

refresh目标键为 `shop`、`academy.0`、`academy.1`、`academy.2`，三个学院分别计数。

| 策略字段 | 语义 |
| --- | --- |
| `limit` | 非负整数上限；0禁止手动刷新；null无限制 |
| `initial` | 初始次数，默认limit，范围0～limit |
| `recovery.intervalSeconds` | 每多少秒恢复一次，正整数，支持离线时间 |
| `recovery.victories` | 每新增多少条Victory结算凭据恢复一次，正整数 |
| `recovery.amount` | 每次恢复数量，默认1，正整数 |

两个恢复条件必须二选一；省略recovery表示不恢复。达到上限后不储存多余恢复进度。首次生成商品/学院候选不扣次数。UTC时钟倒退不恢复；这是本地机制，调整时钟不具有联网防作弊保障。胜利计数使用持久化、去重结算凭据，重复加载同一结局不重复恢复。单人及本地多人按实际Victory记录计数。

次数随账户保存，过期版本/失败请求不扣次。仍按Mod ID及逻辑版本隔离；切回原版资产保持不变。配置改变纳入逻辑指纹，遵循现有账户/存档版本隔离。

## 说明窗口与任务目标

`stageIntroPresentation` 文件为剧本ID到配置的字典。精确实际ID优先，再匹配 `"*"`；分支按实际ID配置。未匹配使用原版form_stageintro。默认新开及从菜单读档时显示，关闭后继续待处理对白，不消耗对白/回合；教程、征服及已结束战局不弹出。

| 字段 | 含义 |
| --- | --- |
| `title` / `description` | 可选文字或字符串表键，默认剧本名/说明 |
| `commander` | 可选头像名称（无.png）；空字符串隐藏，省略用原指挥官 |
| `content` | 说明区追加的有序内容块，默认空数组，最多64项；支持文字、图片和将领头像 |
| `objectives` | 目标文字/字符串键，省略时读取当前生效规则的descriptionKey，包括存档动态阶段 |
| `victoryText` / `bestVictoryText` | 替换两项回合数字的展示文字；原布局隐藏该项“回合”后缀；不修改评分 |
| `layout` | 原生布局树，格式同victoryPresentation，最多256节点/12层，支持tmp_commander和原图集 |
| `bindings` | 完整控件映射；省略用默认映射，显式填写可省略/null数据项 |

默认映射：description→text_desc，commander→tcmder，minimumRatingRound→text_victory，highestRatingRound→text_bestvic。另支持objectives/victoryLabel/bestVictoryLabel（text）及close（button/tmp_button）。头像绑定tmp_commander/group/groupbox。没有objectives绑定时，目标追加到说明区。自定义布局始终保留宿主标题栏关闭按钮，close是可选额外按钮。

布局节点格式 `{"attributes":{"id":"...","type":"text","x":"0","y":"30","w":"120","h":"24"},"children":[]}`，attributes值全部为字符串。顶层y从27开始，为宿主标题栏留位。窗口宽高80～2048；字体、颜色、图片及嵌套绑定格式同[结算界面](VICTORY_MODDING.md)。绑定不可歧义、重复或覆盖其他绑定的父控件。新增图像须登记图集并附PNG，不能注入HTML/JavaScript。

建议省略layout沿用原版结构，保留左侧头像、说明框和两栏评分目标。`content`在说明文字后按序追加内容，实际任务说明接在最后；说明框大小固定，超出时显示上下滚动条，支持鼠标滚轮和触摸滑动。文字沿用原版字体并分块绘制，避免长文形成过大的单张纹理。

```json
"content": [
  { "type": "text", "text": "增援指挥官" },
  { "type": "commander", "commander": "Napoleon", "text": "拿破仑少校将协助作战。", "width": 78 },
  { "type": "image", "image": "rewrite/images/briefing.png", "text": "战役地图参考", "width": 240 }
]
```

text块的text为文字/字符串键；image块的image为已有图集PNG名称或包内rewrite/*.png路径，text为可选说明；commander块使用不含.png的头像名，text省略时显示将领名。width为可选逻辑宽度，图片默认填满说明区、头像默认78，均按比例缩小到框内。每项文字最多16384字，总计65536字；每张PNG最多4MiB、边长4096、8388608像素，所有唯一引用图片总计16MiB。自定义layout的内容插入点仍由description绑定指定，不接受HTML/脚本或外部图片URL。

原数字来自实际所选BTL的两项评分回合阈值，评分继续使用BTL值。修改真实目标使用victoryRules、BTL VICT或scenarioEvents的victory_rules；只改文字不改变判定。条件支持占领/丢失格子、消灭部队/国家、回合、资源、组合和Lua，见[RULE_EXTENSIONS](RULE_EXTENSIONS.md)。示例同时注册规则和descriptionKey，保留原版布局并插入图片和头像，实际坚守至第3回合才胜利。

打包/导入会校验新配置及引用，拒绝未知字段、负数/小数/字符串数量、无效物品/剧本/图片、缺失/歧义控件。旧宿主需升级后才识别新增入口。
