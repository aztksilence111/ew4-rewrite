# 前端显示资源

- `manifest.json`：图集坐标、兵种动作、地图块、将领名称、界面布局和文字索引；历史字体度量不再控制当前文字绘制。
- `images/`：完整PNG图集。`ui/`：由图集裁剪出的按钮、头像、旗帜、背景等。
- `animations/`：BILE转换后的动画层级和关键帧；`effects.json`：攻击、移动、粒子、声音绑定。
- `maps/`：分块地图底图；其地形及剧本数据仍位于 original。
- `fonts/`：当前文字使用的离线TTF及许可；不沿用Mod位图字库样式。
- `music/`、`sounds/`：音频资源；`music.json` 配置 BGM 列表及关卡/胜负配乐，`audio.json` 配置点击和其他音效映射，见[示例](../../examples/custom-audio/README.md)。`import-baseline.json`：转换器判断原资源是否变化的基础指纹，请勿修改。

同尺寸同布局PNG可覆盖实际读取的client文件；主菜单/选择页背景先确认布局引用纹理，不能假定读取派生ui背景。改源图集布局应重导入，更新ui裁剪图与manifest。只改images可能仍显示旧ui。动画引用必须有效，不可循环嵌套。

完整移植需同时处理视觉、布局、点击范围、交互和后端命令。当前只有前端已读取的布局字段生效，并非通用原布局解释器；不能只套原版按钮后声称UI已兼容。具体步骤见[移植指南](../../docs/porting/README.md)。

原包 HD 资源优先于 SD；同时存在时请修改实际使用的 HD 图集及配套索引。地图 PVR 不可直接放到 maps 使用，应提供配套原索引，由导入器生成PNG块。

## manifest.json字段导航

| 节点 | 实际例子/字段 | 说明 |
| --- | --- | --- |
| `atlases` | `commanders1_hd["Napoleon.png"]`：x/y/w/h/refx/refy | [完整图集](images/README.md)的裁剪与锚点 |
| `profiles` | `ew4.unit.fra.militia.0` | name/kind/unitIndex/grade/scale/card/cardMarker/stats/description/library/x/y/motions，见[动画](animations/README.md) |
| `commanders` | 数字ID字符串到资源名称 | 不是将领实际能力；后端能力在catalog |
| `fonts` | 历史font_text/font_city元数据 | 不再控制文字样式，见[字体](fonts/README.md) |
| `menuTextures` | 原纹理名到file/ratio | MenuArt按布局引用读取PNG与HD比例 |
| `maps` | europe、america | width/height/pixelRatio/tiles/overlays/labels，见[地图](maps/README.md) |
| `strings` | name_Napoleon、desc_Militia等 | 显示文字；原资源导入从INI重建 |
| `strategic` | campaign1_01 | 原battle记录字段，决定介绍/战略标记 |
| `layouts` | form_selcampaign等 | attributes/children布局树；不是任意HTML，只有前端已读取的字段有效 |
| `buildings/capitals` | 建筑类型、首都ID | 原定义到显示元数据 |
| `ui.labels` | 历史campaign_label等缓存 | 新转换器清空旧文字缓存；当前以文本和统一字体绘制 |
| `tutorials` | tutorials1等 | 原教程commands/sha256；编辑需理解Tutorial解释流程 |
| `mapDrawing` | borders等 | 原边界显示元数据，不是国家归属或辖区配置 |

`profiles[id].stats`用于界面显示，不是后端战斗唯一来源；实际规则在legacy/catalog.json。修改生命/攻击时优先改原XML重导入，避免面板与实际值不同。

## 图片使用路径

界面静态按钮/头像多从[ui裁剪图](ui/README.md)取，战场和动画多从images完整图集取。两者都可能显示同一主题；只改一边可能出现菜单新头像、战场旧图。`app.uiImage()/sprite()`和`Menus.portrait()`的搜索顺序见[函数索引](../../docs/SOURCE_INDEX.md)。

manifest是完整文件替换。仅改一张PNG无需附带manifest；改坐标、动作名或地图块需复制完整manifest再修改。不得用只含一个profiles条目的片段覆盖；改历史字形度量不会改变当前统一字体。
