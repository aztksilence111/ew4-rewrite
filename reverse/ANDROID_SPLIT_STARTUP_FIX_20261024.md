# JSON 拆分后 Android 启动修复

2026-10-24，针对最新综合测试 APK 停在主菜单并显示 `Error` 的反馈。

## 根因与证据

旧包 `build/device-plan-test-20261024/ew4-performance-test-20261024-arm64.apk`，
SHA256 `2e861931352495fa6eb68bcd806ce212cfe80c473a9b98367aa1fda678ae6983`。
17 份 JSON 均存在，`legacy/` 到 APK `assets/data/` 的映射正确。
但 Android 多 RID 构建复用了旧 ARM64 程序集，其中没有 `LegacyCatalogFiles`；
旧加载器直接把 schema 3 索引当成完整目录读取，抛出 `Unknown catalog version`。
ModStore 返回的 `{error}` 又没有被前端 RPC 读取，最终仅显示 `Error`。

原包资源及程序集 blob 相等的校验只能说明包与中间产物一致，不能证明中间产物来自当前源码。
本轮直接从两个 APK 的 ELF payload / XABA store 解压五个玩法程序集：
旧包稳定被“缺少拆分加载器”检查拒绝，新包通过实际 IL 加载及启动接口验证。
证据在 `build/android-startup-20261024/original-extracted-startup.log` 和
`extracted-startup.log`、`extracted-assemblies.json`。
桌面执行仅对临时副本调整 PE 机器类型，不能代替 Android 真机执行。

## 修复

- 打包显式指定 `RuntimeIdentifier=android-arm64`，完整 Rebuild，APK、blob 和程序集均从同一 RID 目录取出。
- 综合包及 JIT/AOT 对照脚本同步 RID 路径；综合包增加拆分加载器检查和 APK 解压启动检查。
- ModStore 错误响应补充 `ok/code/message`，保留原 `error` 字段；前端兼容显示两种错误格式。
- 仅允许已验证的内置 0.2.19 账号规则指纹迁移到本次等价拆分指纹：
  `e47fb936518323a29848404514de602e25bfc04bbc5fafa191485fa32c7d39e5` →
  `73e030e5c7d0f9a227b27cc1eb43cbe28f158177b02104788d02bca563ea12eb`。
  保留全部账号字段、递增一次修订号，写入时保留原 `account.json.bak`。
  修改过的规则和未知指纹不使用此迁移；验证失败的账号不再进入内存缓存，重试仍正确拒绝。

## 交付与验证

本地测试包：[ew4-startup-fix-test-20261024-arm64.apk](../build/android-startup-20261024/ew4-startup-fix-test-20261024-arm64.apk)。

- 0.2.19 / code 22，单 ARM64 JIT，144153302 字节。
- SHA256：`5336f3ee78171fefb22474f1448b99dacb8f0908240a6b31c4a9e818646ac5bf`。
- 283 源码输入前后稳定；2247 内容、2306 包记录、blob、原签名 v2/v3、ZIP/ELF 16 KiB 通过。
- 从新 APK 解压程序集后执行 13 项检查：目录、指挥部、征服开局、旧账号完整保留、备份、重开幂等、未知账号重复拒绝、缺配置错误路径。
- 配置专项 978 项；客户端 64451 项及海军部署 24 项通过。
- 既有 Mod 完整回归 106 项通过。
- 修改的 Python、PowerShell、JavaScript 语法检查及修改范围空白检查通过。
- 清理本批重复生成 APK 两份；清单 `build/android-startup-20261024/cleanup.json`。

旧失败 APK 作为本次输入及反例保留。正式发行三批、玩家服务、现有档案和已安装 Mod 未操作。
此前综合测试包“当前性能及拆分源码已入包”的结论被本报告覆盖；资源/旧 blob 的摘要校验本身不代表源码版本正确。
桌面性能测量证据仍是独立冻结构建，不能据此宣称旧 APK 已包含相同优化。

本次未上传、正式发布或安装设备。ADB 仅见离线模拟器，真机启动和性能需要用户复测。
本次迁移仅覆盖账号；旧对局存档和旧 Mod 包仍受各自规则/基础指纹限制，未放宽验证。
