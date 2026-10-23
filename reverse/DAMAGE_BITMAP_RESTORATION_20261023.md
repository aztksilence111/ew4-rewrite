# 伤害数字恢复原版图片

2026-10-23。用户要求伤害数字恢复原版图片显示，其余数字字体保持当前实现。此要求覆盖此前“伤害数字也统一内置字体”的历史要求。

## 实现

- `effects.js` 的 `DamageNumbers` 恢复原有图集裁片、字形间距、偏移及 24 世界单位显示高度；普通伤害保留原图，暴击保留红色染色及原有暗边、透明度。
- `app.js` 启动时预载 `font_fight_num_hd.png` 和 `fight-font.json`，优先兼容当前 manifest 的字形定义；旧 manifest 缺项时使用独立字形数据。
- 其他界面数字继续使用当前包内字体；未编辑通用文字渲染器、字体文件或图片资源。
- 保留工作区已有 Pixi、性能和点击闪动修改；仅更新涉及脚本的缓存版本。

## 验证

`node tools/verify_damage_bitmap.cjs http://127.0.0.1:5476`

14 项通过，脚本错误 0、缺失资源 0。包含全部十个数字及负号与原图逐像素比对、暴击颜色及透明度/暗边、普通 UI 数字画布不变、缓存和生命周期、真实战斗触发、旧 manifest 回退。1280×720、844×390、390×844 三张截图已人工查看。

证据：`build/damage-bitmap-20261023/browser/verification.json` 及同目录截图。修改的四个 JS 文件语法检查通过。

原 `verify_feedback_14_17.cjs` 的伤害预期已更新为图片字形高度 48。综合执行通过前 15 项后，在 `legacy geographic labels retain nonblank dimensions 1280` 处中断，未到后面的伤害断言；该脚本依赖当前地图标签缓存状态，不据此声明综合全部通过。错误及缺失资源均为 0，记录在 `build/damage-bitmap-20261023/feedback/verification.json`。

独立预览：http://127.0.0.1:5476，PID 2756，使用 `build/damage-bitmap-20261023/preview-profile` 独立档案。此轮未打包 APK、未发布或真机测试。
