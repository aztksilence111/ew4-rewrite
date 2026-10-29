# 指挥部布局示例

API 3 覆盖源码，包含 `rewrite/extension.json` 和 `rewrite/headquarters.json`。要求宿主已包含headquartersPresentation接口，Android和Windows Web共用。

- 名册页：640×360画布、无筛选按钮、不显示空槽，仅保留返回、学院、商店和新增“管理”入口。
- 管理子页：独立按钮数量和位置，包含任务、成就、公主、标签，名册改为单排并放大卡片。
- 学院页：作者定义星区按钮、选中候选信息、勋章招募与刷新；未定义的徽章购买入口不会自动显示。
- 任命页未覆盖，自动使用原有界面。

按钮可继续增删，节点ID任意命名，通过actions绑定行为；布局不是宿主内置的简洁版预设。需要增加徽章招募时，新增一个按钮并绑定 `{"command":"recruitBadges"}` 即可。

把rewrite文件合并到自己的作者覆盖目录后pack/verify/import。已有extension.json应合并headquartersPresentation字段，保留自己的地图、技能和规则入口；不要直接覆盖。此目录仅保留源码及说明，不附测试账户或编译包。

完整字段见[作者指南](../../docs/porting/HEADQUARTERS_LAYOUT.md)。
