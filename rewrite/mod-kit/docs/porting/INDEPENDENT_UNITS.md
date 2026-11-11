# 新增独立兵种

需要包含 `unitRecruitment` 的新宿主构建（2026-11-11 源码增补），旧 0.2.38 及更早发行包不包含此接口。API 3，Windows Web 与 Android 共用加载、生产和存档实现。默认不开启。

新增兵种使用完整字符串 profile ID，例如 `my.unit.cruiser.0`。它可以与原有护卫舰同时存在、分别定价和招募；不占用或替换传统兵种编号。部队实例 ID 是另一种 ID，每次生成或招募的部队各有一个实例。

## 定义和显示

在完整 `legacy/units.json` 的 `units[]` 中复制合适的原定义，设置新的唯一 `id` 并修改血量、移动、攻击、射程、粮耗、武器、特性等。保留其他记录。字段见 [[SPLIT_CATALOG]]（普通文件链接：[独立规则配置](SPLIT_CATALOG.md)）。复制模板是作者操作，运行时没有继承依赖；改新定义不会修改原定义。

同时在完整 `client/manifest.json` 的 `profiles` 新增同 ID 显示配置。可复制模板的图卡、动作库、坐标和缩放，再改 `name`、`description`、`stats`。设置 `recruitGroup` 为新兵种自己的 ID，防止与原种类合并成一张不同编队的卡。多级新兵种可共用自己的 `recruitGroup`，用 `grade` 区分编队。`kind` 是兵模匹配键，不要用随意改 `kind` 的方式分组。

在完整 `client/unit-visuals.json` 的 `profiles` 为新 ID 绑定动作定义键，可复用 `client/effects.json` 的 motions 或注册自定义动作库。需要新兵模时还须提供对应 animation JSON、贴图及引用；新增属性记录不会自动生成素材。详见 [兵模接口](UNIT_VISUALS.md)。编辑器会复制所选模板的卡图、显式动作绑定及已有组合兵模。

## 独立招募

在已有 `rewrite/extension.json` 中合并注册，保留其他字段：

```json
{"schema":1,"apiVersion":3,"unitRecruitment":"rewrite/unit-recruitment.json"}
```

`rewrite/unit-recruitment.json`：

```json
{
  "schema": 1,
  "offers": [{
    "id": "my.cruiser.recruit",
    "profile": "my.unit.cruiser.0",
    "buildingKinds": ["port"],
    "minimumLevel": 1,
    "cost": {"money": 180, "industry": 40, "food": 0},
    "countries": ["fra"],
    "scenarios": ["conquest1"]
  }]
}
```

每个报价 ID 全局唯一，须带点号，不能占用 `ew4.card.*`。最多 1024 个报价。一个 profile 可以有多个不同报价；名称、价格和 ID 各司其职。`buildingKinds` 为非空建筑种类列表（city / industry / stable / port / farmland 或已注册自定义建筑），`minimumLevel` 为 1～32。价格三资源非负。

`countries` 和 `scenarios` 省略或为空表示全部，非空时取交集。国家填写全局国家表的 ID，剧本填写不带 `.btl` 的 ID；未知引用、重复报价、未知字段或负价格会被拒绝。每个符合范围的国家获得独立招募授权及 AI 生产组；创建新势力时也沿用其国家范围。未配置的新旧国家和剧本维持原报价。

不需要启用自定义建筑接口。新兵种仍遵守海陆、地块占用、建筑等级、资金和部队上限；海军可在本国海面港口招募。新增海军生产组按 naval 任务规划，不混进陆军生产组。已有 construction 报价可并用，ID 不能冲突。

## 开局部署、事件与存档

传统 BTL 数字兵种槽仍是 0～21；不要把新兵种直接写成 22。开局放置采用 [事件增援](SCENARIO_ARMIES.md) 的 `unit_spawn`，将 `profile` 设置为新定义 ID，指定真实国家、实例 ID 和合法格号。条件可用第一回合，`ready:true` 表示生成后即可行动。后续回合增援和技能召唤也可以引用该 profile。

兵种属性、实例、招募授权及事件完成状态均随战局保存；存读档不重复生成。Mod 逻辑指纹隔离照常生效，改配置后应开新局。此接口沿用五个既有兵种类别及其将领/科技体系，不自动新增将领能力分支。

## 最小可制作示例与编辑器

[示例生成器](../../examples/independent-units/README.md) 可从自己的基础数据生成一份保留全部原记录、额外增加步兵和舰船的覆盖包。

编辑器：内容编辑 → 兵种 → 选择模板 → 复制为独立新兵种，编辑新 ID、属性、兵模模板和招募条件，预览后应用。已应用兵种可在开局增援面板按格号部署。工程历史、恢复和 `.ew4mod` 完整导出包含全部依赖；单独导出 BTL 不包含独立兵种定义和增援事件。
