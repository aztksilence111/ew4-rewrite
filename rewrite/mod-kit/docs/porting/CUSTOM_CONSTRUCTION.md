# 自定义建筑、工兵与招募

API 3 源码接口，2026-11-02。现有 0.2.30 发行包不含新功能。R 本体未配置时保持原有步兵建造及建筑升级行为；Mod 显式配置启用。

在 `rewrite/extension.json` 增加 `"construction":"rewrite/construction.json"`，文件结构见 [完整最小示例](../../examples/advanced-units/README.md)。

| 字段 | 含义 |
| --- | --- |
| `enabled` | 默认 false；true 启用建造权限与自定义建筑 |
| `builderProfiles` | 工兵完整 profile ID 白名单，空数组表示无人能新建；列出各势力/级别的工兵配置 |
| `buildings` | 任意新建筑种类键到定义的映射，不覆盖原五类建筑 |
| 建筑 `name` / `cost` | 建造菜单名称及 money/industry/food 费用 |
| 建筑 `scenarios` | 可建剧本 ID 白名单，缺省空数组为全部；不按回合自动推进年代 |
| 建筑 `aiValue` / `aiValuePerLevel` / `aiUpgradeWeight` | AI据点价值基数/每级增量/升级权重，非负整数，默认60/10/3 |
| 建筑 `levels` | 1 至 32 级；首次建造固定一级 |
| 等级 `constructionTurns` | 默认0；新建/升入本级后的独立施工回合。仅所属方回合开始减一，0时完工；施工中禁止招募和升级 |
| 建筑 `builderKeepsMovement` | 默认false；true时施工消耗工兵行动，但保留剩余移动，可当轮离开 |
| 建筑 `recruitmentPerTurn` | 默认0为不限；正数为每站点每所属方回合招募上限，飞机离场或读档不重置 |
| 顶层 `recruitment` | 可选额外招募列表，使用相同招募字段及必填 `buildingKinds`；可用于城市招募工兵 |
| 等级 `image` | buildings 或 buildings_hd 图集中的 sprite 名；可在 Mod 图集添加自己的机场贴图，必须同时提供有效纹理和图集元数据 |
| 等级 `upgradeCost` | 升入本级的费用；一级不使用此字段，初建使用建筑 cost |
| 等级 `income` / `healing` / `defensePercent` | 回合收益、既有地表恢复、地表防御（0至100）；空中单位不继承地表治疗和防御 |
| 建筑 `recruitment` | 新招募项列表；`id` 全局唯一，不与旧卡冲突 |
| 招募 `profile` / `cost` / `minimumLevel` | 完整兵种 profile、招募费用、最低建筑等级 |
| 招募 `scenarios` / `countries` | 剧本及国家 ID 白名单；分别缺省为空，表示不限。初始与事件创建势力均配置 |

新建筑不自动放入开局地图。建造要求有未行动、非施工中、未眩晕的指定工兵站在己方有效、可通行、非水面、无建筑且无工事的格上，费用充足后原子扣费，工兵行动结束。机场可以建在仍有工兵的格上；普通空层飞机可招募在工兵上方，双层协同队须先让工兵离开，确保两层空位。

启用后，原战壕、铁丝网、掩体也必须符合 `builderProfiles`，保留其原类别与地点规则。以军队实例表达的炮垒/固定火力（地表 fort 类）改为相邻一格指定工兵施工，消耗该工兵行动；因炮垒占据地表，不能与工兵重叠施工。同一格多工兵按实例 ID 稳定选择一支。预置炮垒和事件增兵不属于新建操作。

升级只检查己方已完工建筑、升级费用、等级上限和本回合次数，不要求工兵或驻军。机场继承同一升级入口；目标等级可配置施工时间。工兵离开后建筑施工照常推进，不提供拆除或独立建筑耐久规则。

在建筑 `recruitment` 登记的 profile 只能通过相应自定义招募项及建筑招募；不能继续借旧城市/马厩卡绕过机场资格。实际兵种模板仍须在 legacy/catalog 与 client/manifest 中存在，并配套图像/数值。机场不自动创建兵种、航空星级或维修飞机能力。

AI 遵守相同工兵限制；当己方未拥有相应生产建筑、有当前工兵合法落点和该建筑招募权限时，会优先建一处，然后使用原生产/升级流程。尚无专门选址运输、调工兵建设全国机场网或主动训练工兵的策略。

`/api/action` 使用 `op:"building", unit:<工兵实例ID>, offer:<建筑种类>, revision`；选择结果的 `buildingOffers` 提供名称、图像、费用、`constructionTurns`、`available`及`unavailableReason`，和原工事共用建造菜单。工兵在合法工位但已行动时卡片仍显示，确认禁用并说明原因；执行仍按完整行动资格校验。地格的 `buildingConstructionTurns` 与 `buildingRecruitsThisTurn` 随存档保存。更改配置会改变 Mod 逻辑版本，不直接改旧档。未使用的新字段省略默认值，保持旧存档校验兼容。
