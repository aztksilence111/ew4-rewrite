# 固定局面性能诊断

2026-10-25 回退规划续接：`followup_regression.py . --prepare --run` 要求先用 `prepare.py` 冻结新的 before，本批固定证据目录 `build/turn-regression-followup-20261025`；已有输入不会覆盖。不加参数只核对结果。`followup_branches.py .` 对任务、实际 Lua 技能、东亚小夹具串行比较，探针仅入隔离构建；`followup_checks.py .` 保存客户端/专项退出码，`finalize_followup.py .` 核对结果、源码与实际包。具体结果及真机/大型东亚限制见 `reverse/TURN_REGRESSION_FOLLOWUP_20261025.md`，本轮未证实额外整轮提速。`verify_turn_followup.cjs URL OUTPUT [--ordinary-only]` 保存前端探针和像素检查，`verify_pause_objectives.cjs URL OUTPUT PROFILE` 可指定隔离账户目录。

收尾补充：工作区并行地理功能已修改前端和宿主，`finalize_terrain.py`对全部源码差异单列，核验对象为构建清单和APK，不要求活工作区一致。`freeze_terrain_preview.py`只在目标不存在时从摘要验证后的APK提取独立前端，供5520冻结预览使用，保留输入，不覆盖工作区。下段“前端一致性”指初次核验，当前状态以verification.workspaceDrift为准。

2026-10-25 当前原图地形修复使用 `python tools/PerformanceAudit/finalize_terrain.py .` 只读核验，报告 `reverse/TERRAIN_VISUAL_FIX_20261025.md`。它校验构建清单/APK摘要、包内后端对既有测量清单、前端一致性、四组交互和移动画面、规模/Pixi/实际Chrome70/模型加载/原绘制及清理记录；后续工作区后端差异单列，不宣称活源码与包一致。`verify_terrain_visuals.cjs URL OUTPUT ORIGINAL_BATTLEFIELD` 检查原图纹理与Canvas参考，`verify_render_scaling.cjs` 保留完整原渲染基线，不能用灰色占位路径作为最终性能基线。旧 `finalize_plateau.py` 绑定旧前端，仅用于历史输入。

2026-10-24/25 平台规划四批及通用大地图渲染交付见 `reverse/TURN_PLATEAU_IMPLEMENTATION_20261024.md`。`long_curve.py ROOT --dotnet DOTNET` 从冻结 before/after 运行20轮及第1/10/20轮新进程恢复，六小时上限；`summarize_long_curve.py ROOT` 只核对结果。`prepare.py --instrument --strategy-detail` 可在隔离诊断副本记录不同战略单位、目标尝试和放弃原因，不进入游戏程序集。长曲线曾与前端验证重叠，其计时不可冒充正式串行对照，工作量计数和新进程恢复单列。交付后用 `finalize_plateau.py ROOT` 只读核对源码/实读内容/实际APK、回归记录及证据指纹。

2026-10-24 连续回合诊断入口为 `measure_growth.py ROOT EVIDENCE --run --dotnet DOTNET`。先用 `prepare.py` 冻结并构建 `before`、`after` 及对应的 `-deep` 副本；计时按正反顺序串行运行两组，每阶段三条热曲线，每条覆盖第一至第六回合。普通逐步 trace、深探针及同一第六回合存档在新进程中的冷/热恢复另跑。省略 `--run` 仅核验与重建 `growth-summary.json`，不会推进对局。入口有六小时上限；已有完整 `inputs.json` 的组可续接，不完整目录会拒绝覆盖并要求先检查。

Harness 新增 `EW4_AUDIT_ROUNDS=1..20`（默认1）、`EW4_AUDIT_START_SLOT`（隔离 ClientSlot 路径）、`EW4_AUDIT_RETAINED_MEMORY=1`（仅诊断）。每轮在计时外保存起点及双RNG，结果含历史规模、活跃单位/势力、步骤、分配与GC；深探针另含校验/搜索工作量。存活托管堆采用计时外强制收集，不能混入正式计时或宣称为进程总内存。首次曲线与热曲线分列，后续回合不是首回合的重复样本。默认不开启强制收集。

连续回合批次的核心/客户端/性能/Mod检查、Web/Android Compile及征服4逐步对照完成后，可运行 `finalize_growth.py ROOT EVIDENCE`。它核对成功结果、运行源码和实读内容，再把测量副本、工具与回归输入指纹保存到 `verification.json`；不构建APK，也不代表真机验收。旧驱动GC计数若多包含最终编码，`gcBoundary`会标明，勿直接比较。

2026-10-24 下一批使用 `measure_stages.py ROOT EVIDENCE --run`：先为 `before/search/tactics` 保存未注入和 `-deep` 副本并分别 Release build，再按正序/逆序两组串行运行，每图每阶段6个热样本、2个进程内首次样本；普通逐步 trace 和深探针独立运行。该脚本核对完整源码文件集合（含新增/删除）、实读内容哈希、初始档、最终状态/双RNG/停止边界，保存原始数据和 `stage-summary.json`。只重建摘要时省略 `--run`，不推进任何存档。

诊断计数包含搜索入队/出队/过期/访问、距离场查询/未命中/淘汰，以及按当前调用栈分组的世界完整校验、证明命中和 disabled/unseen 回退。地块及单位访问量在实际循环内累计；嵌套在候选模拟中的事件检查归入 candidate，事件处理工作仍按原实现执行。临时诊断源码不会进入 Web/APK。`inputs.json` 记录本次真正读取的内容集合及运行时，`source-manifest.json` 和 `driver-manifest.json` 记录源码及驱动身份。

本批回归、浏览器和APK检查全部完成后，运行 `python tools/PerformanceAudit/finalize_evidence.py . build/turn-next-20261024` 核对成功记录及源码/内容稳定，补充测试/宿主/构建脚本、锁定依赖、SDK、桌面程序集与最终包的指纹到 `verification.json`。此入口固定本批目录约定和原证书，不是任意发行包验证器；它读取已完成的结果，不把记录核对冒充重新运行测试。

2026-10-24 首批模板冻结/同步校验复用对照见 `reverse/TURN_OPTIMIZATION_20261024.md`。运行 `python tools/PerformanceAudit/compare_round.py . build/turn-opt-20261024` 可核对两图逐步等价、源码漂移及串行主计时；新模板实际校验计数取 `Combat.UnitProfile.ValidateCore`，入口 `Validate` 已含证书命中，不能混用。

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
2026-10-24 设备规划续接：`device_plan.py ROOT --dotnet PATH --prepare` 从`build/device-plan-20261024/before`与本批最终源码构建`baseline/save/movement/combined`独立消融副本；`--run`以两个反向串行进程组测六轮，各轮3个热样本，另跑逐步trace、受控移动完整校验双跑、深探针及同第六轮起点新进程恢复。计时期间不要并行跑构建、浏览器或其他重负载测试；输出`summary.json`，保留旧批次证据。

`android_pair.py ROOT --dotnet PATH --sdk PATH --java PATH`建立同源码、同copy链接模式的ARM64 JIT/Mono AOT本地测试包；`--resume`仅复用源码及APK摘要仍一致的已核验变体。核对五个玩法程序集IL、运行模式标记、188个实际AOT注册库、签名、16KiB及资源，不安装、不上传；AOT是Mono预编译，仍允许运行时回退，并非NativeAOT。不得并行构建共享原项目，以免还原资产文件互相覆盖。

`measure_entry.cjs URL FOLDER`在新浏览器页里仅切换预载/首屏等待范围，按正逆顺序各测三次立即开始和选国停留1.5/5秒，共36组，保存资源/模板数量及分段时间。`--resume`复用相同实验条件的完整页样本；偶数样本中位数取中间两值均值。它是桌面浏览器手机视口实验，不是手机实测，也不改变后端开局时点。

同工作区有其他内容迁移时，`freeze_package_inputs.py ROOT`从已核验JIT包恢复资源/前端，从本批运行源码摘要恢复原始输入；隔离目录不覆盖活工作区。运行内容描述从HEAD恢复并逐项核验2233个输入；`--descriptor-only`仅重建该描述。device_plan与finalize自动使用带收据的冻结目录，将活源码差异另列，不能把冻结包的测量宣称为同期迁移版本的性能结论。

全部结果生成后，`finalize_device_plan.py ROOT --dotnet PATH`检查已有结果、源码/内容/包摘要、签名延续及测试收据，写`verification.json`和`SHA256SUMS.txt`。本脚本不会重跑测试，不能用它替代缺失的验证。
