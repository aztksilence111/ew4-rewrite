# 当前工作区全部更新本地测试APK

2026-10-23。用户要求打一个测试APK，包含其他会话的源码更新，不上传Git或Release。本次直接构建当前共享工作区，保留所有未提交及新增源码，没有为打包创建提交、重置、切换分支或发布。

## 产物

- 文件：`build/all-updates-test-apk-20261023/ew4-rewrite-0.2.16-all-updates-test-20261023-arm64.apk`
- 大小：144151959字节，137.5 MiB。
- SHA-256：`adfb4ce22bb02fcb15fd5e527f5459d723a2664370dd70607579c1c28ded691a`。
- ApplicationId：`org.ew4.reconstruction`；版本0.2.16/code19，未改项目版本号；仅ARM64。
- 沿用原签名，证书SHA-256：`82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1`，与此前正式版/兼容测试包一致。

包含构建时当前工作区已落盘的更新：最新完整屏幕视口与贴边修正、按钮及阴影、暂停遮罩、Chrome70兼容、Pixi与性能优化、自动快进、将领购买上限及接口、伤害数字等；本机Web默认静音的同期改动也保留，Android继续原音量规则。未将尚未落盘的其他会话计划当作已实现功能。

## 构建与核验

- 隔离输出：`build/all-updates-test-apk-20261023/artifacts`。Android Release `SignAndroidPackage`完整构建通过，0警告/0错误，耗时约43秒。
- 仅指定`RuntimeIdentifier=android-arm64`及`RuntimeIdentifiers=android-arm64`，同时使用现有本机.NET Android、SDK及JDK；前端构建统一Chrome70，共31文件，错误0。
- `source-manifest.json`记录构建前186份运行源码/项目/前端资源和构建工具摘要、工作区状态及基线HEAD `9971cd5c80b9fb5db6e44895bfc47468a120984f`。打包后再次核验，186文件变化0，避免其他会话并发修改造成包与源码不一致。该HEAD只表示基线，APK同时包含未提交改动。
- `android-package.json`：CRC、31份兼容文件及源摘要、共享前端、运行资源、字体及本轮程序集存储逐字节核对通过，2291条文件记录，errors=0。
- 程序集存储与本轮隔离输出匹配，包含重新编译的Android原生视口处理；未复用旧APK的游戏程序集。
- `signature.log`：v2/v3签名通过，与旧测试包证书一致；`zipalign -c -P 16 4`通过。aapt2确认包名、0.2.16/code19及仅arm64-v8a。
- `SHA256SUMS.txt`提供最终校验摘要；构建日志为`build.log`，源码清单生成/复核脚本为`capture-source.ps1`。

本轮为打包及包内容验证；共享界面/兼容/原生编译回归见此前[完整视口适配记录](SCREEN_VIEWPORT_FIT_20261023.md)。未安装设备或执行真机验收，没有上传源码、创建Git提交、推送或发布Release。
