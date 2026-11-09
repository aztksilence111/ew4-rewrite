# 自定义音乐与音效

此示例只含两份配置，引用游戏已有音频，可直接打包体验；不复制音频或改动游戏逻辑。菜单依次播放 battle2 / battle3，战斗列表从随机位置开始顺序轮播，征服 conquest1 使用独立列表，胜利切换指定曲目。点击使用选择音，返回使用原点击音；映射只解析一次，不相互递归。

```powershell
.\Ew4.ModTool.exe pack "D:\mods\custom-audio" --id=custom-audio --name="自定义音乐音效" --version=1.0
```

将本例 `client/` 合并到自己的覆盖包，再在 Mod 管理中导入并启用。已有同名配置时按字段合并后保留一份完整 JSON；每份配置在资源层都是整文件替换。导出包包含所提供的音频与配置，停用 Mod 后恢复内置声音。

加入自己的素材：

1. 将 BGM 放入 `client/music/`，例如 `my-battle.ogg`，再加入 `music.json` 的 `battle` 数组。可以保留内置曲目，实现新增，也可以删掉旧引用，实现替换。
2. 将短音效放入 `client/sounds/`，例如 `my-click.wav`，将 `audio.json` 的 `sounds.sfx_click.wav` 改为该文件名。不必复制 `effects.json` 或手动添加播放白名单。
3. 其他可替换入口包括 `sfx_cancel.wav`（返回/关闭）、`sfx_select.wav`（选择）、`sfx_pop.wav`（弹窗）以及移动、招募、开火等现有音效。值设为 `""` 可静音该入口。
4. 新音效可用 `"mod.alert": "my-alert.ogg"` 命名，在自定义按钮的 `data-ui-sound="mod.alert"` 或特效时间轴 `sound` 中引用；只有配置不会自动触发播放。

支持真实编码为 MP3、OGG、WAV 的单文件资源，引用不带目录，大小写须完全一致。跨端优先 MP3 BGM、PCM WAV 短音效；具体 OGG 编码取决于系统解码器。无需修改 `rewrite/extension.json`。接口沿用当前 API 3 资源包，新增字段须使用包含此接口的 R 版；旧版本不能识别新字段。

完整格式见 [音乐配置](../../data/client/music/README.md) 与 [音效配置](../../data/client/sounds/README.md)。
