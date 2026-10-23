# PixiJS 与性能优化断点

2026-10-23。用户在本会话明确启动实现；旧审计中的“仅记录、实施暂停”是历史边界，不再表示本专项暂停。采用一条 PixiJS/WebGL 战场路线；静态文字、少量染色素材、地图块和菜单允许继续使用 Canvas 2D。未授权正式发布，不改发行版本。

工作目录：`E:\Workplace\game\o4mod`。先读本文件，再查 [实施报告](../reverse/PIXI_IMPLEMENTATION_20261023.md)。保留工作区并行的点击闪烁、定位按钮及伤害数字修复，不覆盖他人修改。

已落盘：PixiJS 7.4.3 离线库和官方 CSP 适配、复用精灵/姿态、地图块缓存及依赖失效、模板分时预热、远景兵牌与 60Hz 活动调度、普通/快进增量协议及完整重同步、规划内验证复用、候选移动路径复用与共享未变对象、战略局部落点/连通性过滤/有界距离场、跳过无行动单位、存档去除嵌套 JSON 解析并流式写槽。实际命令及提交继续完整校验，扩展使用保守模拟路径，落盘仍保留 Flush(true) 和原子替换。

本地验收完成：核心310933、客户端2569+24、专项190、原地图13731、停绘15项；三视口/WebGL1和2/粒子混合/真实移动及攻击反击/上下文恢复通过。Web及Android ARM64 Compile零警告错误。两图逐步世界+事件顺序、最终session、双RNG及287/368步均与修改前HEAD9971cd5基线一致。

最终顺序Release计时每图每模式3次、首轮预热排除：快进8.164→5.539s、9.423→7.031s；普通12.791→6.486s、16.700→8.463s，普通响应字节减少99%以上。证据`build/pixi-performance-20261023/{performance-summary-final,equivalence-final,frontend-manifest-final}.json`及`*-results-final/`、`browser-final/`、`render-final/`；测试入口`tools/verify_pixi_renderer.cjs`、`Ew4.Verify --performance-only`、`tools/PerformanceAudit/`。

当前预览5475/PID14024，冻结前端preview-final/wwwroot，独立preview-saves。旧5473/5474服务已停。本轮未改版本、打APK或发布。下一步是同机同存档Android真机复测52秒案例及冷远景长帧/长时内存；不宣称这些已经解决，不从桌面数据推算手机秒数。无待跑本地验收项；具体剩余限制见实施报告。
