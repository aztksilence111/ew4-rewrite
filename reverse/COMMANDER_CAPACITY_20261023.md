# 军事学院容量提示及 Mod 上限接口

2026-10-23，本地实现及验证完成；未生成新APK或发布。原图及摘要见[证据](references/commander_capacity_user_20261023/README.md)。

## 定位与实现

`rewrite/Ew4.Web/wwwroot/menus.js` 购买按钮在发送请求前清空 `this.offer`。后端达到各星级区20人的上限后拒绝购买，界面没有重建按钮，再次点击旧按钮就读取空对象的 `id`。后端原提示为“该星区已满（20位将领）”。

现在按钮捕获当前将领和分区，在成功后清除选择；失败保留选择并重建按钮，所有货币按钮共享请求期间的提交保护。两端共用 `ClientHeadquarters.RequireCommanderSlot`，拒绝文案统一为“该星级区将领已达上限，无法购买”，账户、余额、候选和版本不因拒绝而改变。

## Mod 接口

复用 API 2 的 `metagame` 文件，新增 `commanderSectionCapacities` 字典。例如：

```json
{
  "schema": 1,
  "commanderSectionCapacities": { "0": 30, "1": 25, "2": 40 }
}
```

键0/1/2对应一星/二星/三星区域，按将领原有总星级1～3、4～6、7及以上划分。省略的区默认20；数量为非负32位整数，0禁止新增。军事学院两种货币购买和公主领取共用限制，三区独立；不改变候选数、刷新次数或战场派遣名额。

`/api/headquarters` 新增按一星到三星排列的 `sectionCapacities`；旧 `sectionCapacity:20` 保留兼容。前端采用新数组显示空槽和计数，已有超额将领继续显示，计数仍如实使用配置上限。配置进入现有扩展逻辑指纹，导入/切换/账户版本隔离遵循既有规则。

完整注册方法和约束见[作者指南](../rewrite/mod-kit/docs/porting/METAGAME_AND_BRIEFING.md)，可打包示例见 `rewrite/mod-kit/examples/feedback-config`。

## 验证

- Headquarters专项115项：默认各20、三区边界、满额另一分区仍可购、两种货币及公主限制、准确文案、失败字节不变；Mod各0/1/25边界、默认回退、配置指纹及重启。
- 配置专项114项：含未知区、负数、null、小数、字符串数量拒绝；既有商店/刷新/说明配置回归。
- 客户端2747项、海军派遣24项通过。
- 浏览器52项：三区真实购至20、连续失败、两种货币同时点击只发一次请求、失败后另一分区成功；桌面1280×720、手机横屏844×390和竖屏390×844提示完整，0脚本异常/0缺图。
- 示例实际打包校验、导入、启用成功；真实API返回30/25/40，界面全选95及各区30/25/40槽验证通过。
- Web隔离输出编译、Android ARM64 Compile均零警告/零错误；Android使用 `.codex/tools/dotnet-android` 及项目专用SDK/JDK。JS语法及Chrome70兼容构建通过。

证据：`build/commander-capacity-20261023/browser/report.json` 及四张截图；回归脚本 `tools/verify_commander_capacity.cjs` 须针对独立测试档案运行（会购买测试将领），检测到 `commander-capacity-test` 包时同时检查真实Mod加载。构建输出在同目录 `web/`，独立测试账户及示例包在同目录，未操作玩家档案或其他服务。

预览地址 `http://127.0.0.1:5481` 使用同目录 `preview-profile/` 空白独立档案。未进行真机运行验收。
