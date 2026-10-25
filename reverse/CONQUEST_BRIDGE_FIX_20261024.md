# 征服及战役桥梁显示修复

2026-10-24。用户先要求检查征服桥梁，随后明确要求修复共享绘制缺口。当前源码已完成，Web 独立预览为 http://127.0.0.1:5507 。未打新 APK、未发布、未更新玩家 5191 或操作玩家账户。

## 核查范围

六份征服 BTL 与原包逐份 SHA-256 一致，桥位、双向标记、河边及有效区域异常 0；普通/高清 buildings 图集各有 9 张桥图，无缺失。

| 征服 | 名称 | 桥梁数 |
| --- | --- | ---: |
| conquest1 | 欧洲 1798 | 15 |
| conquest2 | 美洲 1775 | 15 |
| conquest3 | 欧洲 1806 | 20 |
| conquest4 | 欧洲 1809 | 21 |
| conquest5 | 美洲 1812 | 10 |
| conquest6 | 欧洲 1815 | 21 |
| 合计 | | 102 |

合计按剧本分别计数，不跨年代去重。结合此前 87 份战役的 335 座桥，共覆盖 93 份剧本、437 座桥；13 份战役原本无桥。征服 204 个方向、战役 670 个方向重新调用真实 Movement.EntryCost，有桥费用 3、清除桥标志后费用 12，874 项通过。未改通行规则。

## 实现依据

- 原版 `reverse/native/decompiled/000506c4.c` 确认三种样式分别检查方向位 0、1、2，在本格中心按原图参考点绘制；方向 3、4、5 由对端的对应正方向绘制。按此规则消除双端重复，不增加半格偏移或旋转。
- `frontend_resource_refs.tsv` 的 `bridge_%d_%d.png` 定位到 607f4，三样式各三方向，与图集吻合。
- `ClientGame.CellView` 输出当前世界 `bridgeEdges` 及所属剧本的三种 `bridgeStyle` 方向掩码。桥型来自 EffectiveSites，不新增存档字段；旧存档加载复用相同路径。
- `battlefield.js` 的 Canvas 静态底图和 Pixi 地图块均在地形、建筑之后绘桥；网格、目标、部队和旗帜仍在上层。桥方向及样式进入静态缓存依赖，变更会刷新相关块。远景保留桥图。

## 验证

- Web Release 构建零警告、零错误，31 份 Chrome70 兼容前端生成和 ES2018 语法检查通过。
- 原显示测试 14308 项通过，新增三型 64 种方向掩码、非活动地块、清桥及缺省字段检查。
- 客户端 64473 项及海军任命 24 项通过；包括六征服和五个战役/分支/教程入口逐格桥型/方向、征服存读档。
- 专项浏览器 654 项、56 组画面通过，异常/缺图 0：六征服及 campaign2_01，在 1280×720、844×390、DPR2 下覆盖 Canvas/Pixi、近景/远景。逐组验证实际桥图提交、可见像素、镜头内桥位及移除桥标志后的缓存刷新；像素变化最少 669，已人工查看手机征服和桌面首关截图。
- 每个浏览器组合同时用 93 份真实桥位清单校验绘制次数，总数与 437 座一致；此项是全量提交检查，实际画面检查为上述七份代表剧本，不代表逐关通关或 Android 真机验收。
- Pixi 回归 17 组通过，覆盖 WebGL1/2、近远景平移、增量状态、丢失上下文恢复和战斗表现，错误 0；证据在 pixi-regression/results.json。

## 证据及复跑

`build/conquest-bridges-20261024/` 保存 inventory.json、runtime-checks.json、browser/checks.json、28 张近景截图、独立构建及档案。战役运行态/通行复查通过原审计项目重跑，`build/campaign-bridges-20261024/runtime-checks.json` 已刷新为修复后的接口字段；该目录旧 browser-checks.json 及截图仍是修复前证据。

```powershell
python tools/audit_campaign_bridges.py --conquest
dotnet run --project build/conquest-bridges-20261024/runtime -c Release -- E:/Workplace/game/o4mod
node tools/test_frontend_native.cjs
dotnet run --project rewrite/Ew4.Verify -c Release -- E:/Workplace/game/o4mod --client-only
node tools/verify_bridges.cjs http://127.0.0.1:5507
```

此前 [战役审计](CAMPAIGN_BRIDGE_AUDIT_20261024.md) 的“尚未修实现”属于当时状态，本记录替代该状态。Web/Android 共用源码，但本轮没有新 APK，既有手机安装包仍不包含此次修复。
