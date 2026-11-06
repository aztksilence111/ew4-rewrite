# Android 顶栏与系统栏手势

2026-11-06：用户反馈点击 R 版顶栏返回键容易呼出手机状态栏，希望只在下滑时呼出。

原实现 Android 11 起已设置 `ShowTransientBarsBySwipe`，Android 8–10 使用 `ImmersiveSticky`。本次将旧版 decor 沉浸兼容标志应用于全部支持版本，再在 Android 11 起设置现代 InsetsController 的滑动临时显示策略；`OnResume` 也恢复全屏，保留既有焦点恢复、旋转及退出取消的恢复入口。顶栏返回动作和布局不变。

预期：点击游戏按钮不呼出系统栏，从屏幕顶边下滑仍可临时显示状态栏；底边系统导航手势保留。Android 系统负责识别边缘手势，应用不能替代厂商手势阈值；当前无真机证据确认此次兼容补强能消除用户设备上的误触，不把既有配置差异当作已证实根因。

验证：Android ARM64 Release `Compile` 成功，0 警告、0 错误；定点 `git diff --check` 通过。ADB 无连接设备，未做点击／下滑／旋转／文件选择返回的设备回归，未生成 APK 或更新正式发行。后续 APK 验证以上操作并确认滑出的系统栏可自动收起。

本轮编译产物核真实路径、链接、Git 跟踪状态与活动进程后清理；简短凭据保存在 `build/maintenance-20261106/android-system-bar-gesture.json`。保留既有存档导入改动和玩家服务／资料。
