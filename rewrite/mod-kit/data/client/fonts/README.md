# 稳定文字与字体

当前rewrite使用十个内置字体族，发行文件拆为解码后不超过8 MiB的WOFF2分片，不读取系统字体。中文界面使用文鼎楷体 AR PL UKai CN，英文和普通数字使用 Liberation Serif Bold，战斗跳字使用 Liberation Sans Bold Italic；后两者的修改分片已按OFL更改内部族名。它们是对照原图选择的替代字体，未确认是原版源字体。霞鹜文楷、Noto Serif SC及Jigmo/Unifont保留为缺字回退。

启动只等待常用主字库；失败时依次尝试内置文楷、宋体、Unifont常用分片。其他文字按需加载，解码串行执行，载入后重绘画布；未就绪的字符暂用内置替代字形。错误条显示失败文件名并可原地重试。全部常用字库或基础资源失败时保持入口禁用，避免进入未初始化的指挥部。`coverage.json`包含每个分片的`ranges/bootstrap/decodedBytes`，完整字体源保存在本地字体源归档，不随包重复发行。

## 修改入口

战斗伤害数字使用独立的 `EW4 Combat` 内置轮廓字体，黑描边、白色普通伤害和红色暴击。地图图层文字不属于界面字体统一范围：原英文标注恢复图层美术，既有中文地图标注保留旧文楷/宋体链，使用独立的 `mapFontFamily/prepareMapText`；不得跟随界面换字体。动态城市名仍属于界面文字。

| 目标 | 当前入口 |
| --- | --- |
| 将领名、按钮文字、说明 | 原`stringtable_cn.ini`和相关XML，修改后重导入 |
| 城市原名 | `def_areaname.xml`及剧本地点引用，修改后重导入 |
| 城市中英文对应 | `frontend/city-names.json`，自创名需作者补译名 |
| 字号、颜色、描边、对齐 | `original-ui.js`的`TextRenderer.layout/element`及调用点；暂无完整Mod文字样式配置接口 |
| 标题logo等文字美术 | 配套图集和布局引用，作为图片替换 |

`TextRenderer.prepare`加载常用离线字体，`layout/element`按字号角色绘制；城市白填充黑描边。新增直接绘制文字的画布需调用`Ew4OriginalUI.prepareText(text)`，并在`ew4fontsloaded`事件后清除文字缓存；测试可等待该Promise后核对生僻字。历史`manifest.fonts`的`height/glyphs/preserveColor`、FNT/PNG字形及`ui.labels`预渲染字不再决定当前文本样式。版本3导入器不导入这些字库样式并清空旧文字缓存；旧产物保留元数据也不代表仍使用它。

## 控件文字适配（2026-10-20）

源码位置为`rewrite/Ew4.Web/wwwroot/`。调整下面的入口时保留同一组内置字体族，按控件用途分别指定颜色和尺寸：

| 范围 | 文件与函数 | 当前约定 |
| --- | --- | --- |
| 共用绘制 | `original-ui.js::TextRenderer.element(text,font,color,width,align,options)` | `width`是包含留白的逻辑像素总宽；不是CSS变换后的屏幕像素 |
| 单行长名称 | `TextRenderer.layout/element`的第六参数 | `{singleLine:true,size:11.5}`按可用宽度重新绘制较小字号，不强制换行或横向拉扁；`size`可省略以使用字号角色 |
| 限高控件 | 同上 | `{singleLine:true,maxHeight:18}`同时适配总高度；多行说明保持换行，不使用单行选项 |
| 普通HTML文字 | `TextRenderer.forElement/paintTree` | 读取控件CSS的`font-size/color/text-align`，按钮为图标和相邻内容留出空间；不要再统一替换为白色标题字 |
| 入口及国家列表 | `app.js::labelImage/originalLabels` | 主菜单14px浅色，浅底国家条11.5px深色、84px宽；国家列表和“选择国家”标题使用不同路径 |
| 原XML文字控件 | `native-layout.js::Layout.write` | 读取当前Mod布局的`font/color/textcolor/w/h/align/breakwords`；无显式文字对齐的按钮默认居中，保留显式左/右对齐 |
| 战役条目和将领卡 | `menus.js::Menus.lettering`及调用处 | 有限宽度的标题和名称用单行适配；说明正文继续多行显示 |
| 画布缩放 | `original-ui.css`的`.original-text canvas` | 宽高保持比例；不要仅压缩宽度而固定高度 |

例如在源码调用`this.api.text('较长的将领名称','font_text_2_cn_hd','#ebe6e1',74,'center',{singleLine:true})`。此处是开发接口，不是可直接写入Mod清单的新字段。CSS中的字号使用568×320逻辑坐标，外层统一缩放到窗口；不要拿截图中的物理像素直接写入字号。

前端源码修改后刷新Web预览即可；旧APK不会自动更新。可在独立5185测试服务运行`tools/verify_font_regression.cjs`，覆盖字体加载、国家条目边界、商店文字居中和城市填充/描边；另用`tools/verify_sidebar_restoration.cjs`验证战役侧栏。不要把自动化指向玩家正在使用的5184。

不要改`preserveColor`或重画FNT/PNG来修复当前描边。表现包字体角色配置尚在规划；替换内置WOFF2需同时更新覆盖索引、缓存版本和许可，不是已经验证的任意字体切换功能。

## 验证与许可

重导入并启用后，检查中文/英文城市名、长将领名、商店、技能说明和资源数字，核对字形、换行、点击区、填充和描边。原版与Mod往返切换检查内容来源。分步操作见[移植指南](../../../docs/porting/README.md)。

## 字库文件与加载顺序

| 文件 | 前端字体族 | 用途与来源 |
| --- | --- | --- |
| `parts/ARPLUKaiCN-*.woff2` | `EW4 UI Kai` | 中文主字体，文鼎楷体CN；来源及摘要见coverage.json |
| `parts/LiberationSerif-Bold-*.woff2` | `EW4 UI Latin` | 英文及普通数字，粗衬线；内部族名已更改 |
| `parts/LiberationSans-BoldItalic-*.woff2` | `EW4 Combat` | 战斗数字，粗黑斜体；内部族名已更改 |
| `parts/LXGWWenKai-Regular-*.woff2` | `EW4 Kai` | 后备及既有中文地图标注，霞鹜文楷 Regular 1.530；https://github.com/lxgw/LxgwWenKai |
| `parts/NotoSerifSC-Regular-*.woff2` | `EW4 Serif` | 后备宋体，400字重；https://github.com/google/fonts/tree/main/ofl/notoserifsc |
| `parts/Jigmo-*.woff2` | `EW4 Han` | 基本平面汉字；Jigmo 20250912 |
| `parts/Jigmo2-*.woff2` | `EW4 Han Plane 2` | 第二平面汉字，如𠮷；https://kamichikoichi.github.io/jigmo/ |
| `parts/Jigmo3-*.woff2` | `EW4 Han Plane 3` | 第三平面扩展汉字，如𰀀；同上 |
| `parts/Unifont-*.woff2` | `EW4 Unicode` | 其他基本平面文字；GNU Unifont 17.0.05 |
| `parts/UnifontUpper-*.woff2` | `EW4 Unicode Supplement` | 其他补充平面文字；https://unifoundry.com/unifont/ |
| `coverage.json` | 不适用 | 字库cmap并集、文件哈希和来源；必须与字体一起发布 |

`original-ui.css`定义`--ew4-font-family`；`original-ui.js`依据覆盖索引按需创建带`unicodeRange`的`FontFace`，加载成功后加入`document.fonts`。同一字体族的分片范围不重叠。DOM与Canvas使用相同顺序，不使用Arial/宋体等系统字体别名，也不合成粗体。Android `MainActivity.LocalClient.ShouldInterceptRequest`以`font/woff2`提供同一文件。

UTF-8只是编码。实际覆盖以`coverage.json::ranges`为准；尚未分配的码位、私用区和任意未来新增Unicode字不保证覆盖。`displayText`按码点处理补充平面汉字，先做NFC规范化；缺字显示内置替代字符U+FFFD并在控制台记录码位，不悄悄切换系统字形。不要把UTF-16代理对切成两个字符。

## 重建与缺字排查

1. 在仓库`rewrite/local/font-sources-20261020`准备ukai.ttc（使用CN face 0）、LiberationSerif-Bold.ttf、LiberationSans-BoldItalic.ttf、UKai-LICENSE.txt、Liberation-LICENSE.txt，以及原有文楷/Noto/Jigmo/Unifont源文件。下载地址见`tools/build_bundled_fonts.py::main/recipes`；两份新增许可来自对应Debian包的copyright。原始字体仅用于构建，不随发行包重复分发。
2. 使用含FontTools和Brotli的Python运行`python tools/build_bundled_fonts.py`，生成常用分片及其他码点分片。工具核对各分片cmap及完整覆盖、单片解码上限，生成覆盖索引，并同步`data/runtime-content.json`字体项。
3. 运行`python tools/audit_bundled_fonts.py`核对字体哈希、原版和本地APK中的XML/INI字符；结果在`rewrite/reports/bundled-fonts-20261020/coverage-audit.json`。
4. 发现新Mod缺字时先记录控制台的`U+...`，检查来源是否为私用字符/损坏编码；再决定补字库。增加字库需同步`original-ui.css`和`original-ui.js::families/prepareFonts`；覆盖索引中的顺序必须一致。最后重跑审计及独立浏览器验证。

许可：文鼎楷体使用`UKai-LICENSE.txt`（Arphic Public License），Liberation修改分片使用`Liberation-LICENSE.txt`（SIL OFL 1.1）。霞鹜文楷使用`LXGWWenKai-OFL.txt`，来源为 https://github.com/lxgw/LxgwWenKai/releases/tag/v1.530 ；Noto使用`NotoSerifSC-OFL.txt`（两者均为 SIL OFL 1.1）；Jigmo使用`Jigmo-LICENSE.txt`（CC0 1.0）；GNU Unifont编译字体采用双许可，本发行选择SIL OFL 1.1，完整说明见`Unifont-LICENSE.txt`。保留所有许可。字库在Web与APK中均完整离线内置，不依赖联网下载。
