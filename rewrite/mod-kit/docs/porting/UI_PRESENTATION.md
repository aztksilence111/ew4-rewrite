# 通用 UI 换肤与原版布局导入

#Mod #UI #移植

作者用图片和声明式 JSON 调整已有界面，C# 游戏规则、按钮命令、无障碍名称和存档逻辑继续由宿主维护。战场仍由 Pixi 绘制，菜单与 HUD 沿用 DOM；不需要移植规则到 JS。最小源码见[ui-skin](../../examples/ui-skin/README.md)。

## 文件及覆盖顺序

可选文件为 `rewrite/ui-presentation.json`，根字段 `schema: 1`、`screens`、`native`。原版 APK 可带 `assets/rewrite/ui-presentation.json`。缺省保持宿主界面；导入原 APK 的布局与贴图先应用，JSON 在其后覆盖。停用 Mod 后还原。此文件不计入规则指纹，纯换肤不新建规则存档空间；同时改规则的 Mod 仍按原机制隔离。

图片引用可填已有图集的精灵名，如 `btn_common_blue.png`，或 `rewrite/` 下 PNG 的完整相对路径。不能指定网络地址、CSS、JS、任意选择器或新命令。每张 PNG 最多4MiB、边长4096，配置引用的独立图片合计16MiB，JSON最多256KiB。资源变更使用已有内容版本缓存机制。

## 屏幕与控件

`screens.<屏幕>.controls.<控件>` 的可用目标如下。目标就是已有元素，不复制按钮或重复绑定动作。

|屏幕|可配置控件|
|---|---|
|main|entries（整组菜单）、campaign、conquest、tutorial、headquarters、settings、title|
|scenario|list、pagination、load、title|
|country|confirm|
|campaign|confirm、intro|
|battle|pause、endTurn、undo、nextUnit、skipAi、aiCountry、resources、capacity、mapMode|
|pause|save、settings、restart、exit、turn|
|settings|仅 panel 背景与颜色|

每个控件允许 `x`／`right`、`y`／`bottom`、`w`、`h`、`image`、`pressedImage`、`color`、`background`、`fontSize`。坐标与尺寸须为数字，同一轴不能同时指定两端；颜色为 `#RRGGBB` 或 `#RRGGBBAA`。指定几何字段后，该控件以绝对定位参加布局；未指定字段沿用原有样式，不自动清空整套布局。

screens 的 color／fontSize 作用于 CSS 文字；原字库画布、标签图片不会因此重新排字。需要改变原字库排字请用 native 补丁或专用接口，换成美术文字则用 image。

菜单／弹窗数值为原布局逻辑像素，跟随宿主缩放；战场数值乘 HUD 缩放并解除该目标原来的整体 transform。坐标相对**已有定位父容器**，例如主菜单 entries 相对主菜单、campaign 按钮相对 entries，暂停按钮相对暂停面板。原菜单仍有响应式扩展，贴边应使用 right／bottom，不能把568当所有屏幕的物理宽度。不要同时移动整组和组内控件来表达同一位移。

`panel` 只允许 `image`、`background`、`color`，保留屏幕自适应尺寸。每屏可加最多32个 `decorations`，字段同控件；装饰没有事件、不可获取焦点，不拦截点击。按钮图片按控件尺寸拉伸，作者应准备匹配长宽比的图；复杂九宫格继续使用原版弹窗的 `tmp_button` 或 HQ 节点。

不提供隐藏／禁用／更换命令字段。关闭、返回、Mod 管理及存档导出入口不接受几何覆盖；设定页存档管理区保持由宿主布局。选国与战役侧栏的行树仍由宿主管理：选中前后同尺寸、仅横向位移、底纹覆盖右缘。HQ 整页替换、剧情、战役目标、外交等专用界面继续使用各自接口，见[移植索引](README.md)。这不是可任意新增业务页面的通用编辑器。

## 现有原版弹窗

`native.<form ID>.<控件路径>` 为稀疏属性补丁，例如：

```json
{"schema":1,"native":{"form_pause":{"btn_save":{"x":42,"image":"btn_common_blue.png"}}}}
```

路径用原节点 ID，以 `/` 分隔父子。必须完整、唯一且已存在；原 XML 中重复 ID 的装饰不可用模糊路径修改。支持 x/y/w/h、image、pressedImage、fontSize、color、background；不支持 right/bottom，最多256条。图片映射到 image 的 name 或按钮的 frm1，pressedImage 映射 frm2；fontSize 映射原文字 size。补丁修改运行时 manifest 的副本，不重写 Mod 源文件。

`native` 作用于采用该原布局定义的界面；例如暂停主菜单当前是 DOM，其几何和皮肤应使用 `screens.pause`。同一套旧原版布局只维护图片时，可同时配 screens 与 native，不应假定给任意 form 打补丁就能更换所有现代界面。

## 原 APK 的六套布局

导入器现在识别原包所有 `layout*.xml`。每个 form 优先采用实际修改的568h版本，其次标准 `layout.xml`、640h及其余设备变体。若高优先级变体没有改该 form，会继续寻找下一个变体。以附带的原版设备布局为对照，只把作者修改的属性合并到568逻辑画布，位置与尺寸传递差值，其他属性传递修改值，保留未改 form。`manifest.layoutSources` 记录每个 form 的来源，方便核对。

主菜单按钮和暂停按钮的常用几何／图片差异自动桥接现有 DOM；战场旧按钮只桥接图片，保留安全边距和触控尺寸，需要改位置用 `screens.battle`。不执行 APK 的 SO／DEX，不自动生成缺失控件的业务逻辑，也不保证仅存在于某个设备变体的新节点自动适配所有宽高比。复杂改版请在导入后用此配置与 HQ 接口校准。

一战 Mod 只改标准／640h布局时，不再被未修改的568h遮蔽。大鏖战六套均改时优先568h。旧版已经安装的包保留其原字节，须重新导入才能得到新布局转换结果；本次未自动替换玩家安装。

## 检查

先运行作者包校验，再在桌面、手机横屏与竖屏检查按钮触达、选中行等宽等高及右缘覆盖、暂停设定和双端导出入口。宿主回归源码为 `Ew4.ModChecks/UiPresentationChecks.cs` 与 `tools/verify_ui_presentation.cjs`。本接口属于共享 Web/Android 前端；源码和浏览器验证不等于 Android 真机验收。
