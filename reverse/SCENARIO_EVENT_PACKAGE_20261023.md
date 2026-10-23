# 图文事件测试包基础版本修复

2026-10-23。用户导入 `scenario-event-layout.ew4mod` 时提示“Mod格式或基础资源版本不匹配”。在独立5495服务复现：旧包绑定 `743fba82…`，当前基础资源为 `fd09d5c0…`。

## 交付

- 当前入口：`rewrite/content/scenario-event-layout.ew4mod`，ID `scenario-ui-fixture`，版本 `1.1.0`。
- 大小：1,513,203字节；SHA256 `9570500c5589872b880182b42759b9fdb94db86bd69b088b01bb64fdb75c2a30`。
- 只保留原包 `rewrite/extension.json`、`rewrite/events.json`、`rewrite/images/council.png`，三项摘要逐字节一致；移除25项旧界面/原版资源覆盖，使用当前内置资源。通过ModTool重新计算基础/文件/规则指纹并打包，没有绕过校验。
- 原包仍在 `rewrite/reports/scenario-event-layout-20261021/`，未覆盖。

## 验证

ModTool完成95剧本校验及导入/导出/重导往返。独立Web上传旧包复现报错，新包导入启用成功；16项浏览器检查通过，异常与缺图均0。验证图片加载、12选项、999金币条件、长文独立滚动、1136×640/844×390/390×844窗口、待选保存恢复、方案4准确增加5金币及自动存档重载不重复发奖。桌面和竖屏截图已目视核对。

证据、重封装脚本及浏览器验证脚本位于 `build/scenario-event-layout-20261023/`，其中 `package-checks.json` 与 `browser-checks.json` 为本轮结果。重封装脚本首次运行的汇总属性计数出错，修正后由已完成的验证结果生成报告，封装及导入校验均已通过。

游戏源码、玩家预览与正式账户/存档未修改；测试只使用独立目录，5495测试服务收尾停止。没有新APK、Git提交或发布；Android导入仍待用户实机验证。导入新版后启用，并新开第一征服法国。
