# 0.2.24 发布进度

2026-10-27。用户要求双端打包、上传 Git 与 Release，并包含所有更新内容。

- 版本：0.2.24 / Android code27 / 规则API3。
- 发行目录：`build/release-0.2.24-20261027`。
- 纳入当前全部源码、前端资源、接口文档与最小示例，包括此前排除的剧情播放器，以及存档导出、地图纹理修复、BGM恢复、部队超限经济和AI应急解散。
- 沿用包名与原签名；保留现有玩家账户、存档、已安装Mod及活动服务。示例不自动安装，不捆绑玩家数据。
- 已正式发布：[v0.2.24-20261027](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.24-20261027)。源码及标签 `42164de6bfe6634fcbee0f8e2e0d05b7c0413a12`，main已同步。
- 本轮追加：战役／选国原版界面、等尺寸标签平移及右缘覆盖、标题／载入居中、旧Mod底图独立扩展；将领七类能力超过五星显示与持久化、商业道具价格每星4%在五星后继续。
- 双端构建零警告错误；核心314938、客户端64459／海军24、通用Mod124、剧情74、商业13444、Mod高星及交易57、实际APK程序集启动17和高星57、发行Web战役257／高星88、Web资源3341项通过。APK同原证书v2／v3签名，code27，10个ARM64库及16KiB对齐通过；最终附件3681项含ZIP3534文件逐字节一致。没有Android真机安装验收。

## 正式附件

- APK：190253973字节，SHA-256 `d2b17badf80dd77006630faf48c1ca4a6ccc5302bd6c1c04de5e0374924f77db`。
- Windows Web ZIP：216186257字节，SHA-256 `0f17c7fd90bf22f64f6155be19988d492ed0d798618846f65f1f631053d2138c`。
- `release-manifest.json`、`SHA256SUMS.txt`：共四附件，GitHub大小及SHA-256核对错误0。凭据见发行目录 `validation-summary.json`、`verification.json`、`android-verification.json`、`android-assemblies.json`、`web-verification.json`、`remote-audit.json`。

## 清理与预览

核对Git、绝对路径、重解析点及活动消费者后，停止本轮独立5589服务，清测试截图／日志／随机档案／Mod夹具／重复bin/obj、旧预览宿主及三批外旧本地包和运行目录，63目标11714文件2987773831字节，剩余0／错误0。先保存 `cleanup.json`，再更新双STATUS和日志。保留0.2.24／0.2.23／0.2.22三批，六包摘要复核通过；原始输入、现有服务、玩家账户／存档／已安装Mod保持。5588改用本次发行宿主并沿用原profile，3份账户／存档摘要变化0，页面及兼容菜单HTTP200。没有自动安装示例或导入大鏖战。
