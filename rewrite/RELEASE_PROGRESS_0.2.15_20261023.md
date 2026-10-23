# 0.2.15 发布进度

2026-10-23 用户授权同步 Git，构建 Windows Web 包和 ARM64 APK 并上传 GitHub Release。

当前阶段：正式发布完成，[GitHub Release](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.15-20261023)。包对应源码`a294c29637ab354c57bbff23f1e3796e5195fc32`，版本0.2.15/code18，沿用旧签名。包含蜂窝定位与计数、侧边阴影、字体外观、战役可见性及背景音乐；未实施的性能/闪烁规划不纳入完成声明。

四附件均已上传并核对远端大小和SHA-256；`remote-audit.json`错误0、draft=false。Web ZIP为170,723,811字节，SHA-256 `9c9fc64e90b702c41462acedb7c1dd57829c6ec57b91277afd096ac108c46606`；ARM64 APK为143,722,859字节，SHA-256 `09329a5719edf0a9bbf61422ab9a49c35588b27f99f7dcb89d9207b2ecac2a62`。另有release-manifest.json和SHA256SUMS.txt，不含玩家存档。后续提交只记录发布完成，不改包内运行源码。

本轮验证：完整核心回归errors=0；战役可见性20/客户端2540/海军24/Mod106/战役音乐Mod17全部通过。Windows自带运行时构建、2744项发布目录核验、发行资源检查及11249项独立启动/存读档与资源闭包检查通过。发布版浏览器蜂窝185/字体42/综合73/战役音乐59全部通过，0异常缺图；5首音乐实际解码播放、84个主入口及国家分支均核验。旧综合测试已适配分离阴影后的主体尺寸和蜂窝新位置。

ARM64 APK构建及前端/2233份运行资源/游戏程序集逐字节核验通过，包含music.js、music.json和5首MP3。0.2.15/code18、同上一版证书、v2/v3签名和16KiB对齐均通过。浏览器驱动工作目录已修正为发布目录，复测通过。尚未进行Android真机测试。

证据目录：`build/release-0.2.15-20261023/`。`progress.json`记录本机构建步骤；`assets/release-manifest.json`记录包对应的精确源码、附件大小及SHA-256；远端正式发布以`remote-audit.json`的errors=0为准。

发行说明：[RELEASE_0.2.15_20261023.md](RELEASE_0.2.15_20261023.md)。未运行Android真机。
