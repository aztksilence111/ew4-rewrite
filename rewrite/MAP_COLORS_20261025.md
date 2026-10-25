# 国家与阵营地块配色接口

日期：2026-10-25。独立预览：<http://127.0.0.1:5562/>，PID 39948，独立 profile，未更新玩家服务。

## 核对与改动

- 国家颜色已有接口：`client/countries.json` 的 `countries.<国家ID>.color`。
- 事件新增势力已有接口：`faction_create.color`；局中再次改色使用 `faction_identity.color`，存档保留。
- 阵营地图此前使用前端固定色池，没有作者配置入口；本轮新增 API 3 的 `mapColors` 注册字段。
- 内置注册到 `rewrite/data/rewrite/map-colors.json`，默认空配置保持当前外观。支持 `*` 全局默认、逐剧本覆盖、按完整 camp ID 指定颜色，以及可选的 `campPalette` 色池。
- 接受 RGB/RGBA，包括完全透明黑色；显式阵营不挤占其他色池位置，支持事件新增阵营与原型同名 ID。颜色变化刷新地块缓存，相同配置不重建。
- 普通/辖区地图继续使用国家色；阵营图的地块和海岸渐变使用阵营色。配置不改变归属、外交或自动兵牌字母。
- `MapColorsPresentation` 负责校验、默认合并；`ExtensionLoader` 读取并纳入内容指纹；`ClientContent` 拒绝未知剧本；`ClientGame` 返回有效配置；共享 Renderer 供 Canvas/Pixi 使用。
- [完整配置文档](mod-kit/docs/porting/MAP_COLORS.md)和源码示例已加入 Mod 工具包导航。

## 验证

- 配色专项 20、既有势力创建/改色/存读档 56：通过。
- 客户端 64525、海军部署 24：通过。
- 实际 Edge 下 Canvas/Pixi × 1280×720、844×390、390×844：179 项通过，异常/缺图 0。涵盖真实像素改色与撤销、RGBA 投影、归属不变和缓存复用；桌面与竖屏截图已目视核对。
- 兼容前端 39 文件构建、JS 语法、相关 diff 空白检查通过；Web 与 Android ARM64 Compile 零警告错误。
- Android 使用项目已有 `.codex/tools/dotnet-android` SDK、android-sdk、android-jdk；初次默认 SDK 缺工作负载、随后无还原编译缺 RID 资产，切换已有 SDK 并还原后编译通过，未安装工具链。
- 示例资源曾经目录校验通过（95 剧本、2 文件、0 警告）；用户随后要求不用打包示例，已移除校验生成的 `mod.json`。未导出 `.ew4mod`、APK 或 ZIP。
- 最终浏览器证据：`../build/map-colors-20261025/browser-configured/verification.json` 与同目录截图。未做真机验收或正式发布。

## 清理与保留

清理前核对本任务真实路径、活动进程和 Git 跟踪状态。删除首轮重复浏览器输出 11800042 字节和本轮随机检查存档 10380300 字节，共 22180342 字节；保留最终截图、运行目录、预览 profile 和其他任务文件。
