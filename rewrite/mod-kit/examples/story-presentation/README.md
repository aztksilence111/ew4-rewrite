# 剧情播放器最小示例

需要支持 `storyPresentation` 的 API 3 引擎。本目录只保存作者源码，不附导出包。

启用本示例后进入第一个征服、选择法国：开场对话 → 原关卡对白 → 军议选项。选择“派遣使者”可演示胜利判定 → 局末剧情 → 结算窗口；另两个选项演示资源结果及恢复战局。金币不足时进攻选项禁用。

- `rewrite/extension.json`：注册剧情目录和事件。
- `rewrite/story/catalog.json`：复用背景、角色／分层、开局及胜败绑定。
- `opening.json`：多人对话、表情映射、跳动和进场动画。
- `council.json`：选择分支、资源条件和多个剧情结果。
- `events.json`：以完成／选择／结果为条件，调用剧情或执行事件。
- `ending.json`：局末演出。

当前复用游戏已有地图背景和方形将领头像，仅用于演示接口。正式演出可换成独立背景、透明全身立绘及表情拆分。将自有 PNG 放在 `rewrite/story/backgrounds/`、`rewrite/story/characters/`，修改目录引用即可；素材无需复制进每个关卡脚本。

作者从自己的副本打包，不在本目录维护自动生成的 `mod.json`：

```powershell
.\Ew4.ModTool.exe pack 'D:\mods\story-demo' --id=story-demo --name='剧情演示' --version=1.0
.\Ew4.ModTool.exe import 'D:\mods\story-demo'
```

内置内容使用相同文件布局，放在源码 `rewrite/data/rewrite/story/` 并从内置 `extension.json` 注册，Android 打包到 `assets/rewrite/story/`。详细字段与限制见[剧情接口](../../docs/porting/STORY_PRESENTATION.md)。
