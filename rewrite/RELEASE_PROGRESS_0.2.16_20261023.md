# 0.2.16 发布进度

2026-10-23 用户授权修复完成后同步Git、构建Windows Web与Android ARM64双端并上传GitHub Release。

当前阶段：正式发布完成，[GitHub Release](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.16-20261023)。包源码 `ca82e6c6fd298ef4789372fe87831800dc6e13d5`，版本0.2.16/code19，沿用既有签名。后续提交仅补发行记录，不改包内运行源码。

源码检查：多人245、辖区228、客户端2763/海军24、结算459/胜利Mod32、隔离机制356、浏览器42项通过，异常/缺图0，Web/Verify构建零警告错误。正式mechanics-only入口的历史指纹问题见[核查报告](../reverse/CAMPAIGN_ALLIANCE_CAPTURE_20261023.md)。

完整核心310933项、Mod106项通过。Web发布目录2744项及逐字节分发检查4497项、独立启动/存读档/资源闭包11249项通过；发行包内胜负/失败显示浏览器42项及字体42项通过，异常/缺图0。两端使用隔离构建和测试档案，不包含玩家存档。

ARM64 APK的当前前端（含胜负界面）、2233份运行资源与本次游戏程序集逐字节核验通过；版本0.2.16/code19、与0.2.15同证书、v2/v3签名和16KiB对齐均通过。

四个附件已上传，draft=false，remote-audit.json的errors=0：

| 附件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| ew4-rewrite-0.2.16-20261023-web-win-x64.zip | 170728114 | 3fad02e26e40f6cc8d60c15304f6417f573d4df34e4817484c305f268eef1132 |
| ew4-rewrite-0.2.16-20261023-arm64.apk | 143722859 | b35b3e64fbb0eb7223ed91d3558da98b2ebfa9709d4e5710afdb27a836bd23ef |

另附release-manifest.json和SHA256SUMS.txt；远端四附件大小与摘要、源码及标签均核验一致。证据目录：build/release-0.2.16-20261023。Android真机尚未运行。
