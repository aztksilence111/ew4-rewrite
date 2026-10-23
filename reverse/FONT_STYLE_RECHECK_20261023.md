# 界面字体与地图图层范围纠正

2026-10-23。用户纠正：只统一显示字体，不改写在地图图层上的文字；要求中文、英文和数字均对照原版重新核查。

## 结论与实际修改

- 0.2.14 将文楷用于中文、英文和数字，风格选择不准确。原PNG中文字具有传统楷体笔形；普通英文及数字具有粗衬线；战斗数字是粗黑斜体，不能用一个手写字体代替三类。
- 中文界面改用 AR PL UKai CN；英文及普通数字改用 Liberation Serif Bold 的轮廓；战斗数字改用 Liberation Sans Bold Italic 的轮廓。均在包内离线提供，后两者修改分片按OFL更改内部族名。保留原七字体作为缺字回退；未识别原版源字体文件，不宣称完全相同。
- `battlefield.js::mapText` 恢复0.2.14之前的图层绘制、边界、透明度、旋转和密度。英文地图标注恢复原图层美术。既有中文地图标注的文楷/宋体链被明确隔离为 `mapFontFamily/prepareMapText`，避免界面换字体再次影响地图。动态城市名仍使用界面字体。
- 界面DPR重绘、FB-15升级数字放大、FB-16缩小控件和FB-17编制标记保持。
- 截图复核发现加载时序缺陷：发布通用文字渲染器后再等待字库，MutationObserver可能先把原菜单文字转为画布，随后originalLabels读取textContent为空。已将渲染器发布移到最后一次await之后，新增慢字库和五视口主菜单文字像素检查。

## 证据与边界

`build/font-recheck-20261023/font-comparison.png`：原字图、0.2.14、修正后三行对照；七组样本包含中文菜单/控件、英文字母大小写、0–9、费用151/135/3600、64–74、易混淆字符和标点、战斗数字。`font-audit.json`记录实际使用字体及样本文字宽度。轮廓字体的advance与原字图有差异，保留控件的等比例单行适配，不声称逐像素重制。

字体选择经浏览器CDP实测：中文UKaiCN、英文和数字EW4UILatin-Bold、混排两者、战斗EW4Combat-BoldItalic；五组全部使用内置字体，无系统字体回退。

验证：FB-14–17专项73项/12图、字体回归42项/10图、启动故障与恢复33项/7场景通过，0脚本异常/缺图。三地图标注缓存与直接绘制原美术逐像素相同；源码与244f92a的地图方法比对只允许字体链隔离的两处等价调用差异。字库393分片、192984码位，六个本地APK文本及内置文本缺字errors=0，单片解码不超过8 MiB。

预览：http://127.0.0.1:5423 ，专用测试档案位于本目录对应的build子目录。原5421是已发布0.2.14，未覆盖旧发行文件。当前修正尚未提交、上传或打新Release，未做Android设备运行；该阶段按用户“再核查”要求提供本地修正和视觉证据。

## 复跑

```powershell
node tools/audit_font_appearance.cjs http://127.0.0.1:5423
node tools/verify_feedback_14_17.cjs http://127.0.0.1:5423 build/font-recheck-20261023/feedback
$env:EW4_TEST_URL='http://127.0.0.1:5423'
$env:EW4_FONT_REPORT='E:/Workplace/game/o4mod/build/font-recheck-20261023/regression'
node tools/verify_font_regression.cjs
node tools/verify_font_startup.cjs
python tools/audit_bundled_fonts.py
```

构建入口及下载来源见`tools/build_bundled_fonts.py`，原始TTF/TTC仅在`rewrite/local/font-sources-20261020`保存。字体许可随生成分片保存。未改动并发性能核查文件及其日志。

## 阶段日志

2026-10-23：完成范围纠正、三类字体接入和七组对照；截图发现并修复主菜单文字初始化竞争，补慢载入及五视口像素验收。源码语法及diff空白检查通过，短锚点已更新；本地5423供复核，旧发行保持。
