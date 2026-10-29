# 指挥部自定义界面

包含本接口的宿主支持 API 3 `headquartersPresentation`。Android 与 Windows Web 共用，未配置时沿用默认界面。它是完整布局和命令绑定，不是固定工具栏上的几个显示开关。

在包内 `rewrite/extension.json` 注册：

```json
{
  "schema": 1,
  "apiVersion": 3,
  "headquartersPresentation": "rewrite/headquarters.json"
}
```

目标文件为 `{"schema":1,"screens":{...}}`。[最小可用示例](../../examples/headquarters-layout/README.md)包含无筛选的名册、独立管理页和学院布局。需要保留原扩展的其他入口，合并注册字段；不要用示例入口覆盖已有技能、地图和规则配置。

## 页面与布局

`screens.roster` 对应局外名册，`academy` 对应军事学院，`deployment` 对应对局任命。每页均可省略，省略的内置页使用原界面。其他键是作者自定义页，可通过 `page` 命令打开。最多32页；页名和节点ID使用英文字母开头的字母、数字、下划线或连字符，长度最多64。

每个页面提供：

| 字段 | 含义 |
| --- | --- |
| `layout` | 完整布局树；根 `attributes.w/h` 为80～2048的逻辑尺寸，宿主等比缩放居中 |
| `bindings` | 数据名到节点路径的映射，可省略所有非必需数据；未绑定数据不会自动插入 |
| `actions` | 按钮节点路径到命令对象的映射；数量、位置、外观、文字完全由作者节点定义 |
| `roster` | 可选名册排列，字段见下文 |
| `initialSection` | 可选0/1/2，首次进入名册的星区；省略为全部，与招募星级区相同 |
| `background` | 页面底色，默认 `#ebe6e1`，支持 `#RRGGBB` / `#RRGGBBAA` |
| `backgroundImage` | 可选原图集PNG名或包内 `rewrite/*.png`，铺满作者画布 |

节点格式沿用原生布局：`{"attributes":{"id":"shop","type":"button",...},"children":[]}`。所有 attributes 值是字符串，children 可省略。最多256节点、12层。支持 `group`、`groupbox`、`image`、`text`、`listbox`、`grid`、`button`、`tmp_button`、`tmp_intitle`。本界面原点为画布左上角 `(0,0)`，没有原版弹窗的27像素标题偏移。

常用属性：`x/y/w/h`，`scale` 或 `hscale/vscale`，`text/string/title`、`font/size`、`color/textcolor`（RGBA四个0～255整数，以逗号分隔），`align`（文字left/center/right或节点hmiddle）、`valign`（middle）、`breakwords`（true启用换行）；图片用 `name`，按钮用 `frm1/frm2`，边框用 `frame`。还支持 `background`、`opacity`（0～1）、`rotation`（角度）、`radius`（逻辑像素）。`tmp_intitle` 沿用原生22像素高度。

图片引用可以直接指向包内 `rewrite/images/custom.png`，无需修改图集登记。每图最大4MiB、边长4096、像素总数8388608，引用图片合计最大16MiB。`button` 使用整张图片，`tmp_button` 使用可伸缩原生边框；图片尺寸建议明确填写。图片节点不拦截点击。

短ID必须唯一，嵌套节点可用 `header/back` 完整路径。数据绑定与命令绑定不得共用或互相包含节点；名册容器会被动态填充，不要把其他已绑定节点放入其中。

## 数据绑定

| 数据名 | 内容及节点类型 |
| --- | --- |
| `roster` | 名册/学院候选/任命列表；绑定到group/groupbox/listbox/grid，可省略 |
| `title` | 当前内置页面标题 |
| `medals` / `badges` | 当前账户勋章/徽章 |
| `count` / `capacity` | 当前名册过滤结果数量/星区容量，任命时使用总容量 |
| `refreshRemaining` | 当前学院星区剩余刷新次数，无限为∞ |
| `offerName` / `medalPrice` / `badgePrice` | 已选学院候选的姓名及两种价格，未选为空 |

除roster外均绑定到text/tmp_intitle。作者可自行放置相邻标签、货币图标及装饰，未定义的按钮不会从默认界面补进来。隐藏筛选无需额外开关：布局内不放filter命令，首次默认显示全部将领。

`roster` 排列字段：`rows`（1～16，默认2）、`cardScale`（0.25～3，默认1）、`gap`（0～64，默认6）、`showEmpty`、`showInfo`、`showPromotion`（后三者默认true）。将领卡片沿用现有头像、姓名、军衔/爵位和状态，卡片基准78×98；按照作者容器尺寸滚动。标签排序和真实容量保持原规则，隐藏空槽不会改变招募上限。

## 按钮命令

例如 `"actions":{"myShop":{"command":"shop"},"more":{"command":"page","page":"management"}}`。可以添加多个执行同一命令的按钮。新增按钮必须同时提供布局节点及动作；减少按钮时删除二者。

| command | 行为 |
| --- | --- |
| `back` | 返回；编辑标签时先取消编辑，自定义页回名册，学院回名册，任命回战场，名册回主菜单 |
| `main` / `roster` | 回主菜单/局外名册 |
| `academy` | 打开学院，`section` 为0/1/2，默认0 |
| `filter` | 切换名册过滤，`section` 为0/1/2，省略为全部 |
| `shop` / `princesses` | 打开既有商店/公主流程 |
| `tags` | 开始/确认标签编辑；可用 `editingText` 指定编辑时的按钮文案，如“确认” |
| `tasks` / `achievements` | 打开已有任务/成就面板 |
| `refresh` | 刷新当前学院星区，遵守剩余次数及忙碌状态 |
| `recruitMedals` / `recruitBadges` | 使用对应货币招募已选候选，受真实价格、余额、容量与版本校验约束 |
| `info` | 查看最近选中的将领详情 |
| `page` | 打开 `page` 指定的自定义页；内置页使用对应命令进入 |

每个自定义页面至少提供一个 `back` 按钮，防止触屏进入后无处返回；它的图形、大小和位置由作者决定。`deployment` 只允许back/info，实际任命沿用卡片内的任命操作，避免将局外流程带入战局。配置只改变表现和可达入口，新增玩法仍需相应规则接口；不执行HTML、JavaScript或任意RPC。

## 验证

修改后重新pack、verify、导入并启用；已安装的哈希目录不要手改。配置无效、命令未知、资源缺失、绑定歧义会在加载时拒绝。现有 `presentation.headquarters` 的工具栏尺寸配置继续只作用于默认界面，自定义页面以本布局树为准。

```powershell
dotnet run --project rewrite/Ew4.Verify -- <仓库根目录> --headquarters-presentation-only
node tools/verify_headquarters_presentation.cjs <已启用示例的独立宿主URL> <临时结果目录>
```

检查桌面、手机横竖屏、完整返回链、标签取消/确认、商店、招募扣款和切回无配置Mod后的默认界面。新接口需要更新宿主，既有旧APK不会自动支持。
