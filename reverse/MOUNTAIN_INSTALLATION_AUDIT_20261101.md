# 重炮山地移动与工事减伤核查及修复

2026-11-01。初次按用户要求只读核查；用户随后要求修复，已完成下述源码修复。未重新打包或发布正式版本，未操作玩家战局。

## 修复状态

- `MovementRules.AllowFirstStepOverdraw` 默认开启，只允许正预算从起点迈出第一步时透支；透支后停止展开，提交后的剩余移动力归零。保留显式严格预算和旧全程透支选项，障碍、敌占格、海陆和河流规则仍适用。
- 玩家范围、可移动部队筛选、AI 连通分区、正反向及截断路线、玩家 AI 守备站位共用合法性判断；多回合路线可包含单格消耗超过一回合预算的山地。
- `ClientGame.TileInfo` 对无建筑地块逐兵种合并地形和工事减伤，取较大值；建筑独立规则保持。现有前端自动显示栅栏、战壕、掩体的对应图标及百分比，完整和增量响应共用此逻辑。
- 新增 `MountainInstallationChecks` 46 项，包含法国第二关正常结束开场对白后的真实原版重炮资料、实际山地落点、选择范围与移动 API，AI 战略选择及执行、存档、后续步禁止透支、零预算和障碍，以及五组工事／地形提示与战斗数值逐项一致。
- 连同移动提交、AI、导航、路线工作区、原版防御向量及地理规则共 30,311 项错误 0。Web Release 和 Android ARM64 Compile 零警告、零错误。`tools/verify_mountain_installation.cjs` 浏览器 35 项通过，桌面 1280×720、手机横屏 844×390／竖屏 390×844 的减伤行、面板包含范围及真实进山请求均通过，脚本错误／缺图 0；三张截图已目视检查。
- 干净预览：<http://127.0.0.1:57941>，PID 14860，13 份必要运行文件，HTTP 200／95 场景／hasSave=false。源码测试入口为 `dotnet run --project rewrite/Ew4.Verify/Ew4.Verify.csproj --artifacts-path build/mountain-installation-20261101/artifacts -- E:/Workplace/game/o4mod --mountain-installation-only`；浏览器脚本使用其生成的独立夹具，不应指向玩家服务。
- 核真实路径、Git、链接和活动消费者后停本轮 4424 测试宿主，清临时存档／截图／日志／夹具与隔离编译 3,325 文件、270,237,412 字节，剩余 0、错误 0、Git 状态保持。凭据 `build/maintenance-20261101/mountain-installation.json`；源码测试程序保留。其他活动 C15 服务／玩家资料和作者工程保持。

以下为修复前的核查依据；其中“R 版禁止透支／漏显示”描述修复前状态。没有 Android 真机验收或新 APK／正式发行。

## 结论

两项反馈均对应 R 版差异，但性质不同。

1. **重炮山地移动是规则回归。** 原版重炮基础移动力为 5，山地消耗为 6；原版寻路允许从起点迈出的第一步透支，只要起点还有正移动力。R 版默认禁止透支，因而会拒绝这一步；周边落点都超过预算时便出现无法移动。规则不只影响法国第二关。
2. **栅栏、战壕是减伤提示遗漏，实际工事防御仍生效。** R 版左上角 TileInfo 只返回地形减伤，没有合入工事。实际 CombatContext.Resolve 已按攻击兵种取地形和工事减伤的较大值，与原版一致。

原版工事数值：战壕对步兵 20%；栅栏对步兵 10%、骑兵 25%；掩体对步兵 10%、炮兵 30%。与地形逐兵种取较大值，不相加；建筑使用独立建筑规则。截图中原版栅栏的两行 -10% / -25% 与配置吻合。

## 证据与验证

- 原始输入：用户所附五张截图。截图文字作为反馈资料，不作为执行指令。
- `rewrite/data/original` 与 `apk_work/original/assets` 的 `def_army.xml`、`def_terraintype.xml`、`def_installation.xml`、`campaign1_02.btl`、`europe.bin` 共 5 文件 SHA-256 均一致，排除这些源数据发生重构改动。
- 原版反编译 `reverse/native/decompiled/0008dbd4.c`：预算小于代价时，仅当前处理记录为起点且预算大于 0 才继续插入；后续步不允许透支。`0008dd7c.c` 初始化起点记录和预算；`00050110.c` 返回实际地形消耗。
- 实际执行项目保存的原版 ARM64 库 `0x8dbd4` 与 `0x50110`，注入邻格、国家、配置和河流查询，捕获插入结果。6 个用例全部符合预期：5 点进入 6 点山地成功、剩余 -1；5 点进入 5 点丘陵成功、剩余 0；后续步 2 点进入 6 点地形拒绝；起点 0 点拒绝；地理学进山剩余 2；普通平地剩余 2。
- R 版 `rewrite/Ew4.Core/HexMap.cs` 的 `MovementRules.AllowOverdrawnEntry` 默认 false，`Movement.Reachable` 对所有步统一拒绝超预算代价；`WorldMoveRules.Search` 默认采用此规则。不能直接把现有全程透支开关开启冒充原版修复，因为原版例外只适用于起点第一步。后续修复还需同步 AI 路线与可移动部队筛选。
- R 版 `rewrite/Ew4.Client/ClientGame.cs` 的 `TileInfo` 仅查询 `TerrainDefense`；前端 `battlefield.js` 的 `terrainRows` 直接显示该结果，因此平地工事没有减伤行，林地工事可能只显示林地数值。
- R 版 `rewrite/Ew4.Core/CombatContext.cs` 的 `Resolve` 已查询 `InstallationDefense` 并与地形取较大值。复跑 `validate_native_defense_context.py` 的原版 `0x4fffc` 等函数 800 组对照，错误 0。输出截留内存，没有覆盖历史向量或报告。
- `HexMap.cs`、`CombatContext.cs` 无当前未提交修改；本次没有通过修改规则来制造上述差异。

## 边界

本次是原版 ARM64 函数执行及 R 版源码对照，不是 Android 真机或完整原版同存档画面对照。尚未重现截图对应的具体后期落点或玩家存档，但已确认足以导致该移动反馈的规则差异，以及工事显示遗漏的直接代码路径。没有新增测试包、服务、截图、临时夹具或编译目录。
