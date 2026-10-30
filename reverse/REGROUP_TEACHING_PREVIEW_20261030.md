# 整编教学能力预览修复

2026-10-30。先读取用户截图 `G:\Workplace\QQagent\runtime\AstrBot\data\temp\media_image_20261030141125473_84e0.jpg`，确认整编预览仅见结果数字，无法辨认提升的能力。

## 原因与修复

- 原生 `grid_preview` 的两列宽度均由 `rowh=28` 推导，条目内图标和数字被挤在 28px 中；浅色图标在浅色底板上也缺乏对比。
- 商业和移动使用不存在的 `upgrade_tag_commerce.png` / `upgrade_tag_movement.png`，现对应 `generalsinfo_trade.png` / `generalsinfo_move.png`。
- 共享 Web／Android 前端改为完整宽度条目，明确显示兵种／能力名称、原星级、箭头及结果星图；七种教学均支持，图标加深以便辨认。
- 五星内按实际数量绘制原版星图，超过五星用一颗星加 `×实际数量`；缺少的原能力按零比较，仅显示后端预览中确实增加的能力。多项变化可在原预览区域内滚动。
- 继续使用实际 `preview_inherit` 返回值；教学计算、上限参数和存档机制没有调整。

## 验证与交付

- `node tools/verify_regroup_teaching.cjs <隔离测试URL> <输出目录>`：155 项通过，源码 JavaScript和 Chrome70 兼容 JavaScript分别验证，桌面 1280×720、横屏 844×390、竖屏 390×844。
- 覆盖七种教学、4→5、6／17／100／2147483647 星、七项滚动、无提升条目，以及真实教学将领招募、实际预览、确认整编和重载存档一致性。截图人工检查通过，浏览器异常／缺资源／未知图标警告均为零。
- 前端兼容构建 54 文件错误零；修改脚本语法检查和 `git diff --check` 通过。未制作新 APK，未做 Android 真机验收。
- 停止隔离测试服务后核对真实路径、重解析点、Git 与活动消费者，删除本轮测试档案、缓存、截图和日志共 14 文件、1,152,237 字节，剩余零，错误零；凭据 `build/maintenance-20261030/teaching-preview-cleanup.json`。
- 干净预览：<http://127.0.0.1:56154>，PID 51096，既有发行宿主加载当前共享前端，独立资料目录 `rewrite/local/teaching-preview`。真实玩家资料、Mod、正式发行包和同期其他修改均保留。
