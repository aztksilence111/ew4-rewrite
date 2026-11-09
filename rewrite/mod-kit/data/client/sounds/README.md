# 音效与绑定

此目录为短音效，如枪声、炮声等。覆盖路径`client/sounds/<原文件名>`；保留编码兼容性和文件名，不只改扩展名。

推荐使用 `client/audio.json` 映射现有入口或新增命名音效，无需修改完整 `effects.json`：

```json
{
  "schema": 1,
  "sounds": {
    "sfx_click.wav": "my-click.ogg",
    "sfx_cancel.wav": "my-back.wav",
    "sfx_pop.wav": "",
    "mod.alert": "my-alert.mp3"
  }
}
```

目标文件放在 `client/sounds/`，引用只写文件名，支持 MP3、OGG、WAV。非空目标必须存在；空字符串表示静音。键可为现有音效文件名或新别名，不带路径。目标自动加入允许播放列表，点击预载也自动加载替换后的文件；映射只解析一层，目标名与另一个键相同时仍直接播放目标文件。其他未映射声音保持原有绑定。

原有播放由 `effects.json.sounds` 白名单及 timelines 事件的 sound 字段共同决定。不使用 `audio.json` 的新增文件仍需写入原列表；使用映射的新别名可直接作为时间轴 `sound` 或按钮 `data-ui-sound` 的值。只复制音频或定义别名不会自动播放。旧资源导入将 wav/ogg 转存到 sounds，mp3 转存到 music；覆盖包可以明确把短 MP3 放进 `client/sounds/`。

执行入口`effects.js`的`Effects.sound()`，音量取当前设置。浏览器可能需要首次用户操作才允许声音；核对开火/命中时序，不因静音就修改伤害或AI逻辑。

界面默认使用原版 `sfx_click.wav`（点击）、`sfx_cancel.wav`（返回／关闭）、`sfx_select.wav`（选择）及 `sfx_pop.wav`（弹窗），原布局的 `sound` 属性优先，空字符串表示静音。将领提示的 `sfx_lvup2.wav` 同样保留。自定义按钮可用 `data-ui-sound` 覆盖；程序自动点击、禁用按钮和拖动不会触发通用点击音。音效与 BGM 分别取“音效”和“音乐”设置。

上述原布局、通用按钮和战斗时间轴最后都经过音效映射，因此无需逐个修改布局。完整配置会随 Mod 导入/导出并在切换时重新加载，停用即恢复内置配置。见 [最小示例](../../../examples/custom-audio/README.md)。
