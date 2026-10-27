# 东亚游戏底图（rewrite）

2026-10-27 [海岸修正](COAST_CORRECTION_20261027.md)已入5585三图检阅：3倍抗锯齿、暗内岸／浅外滩及河口色带衔接，原地块／建筑／桥渡规则保持。[实际游戏前后对照](coast-correction-detail.jpg)、[当前全图](eastasia_full_preview.jpg)。此前核查指出的问题与本次修正分开记录，当前以修正说明为准。

2026-10-21 地理文字层调整见 [LETTERING_20261021.md](LETTERING_20261021.md)：陆地与海域按对应区域铺字，中文使用 rewrite 包内字体，英文恢复原版地图字形；已校正祁连、外兴安岭、太白山脉及南海、东海位置，补中英文濑户内海，修正 `of` 字形比例。浪花 723→289，镜头限制按实际底图覆盖范围收紧；地形规则及城市设施保持原样。

2026-10-20。沿用已审阅的地理范围，158×136网格、21488格、278处城市与低等级据点。此前275城ID与位置保留；本轮补西昆仑山口的塔什库尔干1级据点、越南顺化以北的河静2级城市。按后续红圈在西侧两片山地之间另补瓦罕走廊的萨尔哈德1级据点，塔什库尔干保留。此前新增济州、顺化、岘港、日土、改则、尼玛、曲麻莱、若羌。朝鲜半岛及济州合计15处，库页岛保留丰原及亚历山德罗夫斯克。城市不带固定势力颜色。

## 交付

2026-10-20目录整理：通用Mod管理构建与测试产物移到`../../rewrite/local/mod-management/`；5190兼容预览的宿主、基础资源快照、Mod与存档整体移到`../../rewrite/local/eastasia-preview/`，继续保留原进度。通用Mod删除和识别代码一直在rewrite正式项目中，不依赖此地图目录。新路径用途见`../../rewrite/RUNTIME_DIRECTORIES.md`。

**当前版本测试征服请导入`../../rewrite/content/eastasia-test-conquest.ew4mod`**，启用后在征服第二页选择“东亚 / 地图检查”。这份小包复用0.2.0内置底图，只添加测试场景与菜单；旧`eastasia-map.ew4mod`绑定旧基础资源，仅供旧预览留档。重新生成当前测试包：`dotnet run --project tools/EastAsiaMapVerify -- . --builtin`，使用`rewrite/local/eastasia-validation/builtin/`隔离目录，不再往地图目录写宿主或存档。

2026-10-20 发布整合：正式运行资源已迁入 `../../rewrite/data/`，与欧洲、美洲同级。`original/eastasia.bin`、地图/城市目录及 `client/maps/eastasia/` 随 APK 和 Web 包一起发布，不新增正式战役或征服。`builtin-integration.json` 记录整合校验。下列 `rewrite/content/eastasia` 路径为本地可再生成的调试产物，不再在仓库重复保存；旧 v1–v5 记录从当前仓库移除，历史可通过 Git 查询。

重建顺序：`tools/build_eastasia_runtime.py --render-only`，完成地形/引擎检查后执行 `tools/integrate_eastasia_builtin.py`。制作配置继续保存在本目录，正式底图资源以 `rewrite/data` 为准。

- `../../rewrite/content/eastasia-map.ew4mod`：内置整合前的完整地图覆盖包，保留旧预览留档；当前版本请用上方`eastasia-test-conquest.ew4mod`。
- `../../rewrite/content/eastasia/original/eastasia.bin`：与原版地图同结构的YSAE/1地块数据，每格12字节。
- `../../rewrite/content/eastasia/eastasia.map.json`：通过实际引擎导入后导出的可编辑现代地图，含地形、河流、城市等级及经济设施，不含政治归属或预设管辖区。
- `../../rewrite/content/eastasia/`：可重新封装的资源目录；底图使用PNG分块及原有地形精灵。
- `../city_plan/conquest_v2/eastasia_overview_small.jpg`：原267城的小体积城市概览；本轮新增城见`map_additions.json`及游戏预览。
- `../city_plan/conquest_v2/india_cities_small.jpg`：印度新增城市局部。
- `eastasia_game_base_small.jpg`：实际游戏地形底图缩略图，不烘焙城市文字。
- `eastasia_terrain_overview.jpg`：约225KiB的地形概览，便于直接查看。

管辖区必须由具体征服剧本的当时归属和细微边界决定；这份底图不预设行政区、省界或跨年份固定国界。2026-10-20已配置通用城市等级和生产设施，正式剧本可覆盖；尚未部署民国军队、将领、各势力初始资源或胜利目标。

## 城市与设施初始配置

278处城市分为1级23处、2级129处、3级82处、4级35处、6级6处、7级3处。上海、东京、大阪开局7级；北京、天津、南京、武汉、广州、加尔各答6级。新增济州及高原、新疆、瓦罕据点1级，顺化、岘港、河静2级。原版欧洲开局最高7级、美洲6级；沿用其特殊初始等级规则，等级不等同于首都身份，不含首都专属收入。

272处独立设施：工厂56、马厩47、港口50、农田119。菲律宾工厂、马厩和两处农田分散在吕宋岛中部与南部空地，两城原有港口保留。全图陆上设施优先填充城市之间的空隙，并在东北、西北、海南和印缅等稀疏区域新增27处补给、修造和农业节点；日本新潟/松本及印度占西/赖布尔再补2工厂、2马厩。工厂重点覆盖工业、矿冶和兵工中心，马厩偏向北方/西北马源地区；农田偏向主要平原、盆地、河谷和绿洲。苏联、日本、朝鲜半岛及殖民地区域同样配置。这是参考民国时期经济地理的通用游戏初始配置，不是某一年的精确经济统计。

每格只承载一种建筑，设施与全部城市不重叠；陆上设施选择可从参照城市陆路到达的平地，优先与所有城市和既有设施拉开间距。港口仍选与外海连通且邻接平地的海格，距参照城市1至3格，并要求可见岸线遮罩在格心确为海面，避免逻辑海格落在视觉陆地上。阿帕里港已移至邻近北侧海格，那霸港按截图由[122,98]移至左上相邻海格[121,97]，保留向右伸出的样式3。`anchor_city`仅说明选址参照，不预设行政或征服管辖关系。所有设施保持无主，原版建筑规则提供生产和招募能力；具体势力收益取决于剧本归属。

`settlement_plan.json`的`landPlacement`控制陆上选址半径和优先距离，单组`placements`或全局`facilityPlacements`可指定按城市来源名索引的`[列, 行]`落点；吕宋四处采用显式分散落点，海南农田也固定留在岛内。选址同时核验陆路可达，避免把设施放到隔海的邻岛。

港口使用原版四种朝向贴图，按邻岸陆地向量选择；样式1/2/3/4分别朝向北/东/西/南侧陆地，数量21/10/14/5。`facilities.json`保留`shore_cell`、`coast_vector`与`building_style`，便于复核；`portStyles`允许按实际岸线明确覆盖。

- `settlement_plan.json`：可编辑的城市等级覆盖和设施配置依据。
- `map_additions.json`：新增11城的独立地理源；`tools/eastasia_additions.py`仅追加城市，不重新排列已有城市。
- `cities.json`、`facilities.json`：实际落格及等级，资源目录`client/maps/eastasia/`保留相同副本。
- `settlements.json`：等级/设施数量和源配置哈希。
- `feedback-browser-verification.json`：本轮截图修正、桥梁删减及实际客户端检查；`infrastructure-browser-verification.json`为此前275城/69节点版本，`settlements-browser-verification.json`为更早237设施的历史记录。

仅调整等级/设施时运行`tools/build_eastasia_runtime.py --settlements-only`，随后运行下方引擎验证器重新导入并封装。新增城市须先运行`--render-only`更新城市落格及可见岸线，不重新排列原267城。

山地、丘陵、林地、沙漠及主要河流均写入实际地块；显示沿用原游戏地形素材，移动成本与兵种地形损耗按重写版现有规则生效。本轮补了川西、川中、南岭、大巴山、山东、朝鲜、日本、西伯利亚、缅甸和印度中东部等区域，并补恒河、亚穆纳河、戈达瓦里河、马哈纳迪河、鄂毕河、额尔齐斯河、叶尼塞河和勒拿河。地形按约42公里格距概化，不是精确高程或民国植被调查图。成都、华北、长江中下游、东北、恒河等主要低地保留平原。

地图PNG采用透明陆地与不透明水体/河流，保留原渲染顺序中的地形及剧本势力底色。5635个陆上地形精灵：山地1738、丘陵1098、林地2132、沙漠667；另有723个海浪前景，总计6358个地形精灵。本轮真实引擎7807项0错误；包3159848字节，详细指纹见`runtime-verification.json`。550建筑格的前序检查见`feedback-browser-verification.json`，地貌补标、五岳及清晰度的检查见`geography-browser-verification.json`，双语独立按钮与实际文字像素见`languages-browser-verification.json`；`cartography-browser-verification.json`保留上一版记录。选国界面暂沿用原版战略背景，确认进入后的游戏底图为东亚。

## 海洋波纹与英文地貌名

`cartography_plan.json`定义固定种子20261020、外海海格10%的波纹密度及71处自然地理标注。`tools/eastasia_cartography.py`沿用原版七种`wave_1`至`wave_7`贴图及欧洲/美洲的样式分布，以随机位移分散落在723格。只选与外海连通的海格；整幅波纹及5像素余量均在可见海面，避开建筑、地貌名和其他波纹，不为内陆湖泊添加海浪。

波纹写入BIN前景种类1及对应样式、位移，并在manifest中作为`seaForeground`显示，不修改海陆、移动成本、城市、设施或桥梁。原版普通/高清图集保持原素材。随机结果可重复，当前分布及数量见`cartography.json`。东亚背景分块增加2像素重叠，避免非整数缩放时海面出现分块细线。

英文标注包含39个陆地区域、21个湖泊、11个海域。新增北部湾（GULF OF TONKIN）、台湾海峡（TAIWAN STRAIT）、外兴安岭（STANOVOY RANGE）、小兴安岭（LESSER KHINGAN），并加强大兴安岭、太行、秦岭、祁连、横断等山系。湖泊包括贝加尔、巴尔喀什、伊塞克、阿拉湖、斋桑、乌布苏、库苏古尔、吉尔吉斯、哈拉乌苏、呼伦、兴凯、青海、博斯腾、纳木错、色林错、鄱阳、洞庭、太湖、洪泽、恰内及田吉兹湖。仅表示自然地理，不表示固定国家或辖区。

标注使用原版`maptext`字形及文字层，追加东亚组合条目，保留原文字库全部条目、变换和图集。东亚地理文字不透明度提高至72%，原版未设置`opacity`的地图仍使用60/255。城市建筑、部队与城市名在地理文字上层。陆地和湖泊用原版衬线大写字形，海域用原版白色斜体；湖名允许沿邻岸显示。长山系通过`length`设置字距跨度，保持字形比例，沿走向展开；按旋转后的完整外框避开建筑、城市名和其他地理标注。完整坐标和外框见`geographic_labels.json`。普通缩略底图未烘焙这些文字，实际游戏实时显示。

五岳配置见`mountain_landmarks.json`，实际格位见`mountain_landmark_cells.json`。在约42公里格距下取最近的空闲可见陆地，华山避开潼关城市格，落在南侧相邻格；泰山、华山、嵩山、南岳衡山新增4个真实山地格，北岳恒山保留原有山地。五处统一使用中山外观，不以五岳的文化地位套用喜马拉雅级别的高山。城市、设施、桥渡及河流阻行标记均保留；重建时先保留设施，再落实山地。

设置中的“城市名称”和“地理名称”为两个独立按钮，各自切换中文/English并保存到本机设定；旧城市语言偏好保留，新增地理语言默认English。71处地理名中文对照在`cartography_plan.json`的`chineseNames`中，中文沿用地理位置、角度、字高和跨度，地理层语言计入静态缓存键，切换立即重绘。缺少中文地理名的原版地图回退原文字。

按2026-10-20后续字体反馈，菜单、城市与中文地理文字改为共享宋体风格字族（`Ew4OriginalUI.fontFamily`）：Windows用SimSun，其他系统依次回退Songti SC、Noto Serif CJK SC与系统serif。沿用本机字体，不向资源包复制系统字体；原字体资产保留。城市仍白字黑描边，暂停界面四个蓝底按钮明确白字。`languages-browser-verification.json`已扩展为27检查，含桌面1280×720与竖屏405×829的同字族、按钮实际白色像素及边框容纳检查；图片`game-pause-font-1280x720.png`、`game-pause-font-405x829.png`。这里只验证Windows浏览器，不表示Android字体实测。

278城双语显示源`city_names.json`，manifest按实际格位保存`cityNames`，避免中国海州Haizhou与朝鲜海州Haeju混用；底图、城市ID及征服名称表不变。只有当前名称仍等于底图基础名称时才使用底图译名；征服覆盖名称优先，未提供对应翻译时原样显示。`tools/verify_eastasia_languages.cjs`在5190隔离预览中验证两种地理文字的实际像素、四种独立组合、278城语言、名称覆盖/同名城、旧偏好迁移、刷新保存和手机布局。

调整波纹、标注或附近设施后使用`--render-only`重建，再运行地形与引擎验证器封装。`--settlements-only`不会重新安排既有波纹和地貌名。`verify_eastasia_terrain.py`同时验证波纹与BIN一致、七种样式齐全、整幅海面/间距/建筑避让、标注不互相重叠及原版文字库完整保留。

## 河流与桥梁

河流同时生成贴图与4528条双向一致的有向河边标记，主干/支流河心为6/4像素，采用原版风格浅岸边、细暗轮廓和抗锯齿。湖内不绘中线，河口沿现有细水道续接开阔海面、逐步淡化岸边，保留实际三角洲分汊。同名河段自动连接不超过12像素的源数据接缝；鸭绿江与图们江等不同水系源头保持分开。下淮河从原有曲折段末端续接洪泽湖，再经三河、高邮湖、邵伯湖接入长江下泄，避免旧补线重复交叉；没有增加现代直接入海水道。钱塘江采用源数据中的富春江弯道，删除旧直线补绘；补充嫩江、大同江及日本部分河段见`river_additions.geojson`。马哈纳迪河希拉库德段中线连接受现代水库影响留下的断口。详见[本轮修正](../../rewrite/EASTASIA_WATER_20261025.md)。引擎`Movement.EntryCost`规定无桥过河消耗单位完整移动预算；实测步兵无桥15、有桥按普通陆地3计算，两个方向均已核验。

`bridge_plan.json`定义54处通行节点：29处桥梁及25处渡口路线；黄河13处、长江及金沙江15处、其他河流26处，全部落格，无省略。按截图减少黄河10处、长江中游5处过密渡口，保留关键城市过河点和较长无桥河段。删除项为循化、青城、景泰、青铜峡、磴口、托克托、三门峡、孟津、开封、台前，以及巴东、枝江、沙市、石首、黄石；这些位置同时清除通桥贴图及双向通桥标记，河流阻行边保留。长江、黄河节点限定在对应干流的实际河边，避免吸附到邻近支流。`bridges.json`保存实际河边、朝向及位置偏差，`bridge-verification.json`记录落格结果。

沿用原版木桥/石桥/铁桥贴图，BTL同步写入双向通桥标记，现代地图导出亦保留。`kind=ferry`以木质通桥外观概化历史渡河路线，不表示当时存在实体大桥；因此武汉、南京使用渡口节点，不添加现代长江大桥。约42公里网格下的桥梁和渡口都按游戏通桥成本过河。含1930年代设施，`year`和`kind`供正式剧本按年份删改；`year=null`表示未指定始用年份。

调整河道或桥梁后使用`tools/build_eastasia_runtime.py --render-only`重建贴图、河边和桥梁，再运行引擎验证器封装；`--settlements-only`仅刷新设施及当前河边对应的桥梁记录，不重画桥梁。

## 剧本城市名称

底图只保存北京、沈阳、长春、海州等基础名称，不含斜杠或括号。城市ID及坐标不变；旧来源名仅用于查找规划记录。征服条目可通过`original/def_battlelist.xml`的`citynames="conquest7.names.xml"`指定同目录名称表，缺省或未覆盖条目沿用底图名称。

`original/conquest7.names.xml`当前为空表`<citynames />`。例如要在特定剧本中使用历史名称：

```xml
<citynames>
  <city id="10084" name="北平" />
  <city id="10060" name="奉天" />
  <city id="10211" name="新京" />
</citynames>
```

ID见`cities.json`的`city_id`，可参考`conquest_names.example.xml`。删除覆盖项即恢复底图名；修改资源目录后须重新封装/导入并启动剧本，不是运行中热更新。加载器拒绝未知/重复ID、空名称和跨目录引用；原剧本未指定名称表时行为不变。

补入燕山主脊与山麓、广东北部/东部/西部丘陵和广西山地，并补越城岭、都庞岭、大瑶山、九万大山、大明山、云开、云雾、罗浮/九连、莲花和凤凰山地。珠三角、潮汕、雷州低地及南宁盆地、桂平河谷保留低地。补五岳后山体外观分低山419格、中山795格、高山524格，另有丘陵1098格；按山系与主脊距离概化，并非实测高程。仅新增东亚专用图集精灵，原图集像素和精灵坐标保持不变；战斗与移动仍使用原有山地/丘陵类型。分类和生成方法见`relief.json`及`../../tools/eastasia_relief.py`。

征服第二页的“东亚地图检查”（conquest7）仅用于浏览与交互验证：278处城市全部无归属，原版法国/英国部队各一支放在北京/东京附近非城市格，只有部队所在单格有临时归属。原版旗帜不代表历史势力。正式剧本另行制作；检查场景不得作为历史布局模板。

## 验证与预览

- `generation.json`：地块重读、海陆与城市锚点、河流边双向一致性。
- `terrain-verification.json`：30个独立地理控制点，以及每个地形精灵与地块类型、格心的对应检查；核对高低山体尺寸顺序、原图集像素不变及50港口均在可见海面。
- `runtime-verification.json`：真实Mod导入/启用、96剧本注册和启动、278城、现代地图及会话存档往返、寻路、原版基线未变。
- `browser-verification.json`：浏览器实际渲染与选格、移动和保存读取的检查结果，生成后以该文件为准。
- 独立预览：`http://127.0.0.1:5190`，使用`../../rewrite/local/eastasia-preview/mods`和`saves`，宿主在同级`server`，基础资源快照在`data`。5184的用户对局不在此目录。

此前单独地图预览阶段未构建APK；本轮发布记录见仓库发布说明，Android实机验收仍未进行。独立资源包绑定rewrite基础资源指纹，基础资源升级后须重新封装或导入源目录。Natural Earth公开地理源缓存与地形区域定义沿用项目已有材料。小岛在约42公里格距下有概化，见`geographic_adjustments.json`；最大城市吸附偏移41.2公里。

## 重新生成

使用Codex缓存的Python 3.12运行`tools/build_city_plan.py`，再运行`tools/build_eastasia_runtime.py`。依赖目录为`C:/Users/WIN11/.codex/tools/ew4-inspect-deps`。然后：

```powershell
& 'C:\Users\WIN11\.codex\tools\dotnet-android\dotnet.exe' run --project tools\EastAsiaMapVerify -- E:\Workplace\game\o4mod
```

地形独立检查入口为`tools/verify_eastasia_terrain.py`。基础区域沿用`maps/terrain_regions.geojson`，重写版补充区域独立保存在本目录`terrain_additions.geojson`，不回写旧V5素材。

验证器封装资源包并仅激活`rewrite/local/eastasia-validation/`中的隔离配置。正式剧本制作前还需评估大规模军队的AI回合耗时；两支检查部队不能证明完整征服的长期性能。
