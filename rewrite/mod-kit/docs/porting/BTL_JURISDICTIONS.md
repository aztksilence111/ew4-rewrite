# BTL手工辖区：JURI v1

适用：2026-10-20起包含本功能的rewrite引擎及Mod工具。原BTL主体仍是版本2；本功能不是原版欧四已经支持的字段。旧APK/旧rewrite不会自动获得读取能力。辖区放在每个剧本的BTL中，不写入公共底图BIN，也不从其他已安装Mod借用。

## 作者工作流程

1. 备份源BTL与原Mod。先用原编辑器完成国家、设施、兵力、事件及剧本区域等修改。
2. 使用新版工具导出辖区。源BTL没有JURI时，导出当前自动划分结果；已经有JURI时，导出作者的现有划分。
3. 编辑JSON的`cellCenters`。保持其他元数据、数组长度和工具生成的`baseSha256`不变。
4. 写回**新文件**。工具会走真实开局路径验证地图、设施和地形；成功才创建输出文件，不覆盖源文件或既有输出。
5. 将新BTL放入自己的发布资源中，保持原剧本文件名；完整资源包使用`assets/<剧本>.btl`，rewrite覆盖包使用`original/<剧本>.btl`。重新导入/pack、校验、启用后新开一局。

在工具包根目录运行，输入文件仍保留剧本名称，如`conquest1.btl`：

```powershell
.\Ew4.ModTool.exe jurisdiction-export "D:\mods\source\conquest1.btl" "D:\mods\work\conquest1-juri.json"
# 使用文本/地图工具编辑JSON，然后写到不同目录，文件名保持conquest1.btl。
.\Ew4.ModTool.exe jurisdiction-apply "D:\mods\source\conquest1.btl" "D:\mods\work\conquest1-juri.json" "D:\mods\release\assets\conquest1.btl"
```

自定义地图、设施定义和其他规则发生变化时，需要提供**该Mod已转换的配套资源目录**：其中包含`original/`、`legacy/`等目录。工具优先读取此目录，缺少的文件使用工具自带基础资源。不要编辑已安装哈希目录；先复制到自己的工作目录。

```powershell
.\Ew4.ModTool.exe jurisdiction-export "D:\mods\source\conquest1.btl" "D:\mods\work\conquest1-juri.json" --content="D:\mods\work\converted"
.\Ew4.ModTool.exe jurisdiction-apply "D:\mods\source\conquest1.btl" "D:\mods\work\conquest1-juri.json" "D:\mods\release\assets\conquest1.btl" --content="D:\mods\work\converted"
```

`--base=<目录>`可指定基础`data`目录，省略则使用工具旁的`data/`。`--content`不是APK路径或原始assets目录，也不会自动转换XML/地图；修改将领/兵种等定义后先重新导入生成匹配的转换数据。

撤销手工划区：

```powershell
.\Ew4.ModTool.exe jurisdiction-strip "D:\mods\release\assets\conquest1.btl" "D:\mods\automatic\conquest1.btl"
```

该命令只移除JURI，保留其他可选扩展；没有其他扩展时输出恢复为原BTL主体。它不修改存档。原编辑器若拒绝扩展BTL，可先strip再编辑，之后重新导出/核对/写入JURI。不要假设旧编辑器会保留不认识的尾部。

## JSON字段与地块编号

JSON是编辑用中间文件，游戏**不自动扫描或加载此JSON**；必须用apply写入对应BTL。schema为1，未知字段、重复字段和不完整数组均报错。

| 字段 | 含义与约束 |
| --- | --- |
| `schema` | 固定为1 |
| `mapId` | BTL引用的底图ID，必须一致 |
| `mapWidth/mapHeight` | 完整底图网格尺寸，必须与当前加载的地图一致 |
| `left/top/width/height` | BTL剧本矩形，必须与主体头部一致；不是整张地图尺寸 |
| `baseSha256` | 原BTL主体的SHA256，小写64位十六进制；排除整个扩展容器，由工具生成 |
| `cellCenters` | 完整矩形内逐格数组，长度必须为`width*height`；不得少填或用null代替 |

两个编号体系必须区分：

- 数组下标为**剧本区域内**的行优先编号：`i = localY * width + localX`，从0开始，X先增加。
- 每个数组元素是**完整底图**中的辖区中心格编号，和BTL设施记录的`Cell`一致：`center = globalY * mapWidth + globalX`。
- 某个数组元素自身对应的底图格：`globalCell = (top + i / width的整数部分) * mapWidth + left + i % width`。
- 元素`-1`表示明确不属于任何辖区，不触发自动补全；`-2`、字符串、null等均非法。

例如：底图宽100，剧本矩形左上为(10,20)、宽3高2，则数组下标0～5分别对应底图格2010、2011、2012、2110、2111、2112。`[2010,2010,2012,2010,-1,2012]`表示两组辖区及一格无辖区。此示意只有在2010/2012确为有效中心、地形及主体信息均匹配时才合法，不能原样套到别的剧本。

## 加载优先级与规则

| 情况 | 行为 |
| --- | --- |
| 战役/征服BTL没有JURI | 沿用`TerritorialJurisdictions.Create`自动划分 |
| BTL有合法JURI | 完全使用逐格记录，跳过自动划分；`-1`也保留 |
| 有JURI但内容无效 | 导入/开局失败并报告剧本与记录/地块，不退回自动结果 |
| 教程含JURI | 拒绝启用该手工配置；辖区机制只适用于战役/征服 |
| 读取战局存档 | 保留存档内固定辖区，不按当前占领状况重新生成 |

城市、工厂、马厩才是中心，中心格必须指向自身。港口独立，不能作为中心，港口自身记录为`-1`；水体、不可用格和剧本范围外格也不能加入辖区。普通陆地和农田可指向本剧本中的有效中心；要塞仍是部队，不会成为辖区中心。

2026-10-25修正后的缺省自动划分按陆路距离除以中心权重比较：工厂/马厩权重12，城市权重为16加城市等级（最低按I级）；数值较小者优先，同分时权重更高者优先，最后按中心格号稳定选择。城市及高等级城市可以自然取得更多周边土地，但每个中心到自身的距离始终为0，因此远处大城不会用固定距离加成吞掉小城的整个腹地。独立小城和远离城市的设施可形成成片辖区，紧挨大城的设施较紧凑，允许多格小辖区被大辖区完全包裹；不预设每个中心的格数。同国连通陆地优先，隔海/飞地标签保留国家及陆地连通优先层次。等级按开局值计算，升级或换主不重划；手工JURI及存档中已有辖区保持原记录。此前“城市固定6格优势、等级每级加2格”的方案因把远处小城压为单格而撤回。

自动划分还会检查每块同国陆地能否在本辖区内连到中心。被其他辖区截断的碎片会重新分给最近且可连通的中心：从各中心的连通本体按实际陆路距离扩入碎片，距离优先，同距才比较权重，确保补归后仍连着本中心。没有任何同国中心可经同国陆地到达的岛屿或飞地只保留行政标签，标为`DetachedCells`，不据此产生陆上前线。手工JURI不执行这项自动重划。

作者可以改变最近中心关系，不要求按距离生成，也不会因指定中心而自动修改开局的国家归属。地块的国家归属和辖区中心编号是两种数据；开局国家归属仍来自BTL主体。中心换主不重新划区，后续染色继续执行现有的中心实踩、友方中心占领、要塞/农田保护及建堡资格等规则。

陆上接壤图由实际相邻且已分区的陆格计算，作者不直接填写`Borders`。隔海的地块可保留行政标签，但会标为`DetachedCells`，不构成陆上前线，也不会借此绕过现有跨海染色限制；其染色资格仍遵守该限制。`-1`格不属于任何中心、不会自动补辖区，也不具备“控制其辖区中心”的建堡资格；在现有规则下，标为`-1`的普通陆格也不会获得辖区染色资格，港口继续走独立占领规则。连接性和边界依据开局数据固定，后续中心或普通格换主不重算。

此功能不重写农田、要塞、外交、胜负或港口机制。要更改这些行为，仍需修改对应后端规则。

## 二进制协议

所有整数均为小端序，不对齐、不压缩、不带字符串结束符（下述8字节magic本身含一个零字节）。文件结构为：`原BTL v2主体 + 可选扩展容器`。主体长度由原BTL头部和各段计数/步长推导，不搜索magic；原记录和段偏移不改变。

容器头固定16字节：

| 偏移 | 字节数 | 内容 |
| --- | --- | --- |
| 0 | 8 | ASCII `EW4XBTL`加`00`，十六进制`45 57 34 58 42 54 4C 00` |
| 8 | 4 | uint32容器版本，当前1 |
| 12 | 4 | uint32块数量，1～64 |

每块由16字节块头及紧随的payload组成：

| 块内偏移 | 字节数 | 内容 |
| --- | --- | --- |
| 0 | 4 | Tag，四位大写ASCII，辖区为`JURI` |
| 4 | 4 | uint32块版本，JURI当前1 |
| 8 | 4 | uint32 flags，bit0为必需；其他位必须为0。JURI必须等于1 |
| 12 | 4 | uint32 payload长度，不含块头 |

同Tag不得重复。未知**必需**块、未知容器版本或不支持的JURI版本会失败；未知**可选**块允许读取，并在工具替换/移除JURI时逐字节保留。不能将未知JURI标为可选来绕过规则。整个扩展容器不超过64MiB，不能含尾随垃圾、截断或越界长度。

JURI v1 payload：

| payload偏移 | 类型 | 内容 |
| --- | --- | --- |
| 0 | int32 | mapId |
| 4 | int32 | left |
| 8 | int32 | top |
| 12 | int32 | width |
| 16 | int32 | height |
| 20 | int32 | mapWidth |
| 24 | int32 | mapHeight |
| 28 | int32 | cellCount，必须等于width×height |
| 32 | byte[32] | 原BTL主体SHA256的原始32字节，非64字符ASCII |
| 64 | int32[cellCount] | cellCenters，顺序及编号如上 |

只有一个JURI时，新增体积为`96 + 4 * width * height`字节。例如3604格增加14512字节，约14.17KiB。导出JSON为便于编辑会更大，不要求随游戏分发。

校验值只用于确认配置对应哪份主体，不是防止作者修改的锁。修改BTL主体前先strip，编辑后重新export、核对调整并apply；不要直接改payload中的hash绕过核对。若编辑器已经改了主体却保留旧扩展，读取会因hash不符而失败，需要恢复备份或由原编辑器另存不含扩展的主体后重做；当前strip不忽略损坏或不匹配的块。地图BIN或设施定义变化也需要重新验证，主体hash本身不包含这些外部资源。

## 生效、存档和离线导入

带扩展的BTL通过APK资源/ZIP/目录及rewrite覆盖包原有导入链读取，不执行包内代码；电脑与Android使用共享读取逻辑。JSON编辑和写入CLI目前在电脑端，手机游戏可离线导入已经制作好的包。Android实际运行仍须使用包含本功能的新引擎版本；本次不会自动更新旧APK。

BTL变化参与规则指纹和存档绑定，导入新内容后可能使用新的`logicRevision`档案目录；原账户/旧档保留，不会强行拿新辖区覆盖旧战局。该版本内保存/重载固定保存作者划分。不得为了复用档案而手改哈希/逻辑版本。

发布前至少检查：改一格到非最近中心确实生效、中心占领后的普通地块转属、农田/要塞保护、敌友双方和AI建堡资格、隔海不产生前线、港口独立、保存重载以及原版/Mod往返。

## 函数与排错入口

| 源码（相对仓库根目录） | 函数/用途 |
| --- | --- |
| `rewrite/Ew4.Legacy/LegacyResources.cs` | `LegacyBattle.Read/ToLegacyBytes/WithJurisdictions`：主体定位、完整保留、读取/替换扩展 |
| `rewrite/Ew4.Legacy/LegacyBattleExtensions.cs` | `LegacyBattleExtensions.Read/Jurisdictions/Write`：容器、版本、长度、校验值；`LegacyJurisdictionData.Apply`：局部数组转全图编号 |
| `rewrite/Ew4.Core/TerritorialJurisdictions.cs` | `FromAssignments`：设施/地形校验与陆上边界；`Create`：仅无JURI时自动生成；`JurisdictionClaims`：占领和保护规则 |
| `rewrite/Ew4.Client/ClientContent.cs` | `Start`：显式优先、模式限制 |
| `rewrite/Ew4.Client/ClientGame.cs` | `Load`：恢复保存的辖区；旧档补齐仅走现有兼容分支 |
| `rewrite/Ew4.Mods/LegacyModImporter.cs` | `Convert`：原BTL字节保留；`ModStore.Validate`：逐剧本实际开局检查 |
| `rewrite/Ew4.ModTool/JurisdictionCommands.cs` | `Run`：export/apply/strip，拒绝覆盖，校验后写入 |
| `rewrite/Ew4.Verify/JurisdictionExtensionChecks.cs` | 二进制异常、所有内置战役/征服往返、存档及覆盖优先级回归 |
| `rewrite/Ew4.ModChecks/JurisdictionModChecks.cs` | 真正离线导入、启用、自动存档、回导及失败保持当前包 |
| `tools/verify_jurisdiction_tool.cjs` | CLI往返、覆盖保护及错误配置检查 |

常见错误：`legacy.jurisdiction_source`表示主体变化；`legacy.jurisdiction_metadata`表示元数据/数量不符；`legacy.jurisdiction_center`指出数组记录的越界中心；`territory.jurisdiction_cell`指出全图地块的设施/地形错误；`legacy.battle_extension`表示容器/版本/块结构错误。先确认用了支持此功能的引擎和工具，再核对当前Mod配套地图，不使用另一个Mod的文件补齐。
