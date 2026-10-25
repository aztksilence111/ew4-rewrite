# 确认键、费用提示与上将名册修正

2026-10-25，按用户三张2376×1080真机截图反馈修正。原截图仅作为视觉证据，未改动。

## 实现

- `single-action.css` 原来使用 `battle-scale`，操作栏已使用较小的 `battle-compact-scale`，导致确认键放大、向下越界及费用提示过大。统一紧凑比例，并在 `positionSingleAction()` 中按触发按钮实际宽度计算确认层缩放，使定位和绘制共用同一比例；确认键覆盖原按钮，不推动操作栏。
- `#hq-deployment-quota` 从固定 `left:255px` 改为整个帽子/文字组合按页面50%居中，适配铺满实际视口的宽屏菜单。
- 上将列表取消固定最少12槽，使用指挥部各星区总容量（默认60槽）与实际持有数量的较大值；两排逐列排列，复用已有鼠标拖动及触摸左右滑动。空槽数量独立于实际可派遣名额，不增加派遣权限。
- 沿用指挥部已保存标签的三角和优先排序，不增加筛选、标签编辑或滑动按钮；进入时仍回到首列。
- 帽子读取 `manifest.layouts.form_deploygeneral` 中 `image_depnums.attributes.name/w/h`，缺省回退原图和26×19；原PNG覆盖接口可直接换图。实际名额继续使用 `deploymentLimits` 的全局/剧本覆盖、0/null支持及战局后端校验，默认12不变。[作者说明](../rewrite/mod-kit/examples/deployment-limits/README.md)已补接口位置。

## 验证

- 新增 `tools/verify_ui_feedback.cjs`：124项通过，异常0、缺图0。844×390、2376×1080、390×844、1280×720、1024×768五视口，80/100/150三档设置，确认键与触发按钮位置/宽高一致，费用框完整可见；名额组合居中、60槽/两排完整、真实触摸双向滑动、末槽可达、标签置顶及无额外按钮。替换帽子图片名/尺寸后仍居中，真实任命更新为1/12。
- 原将领/部队页面84项及指挥部94项回归通过，包含18名不同定义将领、跨星区标签排序、上下排列、任命和信息返回、鼠标/触摸滑动及满额名册。截图、结果保存在 `build/ui-feedback-20261025/unit-general/`、`headquarters/`。
- Web构建、Android ARM64 Compile均0警告/0错误，JS语法及局部diff检查通过。默认系统dotnet缺Android工作负载，使用项目既有 `.codex/tools/dotnet-android` 及本地Android SDK/JDK后完成编译，没有安装或修改系统环境。
- 旧引擎兼容产物在实际HeadlessChrome/71.0.3563.0完成92项检查，异常0，覆盖菜单、Canvas/Pixi、输入及存读档；结果为 `build/ui-feedback-20261025/chrome70/results.json`。目录沿用测试工具命名，实测版本为71，不记为Chrome70或Android WebView验收。
- 主证据：[124项结果](../build/ui-feedback-20261025/browser/verification.json)、[同截图比例确认键](../build/ui-feedback-20261025/browser/reported-action.png)、[同截图比例名册](../build/ui-feedback-20261025/browser/reported-roster.png)。此为浏览器触摸模拟与截图验证，未进行Android真机验收。

## 本地预览

`http://127.0.0.1:5533`，PID31688。宿主在 `build/ui-feedback-20261025/runtime/`，独立 `profile/` 与 `mods/`；前端为当前工作区源码。其他既有服务和玩家档案未操作。未生成新APK、正式包或发布，也未覆盖旧测试APK；已有APK不包含本次修正。
