# 将荣招降、存档核对与R版实现

日期：2026-11-05。用户提供的四张本地图片均成功打开；随后按用户授权，只读提取本地《将军的荣耀：整合优化版.APK》的6个脚本，使用官方 GDRETools v2.7.4、GDSC 4.5.0 反编译，6/6成功。未运行参考APK或还原脚本。

## 参考证据

原APK：111831802字节，SHA-256 `cf7e248f9c4b3bc93c4967b510e6634a2e0e0c4c8d6ba28da5dc8ebc0ac70bfb`，与上次航母核查一致。方法为从ZIP提取 `assets/scripts/{original_battle,original_frontend,preserve_saves,save_game,game_manager,hq_rules}.gdc`，执行：

```text
gdre_tools.exe --headless --decompile=<script.gdc> --bytecode=4.5.0 --output=<scratch>
```

另只读核对 `assets/assets/json/DiplomacySettings.json`；该配置与脚本默认系数一致。工具来自 `https://github.com/GDRETools/gdsdecomp/releases/tag/v2.7.4`。

| 还原文件 | SHA-256 |
| --- | --- |
| original_battle.gd | 953e20f68d2554ad5afd3c5c52f1ce09a8fa9372defbf7b7cb504df94bbdeecb |
| preserve_saves.gd | e0c2cb28cdb4257c5acf907b05ae3cd53e9e9329e8a4420cd35df884e9bcfb8f |
| game_manager.gd | e28540d84ad5efc5bd3fa06b634c5bc6f30c3207c477479abdbc67458c180cf5 |

行号对应本次还原结果，临时源码清理后可按以上步骤复现。

## 招降

`original_battle.gd:14089 dip_surrender_price`、`hq_rules.gd:42`及DiplomacySettings共同确认：

- 基价累加目标全部存活单位的征服金币价及工业价，生命低不减价。
- `4142 set_start_commanders`按军衔降序确定开局将领列表；`13053 dip_lead_general`取首位。只有这位将领阵亡减25%，初始无将领没有此折扣。
- `14047 dip_alive_legions`统计同阵营未灭亡国家。我方阵营比对方阵营每多1个存活军团减5%，最多减25%。截图“军团多5支”不是“目标不超过5支”；初轮截图推测已被源码结论替换。
- 目标名下所有建筑等级合计每级加2%，上限50%，含城市/工厂/机场/港口/油田。
- 我方存活单位数多于目标减15%。三个预留额外系数当前均为0。
- 百分比相加，比例不低于0；金币和工业分别四舍五入。550/180、总修正-61%得到215/70。
- `14136 dip_can_surrender`要求军团模式、敌对、未灭亡、有部队；资源满足即可。所谓招降进度是现有金币/费用，不是另一个随机或战斗进度门槛。
- `14180 execute_surrender`付款、解散目标全部单位，建筑格归玩家，非建筑格无主；不接收败方国库余额。

R版新增 `PaidSurrender`，金币图标走 `purchase_surrender`；原卷轴概率劝降独立保留。系数为Mod可配置规则，无人物或阵营ID硬编码。新局最高开局军衔指挥官存入 `DemandState.Leaders`，不会因别的将领升衔、补入或存读档而换人；旧档缺字段只能按现存将领记录回退，不能恢复已经丢失的初始身份。R版用全部初始将领选最高军衔，不复制参考脚本扫描最多8人的内部限制。

R版建筑分类沿用自身城市/工厂/马厩/港口/农田等定义，并保留独立设施；付费招降只接管设施，空地无主。原概率及事件投降的领土移交不改。客户端提交复核回合、版本、保护名单、敌我、活兵、单位价格与资源；费用不足可查看报价但不能确认。缺价部队禁止免费招降。

## 存档

`preserve_saves.gd:6 SLOT_COUNT`为8；`subset:21`只备份选定账户字段，`apply:39`还会对部分解锁集合合并；不是整份战局原始备份。`encode:115/decode:121`使用GSAVE1前缀、XOR/Base64及12位SHA摘要的数据条。`game_manager.gd:458 click_confirm`实现保存/加载/删除确认；`wipe_saves:94`清其游戏规定的档案及战局文件。槽位“历战”实际指历战之证货币，不是战役次数。

R版按现有ZIP导出契约实现自身导入，不读取将荣数据条：

- 每个Mod ID和逻辑版本继续独立账户/战局目录；普通7战局槽及新增8备份槽均显示Mod名。新战局槽另存身份标识，错误身份在解码前拒绝；旧槽仍限所在命名空间读取。
- 8备份槽保存当前Mod账户、各模式战局槽及原始 `.bak` 字节；完整替换恢复，移除快照中不存在的战局槽，避免账户和战局混用。R版显示“已结算战局”，不把它冒称将荣历战之证。
- 主菜单“存档管理”和设定“备份槽”均可进入；Android/Windows Web主菜单与暂停设定保留导出，并新增导入入口。覆盖、恢复、删除、清理均有确认。
- ZIP导入检查格式/版本、逐文件长度、SHA-256、路径白名单、重复及大小写碰撞、清单完整性；拒绝安装目录、启动缓存和链接路径，限制4096文件、单文件64MiB、总原始数据256MiB。
- 有冲突先返回清单数量，确认后写入；覆盖前原始字节进 `recovery/<id>/`，写入I/O失败尝试回滚。相同字节不重写、不重复生成恢复副本。恢复副本也随导出保留；没有承诺跨多个文件的断电事务。
- 不要求坏档可解析，导入导出均保留原字节；导入后保留原命名空间，启用对应Mod才能读取该Mod资料，不自动启用或安装Mod。

## 验证与交付

- 招降专项20项：参考215/70、阵营数量及上限、建筑上限、无初始将领、首将阵亡但副将存活、首将身份存读档、资源/旧版本/保护/无兵/缺价拒绝、设施接管及空地无主。
- 原外交及实际客户端748项，含真实征服付费报价、扣费、战败及自动存档重载。
- 原导出35项，导入/备份/实际两Mod隔离41项；覆盖预览、原字节恢复、快照外槽位清理、跨Mod身份拒绝、损坏/越界/安装路径/重复ZIP拒绝均通过。
- 实际兼容前端三视口1280x720、844x390、390x844共48项；8槽、Mod标签、保存/取消、实际ZIP导出导入冲突确认/重载/恢复及付费确认通过，脚本错误0。已目检手机备份/报价画面；设定弹窗动画修正为专用缩放，关闭按钮保持在屏内。
- Web Release、Android ARM64 Compile零警告错误；Chrome70兼容54文件及修改文件diff检查通过。未生成新APK或替换正式0.2.31，未做Android真机操作。

独立干净预览：<http://127.0.0.1:56183>。运行目录 `build/save-features-preview-20261105`，独立资料 `rewrite/local/save-features-preview-20261105`。详细接口见[外交说明](../rewrite/mod-kit/docs/porting/DIPLOMACY.md)。

清理及简短验证凭据：`build/maintenance-20261105/save-features-cleanup.json`、`save-features-validation.json`。本轮反编译器/下载包/字节码/还原源码、测试账户/战局/截图/日志及重复编译清理，原APK、四张图片、玩家服务/资料/已安装Mod及同期改动保留；保留回归源码与必要预览运行文件。先凭据，再短锚点，最后WORK_LOG。

清理实绩25目标3223文件665660324字节，剩余0、错误0，Git状态在清理前后相同。12个必要宿主文件与最终Web构建摘要一致。新预览PID34856、首页HTTP200、catalog正常、95场景、hasSave=false、战局及备份文件0；既有56182首页仍HTTP200。清理脚本见 `tools/cleanup_save_features_20261105.ps1`，仅在确认QA宿主身份、真实绝对路径、无链接、无Git跟踪文件及无活动消费者后删除。
