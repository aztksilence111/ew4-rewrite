# 固定局面性能诊断

只把 C# 源码和项目复制到仓库 `build/` 下编译。计时探针、反射读取和浏览器对照替换不进入正式游戏；专用存档与 Mod 目录不得指向玩家档案。

2026-10-23 用户确认：以后性能测试默认开启省略动画的跳过快进，不再询问。主性能口径使用 `batch` 与增量协议；普通模式只在明确的动画/协议正确性对照时单列。首轮与预热后数据都要报告，不能仅用预热成绩代表手机第一次过回合。

后续补充：用户认为真机分段难测，因此分段数据不是开始优化的前置条件。先按桌面热点优化并验证正确性，每批用手机整轮耗时比较；确需进一步定位时再加入包内自动计时/可截图摘要，不要求用户连接电脑调试。

```powershell
$env:EW4_AUDIT_MODES='batch'
$env:EW4_AUDIT_DELTA='1'
$env:EW4_AUDIT_TRACE='0'
python tools/PerformanceAudit/prepare.py . build/performance-audit-20261023/plain
dotnet run -c Release --project build/performance-audit-20261023/plain/Harness -- . build/performance-audit-20261023/plain-results
python tools/PerformanceAudit/prepare.py . build/performance-audit-20261023/deep --instrument
dotnet run -c Release --project build/performance-audit-20261023/deep/Harness -- . build/performance-audit-20261023/deep-results
```

默认 conquest1/fra、conquest4/fra，每种模式三次，第一遍单列。以上命令显式选择批处理；底层 Harness 未设置环境变量时仍保留普通/批处理双模式兼容行为。`EW4_AUDIT_SCENARIOS`、`EW4_AUDIT_MODES` 可传逗号列表，`EW4_AUDIT_SAMPLES` 控制样本数。场景身份及双随机状态固定，完整会话哈希核对模式/重复间一致。每次采样写入 results.json；不可并发运行计时任务。

Pixi/性能实施验收增加 `EW4_AUDIT_DELTA=1`，显式请求普通及批处理增量响应；旧源码基线使用默认完整协议。`EW4_AUDIT_TRACE=1` 在每步响应后累计世界与事件顺序摘要，仅普通模式的逐步摘要可相互比较。Trace 含额外存档编码，必须与性能计时分开运行；正式计时将 Trace 设为 0。最终源码证据及浏览器入口见 `reverse/PIXI_IMPLEMENTATION_20261023.md`。

`prepare.py` 使用显式函数清单注入嵌套计时，配置 Validate 按所属类型区分。source-manifest.json 保存原输入哈希及自动注入清单；WorldState.Copy、保存包装和write/flush另有显式锚点。统计按同步单线程设计，不能拿来测并行生产服务器。探针增加开销；绝对基线使用未注入的 plain，不把被包含的时间相加，也不把累计分配当常驻内存。

2026-10-23 真机反馈复核修复了旧响应编码锚点，并补 `ValidateCore`、`WorldSave.EncodeValidated` 和当前流式保存的 serialize/write、flush 分段探针。世界校验计数表示实际进入 `ValidateCore` 的次数，不包含 PlanningScope 命中后提前返回的请求。最新证据与后续优化顺序见 `reverse/TURN_PERFORMANCE_REVIEW_20261023.md`、`build/turn-review-20261023/summary.json`；只在诊断副本注入，不进入 APK。

2026-10-23 首次 instrumented 是较少探针版本，deep 进一步区分复制和写盘。`summarize.py build/performance-audit-20261023` 检查 plain/instrumented/deep 的输入及最终身份一致，并汇总基线。文件没有全部保留时勿调用此汇总入口；原始 results.json 可独立检查。

`render.cjs isolated-url output-folder` 在专用 Web 服务运行桌面 Edge（1280×720、DPR2），记录渲染各阶段、缓存构建、海岸模板生成和真实指针拖动。依赖沿用仓库既有 Playwright 安装。测试前冻结 wwwroot 并记录哈希，以免其他工作区改动混入。

渲染对照限制：no-body-draw 只在测试页跳过兵模位图绘制，仍计算姿态及画兵牌，不是完整远景 LOD 实现；state-clone 是人为每帧替换相同内容的对象以测状态准备开销，不代表真实游戏每帧都会收到新状态。movement 是局部呈现夹具，不提交实际部队动作。强制绘制用来测60Hz交互负担，idle保留现有30Hz节流。所有时间为 JS 提交绘制及 rAF 间隔，不是手机GPU完成时间。

上述 `render.cjs` 是迁移前 Canvas 诊断工具，30Hz 描述属于旧基线。迁移后的检查使用 `tools/verify_pixi_renderer.cjs URL OUTPUT`，覆盖三视口、WebGL 1/2、真实移动与战斗、粒子混合及上下文恢复；生命周期/快进检查用 `EW4_PERF_URL=URL`、`EW4_RENDER_REPORT=OUTPUT` 配置 `tools/verify_render_skip.cjs after`。这些桌面浏览器结果均不代表 Android 真机性能。
