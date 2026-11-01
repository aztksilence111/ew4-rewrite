# 0.2.30 发布进度

用户授权生成并上传 Android ARM64／Windows x64 Web 双端包及 Git／GitHub Release。版本0.2.30、Android code33、规则API3。

本批纳入商店金星奖章可用于战役兵种科技升级修复；基于已发布0.2.29，不纳入同期C15策划、目录迁移及研究输入。

构建目录：`build/release-0.2.30-20261101`。包源码及标签：`4b868dcffbac4e1ee937065f071ac33121448b77`。

## 发布完成

- 已公开：[v0.2.30-20261101](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.30-20261101)。Git main已同步包源码；上传草稿四附件并核远端大小、SHA-256后公开，main／标签／附件审计错误0。校验清单及manifest同包发布。
- APK：190485550字节，SHA-256 `161dad5f301ebef510bb1f13d4b2a0612ba65c6bbf0a9502af45c12dab04115a`。
- Windows ZIP：216522720字节，SHA-256 `50fe137bcdd1eb5db01bfc79781d2a7425815e09556ed22987f87c037949cf20`。

## 验证

- Web自包含Release／Android ARM64 Release构建通过；原包名和证书、10个ARM64库及16KiB对齐保持。首轮构建被检出带x64，已拒绝该包并在独立目录显式限定`RuntimeIdentifiers`及`RuntimeIdentifier`为ARM64重建，仅最终包上传。
- 科技专项2726、客户端64492及海军24、导出后端35项通过。实际最终APK提取程序集再执行科技2726、导出35及包资源22项通过；一次性PE架构规范化仅用于桌面CLR执行，不等于Android真机运行。
- 正式Windows包浏览器源码／兼容前端科技27项、双端桥接及Web真实导出57项通过，覆盖桌面、手机横屏和竖屏；脚本／缺图错误0，截图已目检。科技升级使用真实购买奖章，实际扣费与刷新持久化正确；导出保留原始字节及账户／存档／备份，排除安装内容和缓存，主菜单与暂停设定入口均回归。
- Windows内容3370项、最终包3760项、ZIP3598文件逐字节校验通过，前端兼容54文件通过。详细摘要见`validation-summary.json`、`verification.json`、`android-verification.json`、`web-verification.json`及`extracted-assemblies.json`。
- 未做Android真机安装验收。

## 本地清理

核绝对路径、重解析点、活动进程及Git后清26目标16084文件4524309271字节，剩余0／错误0、Git状态保持。包含本轮测试账户／存档／截图／缓存、首轮多ABI包、最终APK编译副本及其他重复bin/obj、旧金星测试预览、三批外0.2.27本地双端包及运行目录。停止测试进程后DLL短暂占用导致一次删除中断，确认进程已退出、锁释放后按原干跑计划续接；收据保留原计划总量及剩余项。

本地仅留0.2.30／29／28三批六包且摘要正确；297份受保护资料摘要变化0，原56167／68／69／71／72／73／74及5184八服务HTTP200。保留活动运行目录、玩家资料、已安装Mod、签名、原始输入及同期C15策划／迁移／旗帜改动；旧57942独立金星预览已由正式版覆盖后清理。先保存cleanup／post-cleanup-verification，再更新维护说明和短锚点，最后WORK_LOG。远端Release与Git历史不清理。
