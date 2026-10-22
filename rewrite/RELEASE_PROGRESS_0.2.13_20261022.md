# 0.2.13 发布进度

用户于 2026-10-22 授权同步 Git、构建 Web/APK 并上传 Release。

**已完成，2026-10-22 23:30（北京时间）。** [正式 Release](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.13-20261022)，包源码及标签指向 `6f937fe50afa0bd2b5c0ecb47845c5b735f71249`。四个附件远端大小/SHA-256及标签均通过，`remote-audit.json` errors=0；后续仓库提交仅记录发布结果和修正发布工具，不改包内游戏源码。

| 最终阶段 | 结果 |
| --- | --- |
| Git源码同步 | 完成，远端 API 核对提交一致 |
| Web ZIP | 154456920 字节，独立运行及存读档通过 |
| ARM64 APK | 125674540 字节，0.2.13/code16，旧证书 v2/v3、16KiB 对齐通过 |
| 最终浏览器检查 | 180 项通过，0 异常、0 缺图，桌面/横屏/竖屏截图已核 |
| 上传 | Web、APK、release-manifest.json、SHA256SUMS.txt 共4附件，全部已发布 |
| 远端审计 | 0 错误；精确摘要见随 Release 提供的 SHA256SUMS.txt |

过程异常已解决并留证：隔离表现检查补齐输出目录与历史夹具；旗帜对照固定至上版 `99e3c2b`，避免提交后的 HEAD 被误作旧版；百度网盘在本地附件目录生成隐藏 `.baiduyun.uploading.cfg`，发布工具改按 manifest 明确清单枚举，不操作该同步文件。未运行真机，不能据此关闭设备验收。

本次证据目录：`build/release-0.2.13-20261022/`。
实时阶段状态：该目录 `progress.json`，包含更新时间、成功步骤和失败信息；构建日志单独保存。

源码冻结前已完成：Web 与 APK 完整构建，核心 310933、客户端 2239+24、Mod 106、前端 13731、表现 5936+840、撤回 93、配置 108、勋章 27732；Web 精确资源 4384、作者资料 2633、独立启动/存读档 10967 全部通过。APK 同旧证书、v2/v3、16KiB 对齐及 code16 已确认。浏览器、压缩和远端结果继续更新至上述实时证据，不能从本段推定已经上传。

| 阶段 | 完成依据 |
| --- | --- |
| 远端核对 | `remote-before.json`，远端 main 必须等于发布前本地 HEAD |
| 回归检查 | `core-checks.log`、`client-checks.log`、`mod-checks.log`、前端报告 |
| Web 构建 | `web-build.log`、`web-verification.json`、`web-publish-verification.json`、`standalone-verification.json` |
| APK 构建 | `android-build.log`、`android-verification.json`、`android-signature-verification.json` |
| 源码提交/同步 | `source-sync.log` 及远端精确提交核对 |
| 压缩与摘要 | `assets/release-manifest.json`、`assets/SHA256SUMS.txt` |
| 上传 | `release-upload.log`；先上传到草稿，附件均验证后才正式发布 |
| 远端复核 | `remote-audit.json`，源码、标签、附件数量、大小和摘要一致且 errors=0 |

Git HTTPS fetch 本轮连接重置，采用仓库已有 GitHub API 同步方式；不以本地 remote-tracking ref 代替远端查询。失败保留现场及错误，不能把 Compile、构建或创建草稿写作已发布。历史记录中的“未打包/未发布”仅描述当时阶段，本次结果以上述实时证据为准。真机测试不在本次完成范围。
