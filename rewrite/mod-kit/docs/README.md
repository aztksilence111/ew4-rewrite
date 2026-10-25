# Mod制作与排查导航

[国家、势力与阵营地块颜色](porting/MAP_COLORS.md)：国家表/事件颜色沿用现有接口，API 3 新增阵营地图按 ID 配色、默认色池及逐剧本覆盖。

[暂停战局目标栏](porting/PAUSE_OBJECTIVES.md)支持 UI 配置、当前胜负条件及星级区间；暂停菜单居中，左栏避让并纵向滚动。

[任务、成就与局外奖励接口](porting/OBJECTIVES.md)支持将领/装备持有、隐藏关卡解锁、战役/征服胜负、指定地点与占城/击杀累计条件；作者配置列表与原风格UI，R版默认条目为空。

[物品/装备效果接口](porting/ITEM_EFFECTS.md)复用技能 JSON 与外置 Lua，支持装备授予技能、替换消耗品效果和原效果数值覆盖，另有[可打包示例](../examples/item-effects/README.md)。

多阶段更新胜负条件、替换胜利/失败结算界面与控件绑定见[结算扩展指南](porting/VICTORY_MODDING.md)及[三阶段示例](../examples/victory-stages/README.md)。

切换所扮演势力、动态胜负条件及分支结局见[控制接口](porting/SCENARIO_CONTROL.md)与[可选示例](../examples/scenario-control/README.md)。

[剧本事件接口](porting/SCENARIO_EVENTS.md)支持自定义条件→直接结果，或条件→多按钮弹窗→所选结果；含完整条件/结果表、存档语义与[中文示例](../examples/scenario-events/README.md)。

周期事件、军团补给费用及独立双边外交已接入同一接口，见[周期与外交示例](../examples/scenario-recurrence-diplomacy/README.md)。

创建/复活势力及修改名称、国土颜色和旗帜见[势力接口](porting/SCENARIO_FACTIONS.md)及[分支示例](../examples/scenario-factions/README.md)。

这份手册对应当前rewrite实现，不是“所有原包改版均已兼容”的承诺。真实APK的兼容性应以逐包测试结果为准；只通过内置版或构造样本不足以证明某个改版能运行。

已有作品先读[原版Mod移植指南](porting/README.md)。其中列出UI/布局/交互、将领与规则、资源/账户隔离、函数入口及发布验收，并单列尚未实现的规划，避免使用不存在的接口。

[原包入库与rewrite入口对照](porting/ENTRYPOINTS.md)按原资源文件串联转换函数、产物、执行函数，并另列已核实原SO地址和证据文件。

[BTL手工辖区指南](porting/BTL_JURISDICTIONS.md)提供逐格中心配置、导出/写回/撤销命令、JURI v1二进制协议、编号规则和函数入口。无扩展的旧BTL继续自动划分。

[技能与胜负规则API 1/2](porting/RULE_EXTENSIONS.md)提供自定义标签、动态兵种/将领技能、外置Lua、布局、主动技能和VICT条件；API 2新增召唤、传送、范围效果、持续状态和移动力。制作步骤见[修改指南README](porting/README.md)，另有[API 1示例](../examples/rule-extension/README.md)和[API 2六技能示例](../examples/common-skills/README.md)。

## 先选制作方式

| 手里的材料 | 操作 | 不需要做什么 |
| --- | --- | --- |
| 修改后的APK | 主菜单Mod → 填标识/名称 → 导入APK → 等待校验 → 启用 | 不安装该APK、不先到电脑转换 |
| 原assets资源 | 将修改文件按原相对路径压ZIP；根目录放原文件，或统一放assets下 | 不把原PKM直接改名PNG |
| 已转换的rewrite资源 | 自建覆盖文件夹，放client/original/legacy/frontend，pack后导入/导出 | 不把DLL/SO当资源加载 |
| 自定义技能/标签/胜负规则 | 按API 1/2放rewrite/extension.json、定义与Lua，pack导入 | 支持事件及操作内无需重新编译；API 2需新版引擎 |

标识例如`my-history-mod`，1～64位小写字母/数字/点/横线/下划线；不可使用builtin和系统保留名称。同一个项目更新保持ID，多个不相干项目不要复用ID。

## 覆盖包从编辑到分发

1. 在自己的目录，如`D:\mods\my-overlay`，建立`client/ui/`并放入`main.png`；不是直接编辑工具data或已安装哈希目录。
2. 在工具包根目录执行下面命令。pack会在作者目录生成mod.json；verify失败先修资源，不跳过校验。
3. export生成的文件在电脑或Android游戏内导入，再明确启用。导入本身不切换正在玩的内容。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\my-overlay" --id=my-history-mod --name="历史改版" --version=1.0
.\Ew4.ModTool.exe verify "D:\mods\my-overlay"
.\Ew4.ModTool.exe import "D:\mods\my-overlay"
.\Ew4.ModTool.exe list
.\Ew4.ModTool.exe export <list中的key> "D:\mods\my-history-mod-1.0.ew4mod"
```

尖括号只是待替换参数，不要原样输入。输出目标必须不存在，更新用新文件名。CLI默认读取自身data，在自身mods/saves写内容；`--base=... --mods=... --saves=...`可指定工作目录。CLI activate只影响对应mods目录，不会自动操纵另一实例游戏。

## 改动后的核对顺序

先确认启用名称/版本，再核对画面、菜单和新局。规则/剧本变更需验证实际值和行为并保存/重载。图片需核对HD、布局引用和预裁剪图；文本与自带TTF绘制分开，旧位图字库和预渲染label不是现行文字样式入口。大改UI还需验证点击范围、返回路径、遮挡和动画，不能只比截图。

不同Mod及逻辑版本的存档隔离。变更XML/BTL后看到新账户不等于旧档丢失；旧目录仍保留。切换前在游戏保存，不能依赖重载页面保住未保存的内存对局。

## 深入资料

- [配置/字段总表](CONFIG_REFERENCE.md)：哪个文件控制哪一项。
- [源码/函数总表](SOURCE_INDEX.md)：加载、转换、规则和绘制入口。
- [故障定位](TROUBLESHOOTING.md)：报错和检查顺序。
- [真实改版兼容状态](COMPATIBILITY.md)：六个真实APK的通过范围和阻塞，不能用构造测试代替。
- [具体制作例子](../examples/README.md)：头像、将领、征服、章节。
- [安装目录](../mods/README.md)、[存档目录](../saves/README.md)：位置与版本隔离。

原图集、BTL和地形BIN有各自二进制格式。本说明提供实际读取入口，不保证第三方编辑器生成任意变体都合法。当前真实改版测试仍在推进，失败原因应记录到报告再判断能否修复或属于原生代码扩展。
