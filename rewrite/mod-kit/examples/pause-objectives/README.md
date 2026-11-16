# 暂停目标栏 UI

本示例配置栏宽、行距、材质色、分区顺序与一个由事件状态驱动的阶段目标；胜负条件及星级继续读取当前剧本真实规则。需要包含阶段目标增补的宿主。

示例阶段不伪造夺城或援军，作者应替换为自己的真实地点，在对应事件中写入`scenario/bridge.secured=1`。可用`visibleWhen`按阶段／玩家国家显示，`completeWhen`显示完成；不设置完成条件则为纯提示。未配置阶段的旧Mod保持原界面。

将 `rewrite/` 合入自己的 Mod 工作目录；已有 `extension.json` 时只合并入口，保留其他注册项，再沿用 ModTool pack/import 流程。详见 [PAUSE_OBJECTIVES](../../docs/porting/PAUSE_OBJECTIVES.md)。

暂停窗口始终保持屏幕中心；左栏自动避让，长内容使用纵向滚动条。战役、征服与多人分别读取实际规则。
