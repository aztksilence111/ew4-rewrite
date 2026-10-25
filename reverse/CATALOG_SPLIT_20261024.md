# 规则配置独立拆分

2026-10-24，按用户要求将原 catalog 及两张混合规则表拆为独立编辑入口。

## 交付

基础路径 `rewrite/data/legacy/`，覆盖包路径 `legacy/`。
14 份独立 JSON：commanders、skills、units、unit-variants、items、recruitment、buildings、
installations、terrain-rules、conquest-list、combat-rules、unit-progression、
commander-progression、economy-rules。

`catalog.json` 变为 schema 3 来源/文件索引；旧 runtime/metagame 表变为 schema 2 兼容索引。
目录共 17 个运行 JSON，合计 608141 字节；原 catalog 1710270 字节。
原 XML/INI 未修改，来源材料与拆分前快照保留。
完整字段和编辑流程见 [指南](../rewrite/mod-kit/docs/porting/SPLIT_CATALOG.md)。

## 实现

- `LegacyCatalogFiles` 合成一致的旧版内存目录及 XML 视图，原规则模块共用同一份 JSON。
- 将领商业、属性、价格、抽取定义共源；装备实际效果、交易价格和面板原定义共源。
- 原版技能名称/描述从 skills 文件接入客户端；现有动态参数描述与扩展技能仍兼容。
- 将领 skills 数组可直接引用已在扩展入口注册的新技能；数量不再受旧 XML 四槽限制。
- 兵种 weapon 引用公共倍率；攻击次数使用公共血量阈值；显式单兵种配置可覆盖默认表。
- 成长字段不再在 catalog 原生表及成长表重复存储；合成视图从同一成长配置读取。
- 直接配置参与基础/逻辑指纹；账户规则修订包含有效目录，避免修改独立将领数据后复用旧规则身份。
- APK/XML 导入输出同一组独立文件；旧 schema 2 合并目录与 schema 1 完整规则表继续可读。
- Windows/Android 自动纳入顶层规则 JSON；Android 枚举实际 data 资源，资源索引同步。
- ModTool 新增 split-catalog、catalog-export；Python 导入、地图集成及审计读取器同步适配。

## 验证

证据目录 [build/catalog-split-20261024](../build/catalog-split-20261024/verification-summary.json)。

- 原始值及 Python/C# 加载器一致性 970 项：369 兵种、588 将领、40 技能及相关映射/规则值保留；Python 写出再读取一致。
- 独立配置 977 项：直接改商业/移动/价格、技能文字、装备效果与价格、招募、地形、设施、建筑、成长和经济配置；旧目录兼容；重复技能、未知字段、缺文件拒绝。
- 独立 Mod 7 项：单文件覆盖、已注册 R 技能直接绑定、启动、导出重导、规则指纹变化、旧合并目录覆盖。
- 核心完整回归 311034 项、既有 Mod 回归 106 项通过。证据为 core-validation.json 及原 Mod 检查输出。
- Web Release 发布通过；独立运行/资源/Android 包资源清单核验 11407 项通过，2247 资源记录、2324 Android 资源项。
- 欧洲征服、欧洲战役、美国战役、两种教程均独立启动；无仓库 root 参数依赖。
- Android Release Compile 通过，零警告错误；相关 Python 文件语法检查与修改范围 diff 检查通过。

## 预览与边界

隔离发布预览 `http://127.0.0.1:5519/`，启动时 PID 1424。
运行目录 `build/catalog-split-20261024/web`，档案在同证据目录的 preview-profile。
API catalog 返回成功，95 个原剧本。未操作既有玩家档案或替换现有预览进程。

未生成 APK、ZIP 正式发行批次，未上传 GitHub，未做真机验收。
旧 ew4mod 的基础指纹限制仍有效，不能将“旧目录格式可读取”理解为任意旧包无需重新导入。
未自动迁移已有玩家档案；已有将领实例不按新定义自动重置。
conquest-list 保存原目录元数据，不自动创建新剧本菜单。
原生来源资料表中的未执行常量不因拆分自动变成新规则接口；细节见指南。
