# JSON 放置位置：作者工程、Mod 与 APK

先在 `author-kit.json` 核对 `gameVersion`、`authorKitVersion` 和 `sourceCommit`。这份包的游戏基底固定到该提交；正在开发的下一版接口，以那一版重新生成的作者包为准。同为 API 3 不代表支持所有后来新增的字段。

## 先分清三个根目录

作者包的 `data/` 保存基础资源，`reference/examples/` 保存可复制的接口示例，两者都不是自动生效的工作目录。修改时把需要的完整文件复制进 `project/`，只在工程副本中修改。`project/` 是你的作者工程根目录，也是生成 Mod 后内容路径的起点。

`project/rewrite/extension.json` 对应 Mod 内 `rewrite/extension.json`。将 R 版 APK 当 ZIP 打开时，它对应 `assets/rewrite/extension.json`。JSON 引用仍写逻辑路径 `rewrite/...`，不要加 `project/`、`data/` 或 `assets/`。文件名和路径大小写应保持一致，引用使用正斜杠 `/`。

## 完整路径对照

表中 `X` 表示原有相对路径及文件名。APK 列是 **R 版安装包直接运行时** 的真实路径，不是旧欧四原版 APK 的布局，也不是作者包工具的 `data/` 布局。

| 内容 | 从作者包哪里复制 | 作者工程中放哪里 | Mod 内容路径 | R 版 APK 内路径 |
| --- | --- | --- | --- | --- |
| 扩展注册入口 | `data/rewrite/extension.json` 或示例的 `rewrite/extension.json` | `project/rewrite/extension.json` | `rewrite/extension.json` | `assets/rewrite/extension.json` |
| 扩展 JSON、Lua、图标 | `reference/examples/<示例>/rewrite/X` | `project/rewrite/X` | `rewrite/X` | `assets/rewrite/X` |
| 原版技能数值模板 | `reference/examples/legacy-skill-parameters/rewrite/definitions/legacy-skill-parameters.json` | `project/rewrite/definitions/legacy-skill-parameters.json` | `rewrite/definitions/legacy-skill-parameters.json` | `assets/rewrite/definitions/legacy-skill-parameters.json`（原 APK 未附模板时自行新增） |
| 将领配置 | `data/legacy/commanders.json` | `project/legacy/commanders.json` | `legacy/commanders.json` | `assets/data/commanders.json` |
| 原版技能名称、描述 | `data/legacy/skills.json` | `project/legacy/skills.json` | `legacy/skills.json` | `assets/data/skills.json` |
| 兵种、装备、招募、建筑等独立表 | `data/legacy/X.json` | `project/legacy/X.json` | `legacy/X.json` | `assets/data/X.json` |
| 图集清单、兵模、国家、音效、音乐及呈现 JSON | `data/client/X` | `project/client/X` | `client/X` | `assets/www/content/X` |
| PNG、字体、声音等客户端资源 | `data/client/X` | `project/client/X` | `client/X` | `assets/www/content/X` |
| 征服分页 | `data/frontend/conquest-pages.json` | `project/frontend/conquest-pages.json` | `frontend/conquest-pages.json` | `assets/www/conquest-pages.json` |
| 城市名配置 | `data/frontend/city-names.json` | `project/frontend/city-names.json` | `frontend/city-names.json` | `assets/www/city-names.json` |
| 原格式 XML、BTL、地形 BIN、文字表 | `data/original/X` | `project/original/X` | `original/X` | `assets/original/X` |
| 外交及部队容量 | `data/rewrite/diplomacy.json`、`army-capacity.json` | `project/rewrite/` 下相同文件名 | `rewrite/diplomacy.json`、`rewrite/army-capacity.json` | `assets/rewrite/` 下相同文件名 |
| .44 将领头像及小头像皮肤 | 见 `reference/docs/porting/COMMANDER_SKINS.md` | `project/rewrite/commander-skins.json` 及其引用的图像 | `rewrite/commander-skins.json` | `assets/rewrite/commander-skins.json`，图像按各自逻辑目录映射 |

独立规则表完整清单见 [SPLIT_CATALOG](reference/docs/porting/SPLIT_CATALOG.md)。`legacy/skills.json` 主要是原版技能身份及文案；修改说明文字不会改变实际倍率，改倍率应使用上表的 `legacy-skill-parameters.json`。

## 已有接口应该接到哪个入口

配置文件放对位置以后，还需按接口规定注册。下表的文件名是组织示例；以你复制的完整示例内引用为准。单个字段要求字符串还是数组不能互换，具体 schema、字段和值域查对应文档。

| 修改目标 | `rewrite/extension.json` 中的字段 | 作者包参考 |
| --- | --- | --- |
| 原版技能数值 | `legacySkillParameters`：文件路径字符串 | `reference/examples/legacy-skill-parameters/` |
| 新技能、被动、状态、Lua | `skills`、`traits`、`statuses`、`scripts`：路径数组 | `reference/examples/common-skills/`、`passive-auras/` |
| 给将领／兵种绑定技能 | `commanderBindings`、`unitBindings`：文件路径字符串 | `reference/docs/porting/RULE_EXTENSIONS.md` |
| 装备效果 | `itemBindings` | `reference/docs/porting/ITEM_EFFECTS.md` |
| 剧情事件、增援、建国及胜负条件切换 | `scenarioEvents`；独立胜负定义用 `victoryRules` | `reference/docs/porting/SCENARIO_EVENTS.md`、`SCENARIO_ARMIES.md`、`SCENARIO_CONTROL.md` |
| 剧情演出／开场与暂停目标／结算呈现 | `storyPresentation`、`stageIntroPresentation`、`pauseObjectivesPresentation`、`victoryPresentation` | `reference/docs/CONFIG_REFERENCE.md` 的对应条目 |
| 将领能力、派遣、编制及喊话 | `commanderAbilities`、`deploymentLimits`、`formationCounts`、`commanderDialogues` | `reference/examples/` 下同名主题示例 |
| 地形、地图色彩、AI 操作与协防 | `geography`、`mapColors`、`aiControl`、`aiOrders` | `reference/docs/porting/GEOGRAPHY.md`、`MAP_COLORS.md`、`AI_CONTROL.md`、`AI_DEFEND_ORDERS.md` |
| 空地分层、建筑、收入、独立招募 | `unitLayers`、`construction`、`economy`、`unitRecruitment` | `reference/docs/CONFIG_REFERENCE.md` 的对应条目 |
| 局外商品与奖励、战役隐藏、指挥部呈现 | `metagame`、`campaignVisibility`、`headquartersPresentation` | `reference/docs/CONFIG_REFERENCE.md` 的对应条目 |

`rewrite/army-capacity.json`、`rewrite/diplomacy.json` 和 .44 的 `rewrite/commander-skins.json` 是固定路径配置，不需要往扩展入口添加同名字段。`client/manifest.json`、`client/countries.json`、`client/music.json` 等由对应资源读取器加载，也不应随意注册到扩展入口。不认识的字段或没有在配套游戏实现的机制，不能仅靠新增 JSON 获得功能。

`extension.json` 是**完整文件替换**，不是把多个示例的入口文件先后覆盖即可合并。已有入口时应保留它的字段，将所需新字段合并进去；API 3 工程保留 `apiVersion:3`，不要被 API 2 示例降级。若多个接口同时有绑定或事件文件，按接口 schema 合并内容并保留唯一注册入口，不能写重复 JSON 键。

## 实例：修改原版技能数值

### 用作者工程生成可导入的 Mod

1. 从 `reference/examples/legacy-skill-parameters/rewrite/definitions/` 复制参数文件到 `project/rewrite/definitions/`。目录不存在就创建。
2. 打开 `project/rewrite/extension.json`，在保留原字段的前提下添加：

   ```json
   "legacySkillParameters": "rewrite/definitions/legacy-skill-parameters.json"
   ```

3. 在参数文件填写所需覆盖。例如把间谍倍率设为 2，攻击战术概率设为 25%：

   ```json
   {
     "schema": 1,
     "skills": {
       "ew4.skill.4": { "multiplier": 2 },
       "ew4.skill.31": { "percent": 25 }
     }
   }
   ```

4. 双击 `01-check-project.cmd`，通过后双击 `02-build-mod.cmd`。把 `exports/` 生成的 `.ew4mod` 交给玩家，在主菜单 Mod 中导入并启用。再次修改后重新生成、导入，无实时热加载。

`ew4.skill.N` 是内部从 0 开始的编号；折扣 `percent:40` 表示支付原价 40%。完整参数范围和 29 技能对照见[参数说明](reference/examples/legacy-skill-parameters/README.md)。只保留需要覆盖的技能和字段即可，其余沿用默认值。这不自动给任何将领新增技能。

### 将同一配置放入 R 版 APK

1. 将未修改的原 APK 保留一份，另复制工作副本。用 APK 编辑工具打开副本的 ZIP 资源视图。
2. 新增或替换 `assets/rewrite/definitions/legacy-skill-parameters.json`。
3. 修改已有 `assets/rewrite/extension.json`，合并同一个 `legacySkillParameters` 字段。引用仍写 `rewrite/definitions/legacy-skill-parameters.json`，不要写 `assets/rewrite/...`。
4. 保存为新的 APK，把它拖到作者包的 `03-sign-apk.cmd`。工具先做 16 KiB 对齐，再用原发行密钥重签，并核验签名、证书及全部资源内容字节。
5. 在 `exports/apk-*/` 取签名后的 APK。包名保持 `org.ew4.reconstruction`，versionCode 不低于已安装版。0.2.44 的 versionCode 为 47；以后应以目标 APK 为准，不能把它永久固定成 47。

APK 直接修改会改变内置资源基底。原基底制作的 `.ew4mod` 可能因 `baseRevision` 不匹配而拒绝导入，不能手改哈希强行通过；需要按修改后的基底重新制作配套 Mod。规则更新也可能启用新的账户／战局命名空间，旧档会保留，不承诺直接沿用旧局。实际变更技能后应开新局核对效果，再验证存读档。

## JSON 与 ZIP 常见放错方式

- `project/data/legacy/units.json` 多了一层 `data/`；作者工程应为 `project/legacy/units.json`。
- APK 中的 `assets/legacy/units.json` 放错位置；R 版 APK 应为 `assets/data/units.json`。
- APK 中的 `assets/client/manifest.json` 放错位置；应为 `assets/www/content/manifest.json`。
- 只改 `reference/examples/` 不会生效，它是参考材料，不是你的工程。
- 扩展定义放了文件但未注册到 `extension.json`，读取器不会自动猜测它的用途。
- `legacy/*.json`、`client/manifest.json` 等完整表只保留想改的一行，会把其他行丢掉；应复制完整原文件再改。只有明确支持局部覆盖的字段，例如 `legacySkillParameters.skills`，才按局部覆盖处理。
- `.ew4mod` 根目录是 `mod.json` 和内容目录，不应多包一层 `project/`；由 `02-build-mod.cmd` 导出可避免这类错误。手工资源 ZIP 不是封装后的 `.ew4mod`，旧 APK/XML 导入布局见[移植指南](reference/docs/porting/README.md)。

## 发布前核对

在目标版本执行工具校验并实际启用 Mod；确认游戏读取新数值、AI 使用同一规则、资源路径没有大小写错误。检查一次新局、保存和重载。重签 APK 的证书核验结果并不等于真机覆盖安装验收，安装报错与恢复方法见[Android 重签说明](ANDROID_SIGNING.zh-CN.md)。
