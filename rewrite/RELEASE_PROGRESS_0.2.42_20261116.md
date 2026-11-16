# 0.2.42 已正式发布

2026-11-16：[正式下载](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.42-20261116)。Android通用APK（ARM64＋x64，code45）、Windows x64 Web、作者工具包1.1.7及两份清单均已上传，五远端附件大小与SHA-256一致，错误0。源码／标签为76f2d0ecb966f5f4555e50769fd2414e6762d944，分支codex/release-0.2.42，规则API3；原工作区和同期修改保持。

新增通用Mod接口：暂停阶段目标、剧情跳过确认及背景呈现、AI区域协防、事件增援objective红圈、事件胜利规则与战役评分，附作者说明与示例。保留0.2.41的UI换肤、分类尺寸、音频、整编滚动和海上工事能力，不打包民国Mod或玩家资料。主菜单与暂停设定保留存档导出。

核心326494、阶段目标12535、剧情78、增援87、剧情确认55、UI107、Windows导出49／存档管理48、Web目录3418／ZIP3663、作者九流程6338通过。Android两ABI各资源22／导出35／导入41通过；重签前后2904个原载荷条目逐字节一致，最终封包3805项、20原生库ELF及ZIP16KiB对齐通过。原证书连续，可覆盖旧版；无本轮Android设备实测，程序集探针在桌面CLR执行。

签名已找回，先前“原私钥缺失”判断已解除。原文件自2026-10-18保留在Codex应用隔离目录C:/Users/WIN11/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local/Xamarin/Mono for Android/debug.keystore，不是用户移动或删除。指纹82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1与旧版完全相同。已另存稳定备份于.codex/keys/ew4-release-debug.keystore并核对文件摘要；原文件不改，不把私钥提交仓库。后续发行显式指定稳定备份，避免不同执行环境的默认LocalApplicationData差异。定位规则保存于.codex/docs/EW4_ANDROID_SIGNING.md。

本地成果在build/release-0.2.42-20261116，保留assets、可用web／author目录、签名及远端审计、验证摘要和清理凭据；源检出在build/release-source-0.2.42。Android成品SHA-256为59602fc9d770280f8571df49db95b7c50ceaacc2bac8d0b38222ca202ec0356d。先前草稿两份清单已替换，最终五附件核对后正式发布，无不兼容签名包上传。

先存成果和收据，首阶段清测试／缓存／重复编译23目标19153文件3864900332字节，仅停56255、解除两依赖链接；30保护文件0变。正式发布后清旧0.2.39双端、无消费者Web及作者不可变输出、待签名中间包和重签辅助产物221目标6799文件1464932063字节，残留0／34保护文件0变／主仓库与隔离Git保持。两阶段共244目标25952文件5329832395字节；正式仅留0.2.42／41／40三批九包且摘要0错。旧作者project／exports／_work、原签名及备份、玩家资料／已装Mod／活动服务和0.2.36玩家数据依赖运行目录保持。此前三个根build辅助脚本删除曾被自动审批blocked by policy拒绝，仍保留，不绕过重试。原始误判及其草稿收据作为必要发行凭据保留。清理后更新短锚点，WORK_LOG最后。
