# 战役入口、国家分支及背景音乐核查与恢复

日期：2026-10-23。用户最初要求只核查，随后明确允许隐藏关初始显示并保留隐藏接口、使用原版国家选择布局、接入音乐；以下实现依照后续授权。

## 原版核查结论

- 原 APK、当前资源和 0.2.14 APK 的 95 份 BTL 逐份 SHA-256 相同，没有关卡文件丢失。反馈的 11 关均为原 `def_battlelist.xml` 中 `hide="1"` 的入口：`campaign1_05`、`campaign1_18`、`campaign2_05`、`campaign2_14`、`campaign3_12`、`campaign3_14`、`campaign4_11`、`campaign4_12`、`campaign5_05`、`campaign5_09`、`campaign6_09`。
- 原隐藏关解锁条件为前置关胜利且 AI 黄圈目标清空；俄土双方分别解锁对应后续隐藏关。
- 同一入口的两个国家分支：重建帝国 `campaign3_08` 普鲁士 / `campaign3_08b` 奥地利；俄土战争 `campaign4_10` 土耳其 / `campaign4_10b` 俄罗斯。R 版已有分支路由，但此前没有使用已导入的原 `form_selcountry` 布局。
- 初始显示全部后共 84 个主战役入口，两个国家分支另可进入。原资源还含未被原列表引用的 `campaign1_02b.btl`，不能据文件数量声称所有战役 BTL 都有菜单入口。
- 原版主菜单循环 `battle1.mp3`；进入战局随机选择 `battle1` 至 `battle4` 循环；失败循环 `defeat_music.mp3`；胜利保持战斗曲。0.2.14 APK 含 26 份 WAV 音效，但没有 MP3/OGG 背景音乐。

原版证据定位：`apk_work/original/assets/layout.xml:138` 的 `form_selcountry`；ARM64 `0x762c0` 读取 `file2`/`countries`，`0x43818` 打开选国页，`0x46358` 绑定国家，`0x45ee4`/`0x45fa4` 选择左右，`0x45d90` 确认分支。音乐对应 `0xaa980`/`0xa34b8` 主菜单、`0x9d624` 随机战斗曲、`0x4be04` 失败曲。

## 已实现行为和接口

内置游戏初始显示原隐藏关，前端与后端入口权限一致；旧解锁记录保留。导入 Mod 默认遵守自己的 XML 隐藏设置。API 2 `extension.json` 可引用 `campaignVisibility` JSON，逐入口或用 `*` 配置 `original`、`visible`、`hidden`，单项优先于通配项。未知 ID、非法模式等会拒绝导入；配置纳入逻辑指纹及存档隔离。

配置示例和完整说明：[campaign-visibility](../rewrite/mod-kit/examples/campaign-visibility/README.md)。恢复原版隐藏可设置 `"visibility": { "*": "original" }`。国家分支共用主入口，不用 `campaign3_08b` 等分支文件 ID 单独配置。

选国页已使用原 `form_selcountry` 布局，左右国旗、名称、选择框和确认按钮依原位置绑定，默认选左侧；两侧均实际进入对应 BTL。国旗使用原 HD 尺寸，选框不截获点击。

五份原 MP3 共 10,156,812 字节已导入，按原菜单/战斗/失败规则播放。单独音乐音量、静音暂停、首次交互恢复播放、应用/网页后台暂停与返回继续已接入。`client/music.json` 可覆盖菜单、随机战斗列表及失败曲；支持 MP3/OGG/WAV 并验证引用文件。旧 APK 导入保留音乐覆盖，读取 `global_data.xml` 的 `BGMusic`。说明：[音乐配置](../rewrite/mod-kit/data/client/music/README.md)。可重复导入工具：`tools/export_music_assets.py`。

## 验证及边界

| 验证 | 结果 |
| --- | --- |
| 战役可见性专项 | 20 项通过 |
| 当时客户端回归 | 2490 项通过 |
| Mod 战役/音乐专项 | 17 项通过，包含打包/导入/导出、权限、旧包及非法引用 |
| 浏览器专项 | 59 项通过，0 脚本错误、0 缺失资源，6 张截图 |
| 浏览器视口 | 1280×720、844×390、390×844 |
| 音频验证 | 5 首均解码为非静音 PCM，实际播放推进、循环、音量、场景和暂停恢复通过 |
| 资源闭包 | 6 个音乐资源记录、2266 个 Android 资源，错误 0 |
| 构建 | Web、Verify、ModChecks 和本地 Android SDK ARM64 Compile 均 0 警告/错误 |
| 静态检查 | JavaScript 语法、Python AST、限定文件空白检查通过 |

验证证据：`build/campaign-music-20261023/browser/checks.json`、同目录截图、`build/campaign-music-20261023/resource-checks.json`。浏览器验证实际启动四个国家分支，并核对 84 个初始入口。

独立预览：[5431](http://127.0.0.1:5431)，使用 `build/campaign-music-20261023/preview-clean-profile`，与测试和玩家档案分开。此阶段没有进行真机测试或自行打包/发布 APK；源码已由并行的 0.2.15 发行任务纳入，发行结果以该任务的进度及远端审计为准。
