# BTL完整格号：CELL v1

2026-10-27起的新rewrite引擎支持。旧APK或原版欧四不支持本扩展，不能仅导入Mod获得读取能力。

BTL主体仍为版本2，设施16字节、部队32字节记录保持。主体中的格号仍按有符号16位解析；大格号不强制转换成无符号数，也不截断。新图在`EW4XBTL`容器中附加必需的`CELL`块，版本1、flags=1；旧引擎遇到未知必需块会拒绝加载。

载荷是JSON对象：

| 字段 | 约束 |
| --- | --- |
| `schema` | 1 |
| `baseSha256` | 原BTL主体SHA256，不含扩展容器 |
| `mapWidth/mapHeight` | 完整底图网格尺寸，加载时与实际BIN核对 |
| `sites` | 按主体设施记录顺序，为每条记录提供一个完整底图格号 |
| `armies` | 按主体部队记录顺序，为每条记录提供一个完整底图格号 |

两数组必须完整，长度与主体记录数一致；元素为`0..mapWidth*mapHeight-1`的整数。生成器对可表示的格号保留主体值，对超过32767的格号写主体占位`-1`，实际位置由CELL提供。桥渡两端的独立设施记录也必须列入`sites`。

`LegacyBattle.Read`校验版本、主体摘要、数组长度和格号范围，`LegacyScenarioMap.Import`再校验真实底图尺寸。读取后的`LegacySite.Cell`与`LegacyArmy.Cell`是32位整数；没有CELL时保持原有16位读取语义。`ToBytes()`保留完整容器，`WithCellIndices()`可验证并写回扩展；其他扩展写入器保留CELL。

不要在旧编辑器中直接保存带CELL的文件，也不要剥离CELL后分发：主体中的占位值无法恢复大格号。改动主体后应重新生成完整索引与摘要，再重新封包。当前实例见仓库`tools/integrate_japan_hre_runtime.py`和`tools/MapReviewRuntime/`。
