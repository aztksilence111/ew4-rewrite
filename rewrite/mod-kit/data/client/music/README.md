# 背景音乐

`client/music.json` 配置 `schema:1`，必填 `menu`、`battle`、`defeat`，可选 `victory`、`playlistMode`、`scenarios`。所有音乐入口均可使用 `client/music/` 下的单个文件名、最多 64 首的数组，或 `null` / `[]` 静音。支持 MP3、OGG、WAV；禁止路径、数组空项、空文件名和不存在的文件。配置作为完整文件覆盖，缺省可选入口才会继承。

内置主菜单循环 `battle1.mp3`；每次进入或载入战局，从 `battle1.mp3` 至 `battle4.mp3` 随机选一首循环；失败结算循环 `defeat_music.mp3`，未配置 `victory` 时胜利保持当前曲。返回主菜单恢复主菜单曲。音乐音量独立于音效，设为 0 或应用进入后台时暂停，恢复后继续。网页首次播放需要一次用户点击或按键。

`playlistMode` 默认 `loop`，保持旧版行为：战斗随机选曲，其他入口取首曲，所选曲单曲循环。设为 `sequence` 后，多首列表在自然结束时播放下一首，末尾回到开头；战斗仍随机选起点，其他入口从第一首开始。单首循环；普通回合更新不重选歌曲。

`scenarios` 是剧本 ID（不带 `.btl`）到对象的映射，对象可选 `battle`、`victory`、`defeat`，分别覆盖全局对应入口。键使用战局状态的实际 `scenario`，分支剧本须写其实际 ID。缺省继承全局值，显式 `null` / `[]` 静音。未配置的胜利曲保持当前战斗曲，不会因回合刷新而重播。

覆盖包可同时提供 `client/music.json` 和所需音频。导入旧 APK 时，原 MP3 映射到 `client/music/`，`global_data.xml` 中 `BGMusic` 映射到 `menu`；其他轨道使用标准名称，非标准战斗或失败曲通过音乐 JSON 配置。

实际可打包的 [自定义音乐与音效示例](../../../examples/custom-audio/README.md) 同时演示菜单列表、新增 BGM、独立关卡曲与胜利曲。音频和配置不改变战斗规则指纹；同一 Mod ID 且其他逻辑相同时，改声音不会另开逻辑存档空间。
