# AI 设定呈现示例

仅保留最小配置源码，不含安装包。需要含玩家 AI 设定功能的 rewrite 客户端，使用 Mod API 3。

将 `rewrite/` 配置合入自己的 Mod；已有 `extension.json` 时只合并 `aiControl` 入口，保留原有技能、地图等注册项。省略字段回退客户端默认值，不需要复制游戏贴图。

完整接口及行为约束见 [AI_CONTROL](../../docs/porting/AI_CONTROL.md)。确认键为文字按钮，`confirmText`、`confirmImage`、`confirmWidth`、`confirmHeight` 可修改。
