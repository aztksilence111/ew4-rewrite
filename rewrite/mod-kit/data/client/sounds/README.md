# 音效与绑定

此目录为短音效，如枪声、炮声等。覆盖路径`client/sounds/<原文件名>`；保留编码兼容性和文件名，不只改扩展名。

播放由`effects.json.sounds`白名单及timelines事件的sound字段共同决定。新增文件还需写入列表、绑定时间轴；只复制wav不会自动播放。原资源导入将wav/ogg转存到sounds，mp3转存到music。

执行入口`effects.js`的`Effects.sound()`，音量取当前设置。浏览器可能需要首次用户操作才允许声音；核对开火/命中时序，不因静音就修改伤害或AI逻辑。

界面默认使用原版 `sfx_click.wav`（点击）、`sfx_cancel.wav`（返回／关闭）、`sfx_select.wav`（选择）及 `sfx_pop.wav`（弹窗），原布局的 `sound` 属性优先，空字符串表示静音。将领提示的 `sfx_lvup2.wav` 同样保留。自定义按钮可用 `data-ui-sound` 覆盖；程序自动点击、禁用按钮和拖动不会触发通用点击音。音效与 BGM 分别取“音效”和“音乐”设置。
