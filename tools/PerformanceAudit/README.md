# 固定局面性能诊断

只把 C# 源码和项目复制到仓库 `build/` 下编译。计时探针、反射读取和浏览器对照替换不进入正式游戏；专用存档与 Mod 目录不得指向玩家档案。

```powershell
python tools/PerformanceAudit/prepare.py . build/performance-audit-20261023/plain
dotnet run -c Release --project build/performance-audit-20261023/plain/Harness -- . build/performance-audit-20261023/plain-results
python tools/PerformanceAudit/prepare.py . build/performance-audit-20261023/deep --instrument
dotnet run -c Release --project build/performance-audit-20261023/deep/Harness -- . build/performance-audit-20261023/deep-results
```

默认 conquest1/fra、conquest4/fra，各普通/批处理三次，第一遍单列为预热。`EW4_AUDIT_SCENARIOS`、`EW4_AUDIT_MODES` 可传逗号列表，`EW4_AUDIT_SAMPLES` 控制样本数。场景身份及双随机状态固定，完整会话哈希核对模式/重复间一致。每次采样写入 results.json；不可并发运行计时任务。

`prepare.py` 使用显式函数清单注入嵌套计时，配置 Validate 按所属类型区分。source-manifest.json 保存原输入哈希及自动注入清单；WorldState.Copy、保存包装和write/flush另有显式锚点。统计按同步单线程设计，不能拿来测并行生产服务器。探针增加开销；绝对基线使用未注入的 plain，不把被包含的时间相加，也不把累计分配当常驻内存。

2026-10-23 首次 instrumented 是较少探针版本，deep 进一步区分复制和写盘。`summarize.py build/performance-audit-20261023` 检查 plain/instrumented/deep 的输入及最终身份一致，并汇总基线。文件没有全部保留时勿调用此汇总入口；原始 results.json 可独立检查。

`render.cjs isolated-url output-folder` 在专用 Web 服务运行桌面 Edge（1280×720、DPR2），记录渲染各阶段、缓存构建、海岸模板生成和真实指针拖动。依赖沿用仓库既有 Playwright 安装。测试前冻结 wwwroot 并记录哈希，以免其他工作区改动混入。

渲染对照限制：no-body-draw 只在测试页跳过兵模位图绘制，仍计算姿态及画兵牌，不是完整远景 LOD 实现；state-clone 是人为每帧替换相同内容的对象以测状态准备开销，不代表真实游戏每帧都会收到新状态。movement 是局部呈现夹具，不提交实际部队动作。强制绘制用来测60Hz交互负担，idle保留现有30Hz节流。所有时间为 JS 提交绘制及 rAF 间隔，不是手机GPU完成时间。
