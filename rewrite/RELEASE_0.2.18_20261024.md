# 0.2.18：运输、经济面板、音乐与网格修复

2026-10-24。Windows x64 独立 Web 包与 Android ARM64 APK（版本码 21）。规则 API 2，沿用既有 Android 签名。精确源码、包大小和 SHA-256 见 release-manifest.json 与 SHA256SUMS.txt。

- 海上运输按原版限制为两点移动预算，玩家和 AI 共用；保留登陆后的内陆长期路线规划。
- 城市经济面板显示结算使用的真实产出，并在驻军经济技能倍率变化时刷新；经济大师实际到账逻辑沿用原有正确结果。
- 音乐在触摸结束和点击时重试解锁，补齐 Android WebView 媒体播放设置与 OGG 类型。
- 网格位于地形、领土及建筑之上，兵模、旗帜与将领头像之下；Canvas 和 Pixi 共用绘制顺序。
- 继承 0.2.17 的性能、Chromium 70 兼容、Web 等比菜单、移动布局及技能说明更新。

发行包不包含玩家账户或存档。Android 实际出声、手机性能及屏幕闪动仍待真机复测。桥梁缺失已完成核查，本版本尚未补绘。

构建与校验记录见[发布进度](https://github.com/aztksilence111/ew4-rewrite/blob/main/rewrite/RELEASE_PROGRESS_0.2.18_20261024.md)。
