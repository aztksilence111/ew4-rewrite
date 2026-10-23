# R 版点击闪动修复

2026-10-23。已实施顶部资源栏重复创建修复；桌面浏览器验证与 Android ARM64 Compile 通过。未生成新 APK、未发布、未执行真机录屏，不能据此关闭未定位的整屏闪动。

## 实现

- `rewrite/Ew4.Web/wwwroot/index.html` 保留三组资源行、图片及独立数字容器，图片直接引用原有逻辑资源。普通点击、busy、选中、数值变化均不设置图片 src、不删除或重建图片节点。
- `app.js` 的 `refreshHud()` 从当前已安装状态的玩家势力读取 money / industry / food，逐项比较显示值，仅替换变化数字的文字子节点。没有动作白名单、前端预扣或按世界 revision 清空整栏。回合文字与国旗同样避免重复更新。
- 重开、读档、玩家势力切换后重新读取当前权威状态；相同数值可继续复用数字节点，文字渲染器实例发生变化时失效。没有另存一份会串到旧势力的资源状态。
- `original-ui.js` 的文字画布记录实际字体依赖、可用字符、字体高度、绘制选项和请求的栅格倍率。无关字库分片加载或相同倍率通知不再重设画布；真正字体或倍率变化仍重绘原画布。现有字体回归覆盖延迟字库和中文/补充平面字符。
- `style.css` 将 HUD 层级限定在战场内部，加入安全区偏移，保持暂停、事件、设置、详情和加载窗口的遮挡。暂停在教学标记之上，事件保留原有层级。
- 初始化时等待三个图标解码再开放开局。Mod 继续沿用 `ModStore.Frontend()` 注入的 `/content/r-<revision>/...` 路径及 `ReadContent()` 覆盖入口；现有 Mod 切换通过页面重载生效，因此旧页面的异步图像不会写入新页面的节点。未修改资源缓存头或 API 规则。

工作区已有及后续并行产生的 Pixi、性能、规则和工具变更均保留。本项产品修改限于上述四个前端文件，不修改战斗规则或动画节奏；其中 app.js / index.html 原有并行改动没有回退。

## 验证证据

专项脚本：`tools/verify_click_flicker.cjs`。完整结果：`rewrite/reports/click-flicker-20261023/browser.json`，31 项通过，异常 0、缺失资源 0、截图 6 张。最终 UI 层级与重开回归 21 项通过，异常/缺失资源 0，另存 `ui-final.json`。

检查使用资源节点引用、MutationObserver、数字画布 width / height 写入计数、实际后端命令及真实导入 Mod：

- 40 次 refresh/busy 变化、不同部队与空地选择、重复后端状态、前后台恢复、拖图缩放：资源 DOM 零变更，原图标和数字画布保持，数字画布零尺寸重设。
- 失败命令与相同存档重载无资源变化；实际建筑升级仅更新扣费项，重复刷新不再更新。
- 无关稀有汉字字库加载及相同 UI 倍率通知不重绘资源数字；实际改变倍率只在三个既有数字画布各重绘一次。
- 1280×720、844×390、390×844 的图标、数字与九位数资源不重叠、不裁切。暂停、设置、详情、加载遮罩的命中层级正确。
- 真实 Mod 事件只改金币时只换金币数字；事件同时设置三种资源时全部同步。未新增任何动作名分支。
- 事件切换玩家势力后显示新势力的 123 / 456 / 789，保存重载仍正确。
- 两个同 ID、同图片文件名但不同图片内容的 Mod 版本分别导入、启用，版本 URL 和渲染像素一致；切回原版恢复原图像素。

现有 `tools/verify_font_regression.cjs`：42 项通过、异常 0、缺失资源 0、截图 10 张；证据在 `rewrite/reports/click-flicker-20261023/fonts/results.json`。

Web 隔离输出构建、ModTool 构建、Android ARM64 Release Compile 均为 0 警告 / 0 错误。系统 dotnet 缺少 Android workload，改用已有私有工具链完成，不安装或修改系统配置。首次默认 Web 输出被已有服务锁定，随后改用隔离输出成功，没有停止其他服务。

```powershell
dotnet build rewrite/Ew4.Web/Ew4.Web.csproj --no-restore -o build/click-flicker-web -v minimal
dotnet build rewrite/Ew4.ModTool/Ew4.ModTool.csproj --no-restore -o build/click-flicker-tool -v quiet
node tools/verify_click_flicker.cjs http://127.0.0.1:5417
node tools/verify_click_flicker.cjs http://127.0.0.1:5417 --ui-only
& 'C:/Users/WIN11/.codex/tools/dotnet-android/dotnet.exe' build rewrite/Ew4.Android/Ew4.Android.csproj --no-restore -t:Compile -c Release -p:RuntimeIdentifier=android-arm64 -p:AndroidSdkDirectory=C:/Users/WIN11/.codex/tools/android-sdk -p:JavaSdkDirectory=C:/Users/WIN11/.codex/tools/android-jdk -v minimal
```

预览：<http://127.0.0.1:5417>，独立档案 `build/click-flicker-profile`，独立后端输出 `build/click-flicker-web`。测试 Mod 与存档只进入该档案。浏览器读取当前工作区前端；后端为本轮构建快照，不代表后续并行修改也已验证。

## 仍待验证

- adb 当前没有连接设备。本轮没有真机数据，不宣称整屏闪动、卡顿或 WebView 合成问题已全部解决。
- 未打新 APK、未递增发行版本、未提交或发布；旧发行包不包含本修复。
- 真机需同局前后录屏，分别核对图标、数字、地图和全屏，记录 APK、机型、Android/WebView、高清设置及帧时间；如仍整屏闪，继续定位视口/Insets、画布上下文和 WebView 合成。
- 本轮没有穷举所有动作、完整回合收入、快进与本地多人流程；通用变化检测已由真实升级、Mod 单/多资源事件、读档、重开和势力切换验证，其他完整流程仍可随真机回归补测。
