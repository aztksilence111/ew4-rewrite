# 商店金星奖章可用于战役兵种科技升级

2026-11-01。按用户反馈与两张原版截图修复 R 版；截图中的说明仅作为行为证据。源码修复完成，未生成新 APK 或发布正式双端包，未做 Android 真机验收。

- 原因：商店的金星奖章是 `ew4.item.53`（原版 `Star`，function 17），购买后保存在账户物品栏；科技升级原先只读 `TechnologyStars` 或旧账户的通关最佳星数，忽略物品栏奖章。
- 科技页余额改为通关可用星数加持有奖章。升级先扣通关余额，再从物品栏扣所需奖章，用尽移除该物品项；旧账户已有奖章立即可用，无需迁移或改写存档格式。通关最佳成绩仍独立记录，通关余额原有 999 上限保留，物品栏奖章不受该上限限制。
- 胜利结算仅增加通关余额，避免将物品栏奖章重复计入。物品出售、直接用于战场部队升级都会同步降低可用科技余额，保留原有直接升级部队用途；升级不足提示改为“金星奖章不足”。
- 科技专项 2726 项通过，新增覆盖无通关购买、混合支付、旧账户、实际胜利奖励、战场使用、超大有效库存、购买及出售 API、支付持久化、重启和升级前战局存档防回滚。新增断言在修复前可复现购买奖章未计入余额。
- Web Release 与 Android ARM64 Compile 均零警告、零错误；Android 使用本机已有 `.codex/tools/dotnet-android`、`android-sdk`、`android-jdk`，未安装工具链。Chrome 70 兼容前端 54 文件生成通过。源码及兼容前端共 27 项浏览器检查通过，覆盖 1280×720、844×390、390×844，真实商店购买和升级点击、精确扣费、刷新持久化、对话框边界及图片加载；脚本错误／缺图 0，桌面和手机竖屏截图已目检。
- 清理前核 Git、绝对路径、重解析点与活动消费者，停止本轮测试宿主；清测试账户、战局、截图、缓存及隔离编译 3259 文件、278243464 字节，剩余 0／错误 0，Git 状态保持。凭据 `build/maintenance-20261101/campaign-shop-stars.json`。源码测试程序保留；其他玩家服务、账户、存档、Mod、原始输入及同期修改保持。
- 干净预览：<http://127.0.0.1:57942>，PID 30744，13 份必要运行文件；HTTP 200、95 场景、`hasSave=false`。运行目录为 `build/r-campaign-shop-stars-preview-20261101`，预览不含测试账户。

专项入口：`dotnet run --project rewrite/Ew4.Verify/Ew4.Verify.csproj -c Release --artifacts-path build/campaign-shop-stars-20261101/artifacts -- E:/Workplace/game/o4mod --campaign-technology-only`。浏览器源码为 `tools/verify_campaign_shop_stars.cjs`，只用于独立测试服务，不应指向玩家服务。
