# 暂停目标栏 UI

本示例只配置栏宽、行距、材质色和分区顺序；胜负条件及星级继续读取当前剧本真实规则。需要包含暂停目标栏功能的宿主。

将 `rewrite/` 合入自己的 Mod 工作目录；已有 `extension.json` 时只合并入口，保留其他注册项，再沿用 ModTool pack/import 流程。详见 [PAUSE_OBJECTIVES](../../docs/porting/PAUSE_OBJECTIVES.md)。

暂停窗口始终保持屏幕中心；左栏自动避让，长内容使用纵向滚动条。战役、征服与多人分别读取实际规则。
