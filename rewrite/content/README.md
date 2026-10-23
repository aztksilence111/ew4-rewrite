# 本地测试内容包

## 通用六技能测试包（2026-10-23 重新导出）

导入本目录的 **`common-skills.ew4mod`**，标识 `common-skills-test`，版本 `1.0`。启用后新开第一征服、选择法国，找到带测试技能的一级重炮，可测试召集步兵、战术转移、范围炮击、群体急行军、毒弹和解毒。详细用途见 [通用技能示例](../mod-kit/examples/common-skills/README.md)。

本包从保留的安装副本导出，五项资源哈希及规则指纹不变，绑定当前 `fd09d5c0…` 基础资源。已通过独立目录导入、导出后重导和剧本校验；报告位于 `build/common-skills-export-20261023/checks.json`。原安装副本及玩家档案未修改。

## 图文事件测试包（2026-10-23 重封装）

导入本目录的 **`scenario-event-layout.ew4mod`**，版本 `1.1.0`，标识 `scenario-ui-fixture`。启用后新开第一征服、选择法国，即可测试图片、长正文与12个选项的独立滚动。最后一个选项要求至少999金币，开局禁用是预期行为。

此包使用当前 `fd09d5c0…` 基础资源指纹，只覆盖事件配置与图片，不附带旧版基础资源。旧 `reports/scenario-event-layout-20261021/scenario-event-layout.ew4mod` 绑定 `743fba82…`，保留为原始材料，不再作为当前导入入口。后续基础资源变化仍须重新封装；不要手改指纹。验证记录见 [重封装说明](../../reverse/SCENARIO_EVENT_PACKAGE_20261023.md)。

## 当前推荐：东亚测试征服

导入文件：**`eastasia-test-conquest.ew4mod`**。

在包含东亚内置底图的rewrite 0.2.0中，主菜单打开“Mod”，选择此文件导入并启用；然后进入“征服”，翻到第二页，选择“东亚 / 地图检查”，选法国开始。场景ID是`conquest7`。

包只附加测试BTL、剧本登记、城市名称覆盖表和征服菜单配置，复用内置东亚底图，所以只有约7.5KB。包含278处无归属城市、272处设施和两支原版占位部队，不是正式历史剧本或势力布局。

重新生成：在仓库根执行`dotnet run --project tools/EastAsiaMapVerify -- . --builtin`。工具写入`rewrite/local/eastasia-validation/builtin/`隔离档案，重新封装本文件；报告记录基础资源指纹、导入和启动检查，不操作5190用户预览。

## 旧完整覆盖包

`eastasia-map.ew4mod`及`eastasia/`是旧的完整地图覆盖包与生成源，曾用于内置整合之前的5190预览。旧包绑定旧基础资源指纹；当前版本优先使用上方测试征服包，不要把旧包或`.previous`当作新版导入入口。仅在重建完整覆盖内容时使用`tools/EastAsiaMapVerify`不带`--builtin`的模式；该模式使用`rewrite/local/eastasia-validation/overlay/`隔离档案。

正式地图资源位于`../data/original/eastasia.bin`、`eastasia.map.json`及`../data/client/maps/eastasia/`。不要直接修改已安装Mod的哈希目录；重新封装导入并启用后再开始新局。测试包依然按Mod标识和规则指纹隔离账户与战局。
