# 背景音乐

`client/music.json` 配置 `schema:1`、`menu`、`battle` 和 `defeat`。三个入口均使用 `client/music/` 下的文件名，支持 MP3、OGG、WAV；`battle` 是 1 至 64 首的数组。禁止路径、空项和不存在的文件。

内置主菜单循环 `battle1.mp3`；每次进入或载入战局，从 `battle1.mp3` 至 `battle4.mp3` 随机选一首循环；失败结算循环 `defeat_music.mp3`，胜利保持当前曲。返回主菜单恢复主菜单曲。音乐音量独立于音效，设为 0 或应用进入后台时暂停，恢复后继续。网页首次播放需要一次用户点击或按键。

覆盖包可同时提供 `client/music.json` 和所需音频。导入旧 APK 时，原 MP3 映射到 `client/music/`，`global_data.xml` 中 `BGMusic` 映射到 `menu`；其他轨道使用标准名称，非标准战斗或失败曲通过音乐 JSON 配置。
