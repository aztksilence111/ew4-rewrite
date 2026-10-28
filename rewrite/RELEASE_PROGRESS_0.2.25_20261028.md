# 0.2.25 发布进度

2026-10-28。用户要求打包双端并上传 Git 与 Release。

- 版本：0.2.25 / Android code28 / 规则API3。
- 发行目录：`build/release-0.2.25-20261028`。
- 纳入外交与核心地区／首都国库、旧档兼容与结算、回合及大部队性能、原版界面和学院动画、Android启动诊断等当前更新。
- 沿用包名与原签名，保留玩家资料、活动服务和原始输入；不捆绑玩家资料。
- 已正式发布：[v0.2.25-20261028](https://github.com/aztksilence111/ew4-rewrite/releases/tag/v0.2.25-20261028)。源码及标签 `03555f43b6300384bb0dcfc515c0bebcad0d082a`，main已同步。
- 双端构建通过；核心318567、客户端64455／海军24、结算485、通用Mod124及客户端／结算／快照／扩展／启动资源／外交Mod专项通过，错误0。更新两处过期测试：复制的技能集合不可变；指纹不同但定义齐全的旧账户可复用，缺失定义仍拒绝且保留原文件。
- 正式Web浏览器战役257、学院154、显示47、外交53、回合27共538项通过，错误／缺图0。外交使用专门生成的存档夹具；学院购买与回合空槽检查使用独立测试账户。
- Web资源3348、实际APK程序集22及最终附件3713项通过；Windows ZIP3560文件逐字节一致。APK原证书、包名／code28、10个ARM64库及16KiB对齐通过。未做Android真机验收，荣耀Play8T启动问题尚待实机确认。

## 正式附件

- APK：190332855字节，SHA-256 `ef5bec72e03dd9c70b090dffe63b9786b0a99317a33c8d3a78eea3dda5506127`。
- Windows Web ZIP：216206712字节，SHA-256 `0faba20ea77e176a413d2862fc5c4188f17f6765b75cc7d0e961de183b439850`。
- `release-manifest.json`、`SHA256SUMS.txt`：共四附件，GitHub大小／SHA-256及源码／标签核对错误0。必要凭据保存在发行目录的`*-verification.json`、`android-assemblies.json`、`validation-summary.json`、`verification.json`、`remote-audit.json`。

## 清理

远端验证后核对Git、绝对路径、重解析点和活动消费者，停止本轮5605独立测试服务，清测试账户／截图日志／Mod夹具／重复bin/obj和三批外旧本地发行产物，66目标15168文件4063220121字节，剩余0／错误0。先保存`cleanup.json`和`post-cleanup-verification.json`，再更新双STATUS和日志。保留0.2.25／24／23三批，六包摘要一致；8份账户／存档变化0，5592／5596／5597／5599／5604五服务HTTP200。原始输入、待真机反馈的荣耀诊断包、签名、已安装Mod和玩家资料保持，未删除远端历史Release。
