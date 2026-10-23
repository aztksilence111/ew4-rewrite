# EW4 rewrite

2026-10-24：[0.2.18双端正式下载](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.18-20261024)，包含运输、经济面板、Android 音乐与网格层级修复，继承 0.2.17 更新；Windows x64 Web 与 Android ARM64 APK code21，远端四附件核验通过。[发行说明](rewrite/RELEASE_0.2.18_20261024.md)、[发布记录](rewrite/RELEASE_PROGRESS_0.2.18_20261024.md)。

本仓库保存 rewrite 游戏源码、运行资源、地图制作配置和 Mod 制作指南。
名称约定：**r版**就是 **rewrite版**的简称，后续不再另行注明。
项目仓库：[aztksilence111/ew4-rewrite](https://github.com/aztksilence111/ew4-rewrite)，公开仓库。
项目状态见 [STATUS.md](STATUS.md)，开发说明见 [rewrite/README.md](rewrite/README.md)，
Mod 制作资料见 [rewrite/mod-kit/README.md](rewrite/mod-kit/README.md)。

新会话直接续接：[r版手感与原版表现断点](rewrite/HANDOFF_FEEL_RESTORATION.md)。士气、勋章、附属效果及士气计算优化已接，地图/移动开发侧复核通过；继续原时钟/动作链和原版同局外观对照。[5404干净预览](http://127.0.0.1:5404)，无新APK或发布。

## 当前快照

本轮发行 0.2.14（APK code17）：落实 FB-14–17 的全内置字体、晋升数字、战场控件尺寸及编制数量接口。[发行说明](rewrite/RELEASE_0.2.14_20261023.md)与[发布进度](rewrite/RELEASE_PROGRESS_0.2.14_20261023.md)记录内容和验证步骤，下载见[GitHub Releases](https://github.com/aztksilence111/ew4-rewrite/releases/latest)。实际远端完成依据为本次构建目录的 `remote-audit.json`；以下未打包记录为开发历史，真机验收仍待。

2026-10-22：[四项反馈修复](reverse/FEEDBACK_UNDO_FIX_20261022.md)已修存档国旗、空地撤回入口、保存拆行和说明将领块，新增占城事件/红黄圈不能撤回条件；93新增专项、108配置、20+172浏览器及Web/Android Compile通过。[5395独立预览](http://127.0.0.1:5395)，未打新APK或发布。[当前待办](rewrite/GOALS.md)同步，士气演出、随机掉落、OTF及长期目标另列，真机验收保留。

2026-10-22：[兵模、动画与特效修复](reverse/PRESENTATION_RESTORATION_20261022.md)已恢复完整动作、选中/取消衔接、粒子发射/轨迹/参考点、移动尾迹及动作声音，补接受损炮台烟雾和Mod音效兼容。攻击/反击同步及操作等待保持；招募绝对速度待核定。本轮源码已修，[独立预览](http://127.0.0.1:5381)，未生成新APK或发布。

2026-10-22：0.2.10 发行记录在 0.2.9 基础上新增指挥部三档筛选、60槽及标签模式，按三星到一星逐列上下排列，加入条件击杀台词、物品效果扩展、全局国家/兵模配置和过回合显示开关。后续本地APK已到0.2.13/code15，实际发行范围与本地源码状态分别见STATUS及发行记录。
[开放规则 API 1/2](rewrite/mod-kit/docs/porting/RULE_EXTENSIONS.md)提供动态技能、离线 Lua 和可配置胜负条件；完整作者指南和示例随 Web 包提供。
[0.2.10 发布说明](rewrite/RELEASE_0.2.10_20261022.md)记录验证范围和已知限制；APK 未进行本轮真机运行验收。下载见[GitHub Releases](https://github.com/aztksilence111/ew4-rewrite/releases/latest)。Web 包完整解压后双击 `Start-Web.cmd`，无需安装 .NET。

## 目录

| 路径 | 内容 |
| --- | --- |
| `rewrite/Ew4.Core` | 与显示平台无关的世界、战斗、技能和胜负规则 |
| `rewrite/Ew4.Legacy` | 原格式解析、兼容目录、扩展加载和 Lua 运行时 |
| `rewrite/Ew4.Client` | 两端共享 API、账户、存档和玩法宿主 |
| `rewrite/Ew4.Web` | 网页预览服务及前端 |
| `rewrite/Ew4.Android` | Android 宿主 |
| `rewrite/Ew4.Mods`、`Ew4.ModTool` | 离线导入、校验、打包及命令行工具 |
| `rewrite/Ew4.Persistence` | 存档编解码 |
| `rewrite/Ew4.Verify`、`Ew4.ModChecks` | 规则和 Mod 验证程序 |
| `rewrite/data` | 唯一基础运行资源，Web 与 Android 共用 |
| `rewrite/data/original/eastasia.bin` | 内置东亚地形；同目录map.json保存城市、设施与桥梁模板 |
| `rewrite/mod-kit` | 工具包文档、模板和示例；发行工具由源码构建 |
| `maps` | 地图制作的文本配置和说明 |
| `tools` | 构建、转换、测试及逆向辅助脚本 |
| `reverse` | 逆向结论、维护记录及测试基准 JSON |

## 开发运行

安装 .NET 10 SDK。运行 Web：

```powershell
dotnet run --project rewrite/Ew4.Web -- --root . --urls http://127.0.0.1:5184
```

从仓库根目录执行。若 5184 已被使用，选用其他端口。
普通运行使用 `rewrite/data`，不需要根目录原始 APK 或解包副本。
Android 构建另需 .NET Android workload 和 Android SDK。

```powershell
dotnet run --project rewrite/Ew4.Verify -- . --extension-only
dotnet run --project rewrite/Ew4.Verify -- . --client-only
```

完整历史验证中部分逆向工具仍需本机原包或额外依赖，见各脚本说明。

## 存储边界

`.gitignore` 排除原始 APK/压缩包、历史安装包、工具包构建副本、编译缓存、
本地 Mod/账户/存档、测试截图和解包产物。它们不作为重复资源上传。
Git 中保留一份实际运行资源和独立地图内容。2026-10-22 已按用户要求直接删除
无用旧构建、安装包和测试临时资源，不保留恢复归档；详见
[清理记录](reverse/LOCAL_RESOURCE_CLEANUP_20261022.md)。
不包含签名密钥或登录凭据。

现有原游戏和第三方 Mod 资源的权利归各自权利人所有；本文件不授予这些资源的新授权。
