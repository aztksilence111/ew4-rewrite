# Android：用原发行密钥重签并覆盖安装

本作者包按项目所有者要求附带 R 版原发行签名密钥。它用于重签你修改后的 **R 版 APK**，以便在满足 Android 更新条件时覆盖原安装。它不是原欧四商业版或其他作者 APK 的密钥。

## 最快操作

完整解压作者 ZIP 到可写目录。修改 APK 内资源并保存成新文件，然后把新 APK **拖到 `03-sign-apk.cmd` 上**。输入文件及原密钥均不会被改写。工具自带 zipalign、apksigner 和精简 Java 运行环境，无须另装 Android SDK 或 Java。

成功后在 `exports/apk-时间-随机号/` 中取得 `*-signed.apk`，同目录有 `signing-result.json` 和 `signature.txt`。脚本会核对 16 KiB 原生库对齐、APK 签名、原证书，以及重签前后非签名条目逐字节一致。每次运行使用新目录，不覆盖旧成品。

也可在作者包根目录用 PowerShell 调用：

```powershell
.\sign-apk.ps1 -Apk 'D:\我的改版\modified.apk' -OutputDirectory 'D:\我的成品'
```

脚本只能对已保存的 APK 对齐并签名，不负责修改 JSON、编译代码、生成资源、调整包名或升级版本号。JSON 在 APK 内放哪里见 [JSON 路径指南](JSON_PATHS.zh-CN.md)。签完以后再改 APK 会破坏签名，需要重新运行脚本。

## 密钥及证书

| 项目 | 值 |
| --- | --- |
| 密钥文件 | `signing/ew4-release-debug.keystore` |
| 别名 | `androiddebugkey` |
| 密钥库密码 | `android` |
| 私钥密码 | `android` |
| 原证书 SHA-256 | `82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1` |
| R 版包名 | `org.ew4.reconstruction` |

`signing/signing-info.json` 保存以上信息及密钥文件摘要，`00-check-toolkit.cmd` 也检查签名组件。不要用操作系统另行生成的同名 `debug.keystore` 替换本包文件；别名和密码相同不等于证书相同。

## 覆盖安装条件

APK 包名和签名必须与手机已安装版本一致，versionCode 不得低于已安装版本。保持当前 APK 的 versionCode，可用于同版资源修订；需要递增时用 APK 编辑器或源码的 ApplicationVersion 设置。0.2.44 正式版的 versionCode 是 47，之后版本应查对应包，不能只看界面的版本名称。

在手机打开签名后的 APK，按 Android 安装界面进行覆盖安装。若使用自己的 ADB 工具，也可执行 `adb install -r "D:\我的成品\modified-signed.apk"`。作者包不自动连接手机或安装 APK。

覆盖安装正常情况下保留应用数据，但你修改的玩法规则仍可能令游戏使用新的账户／战局命名空间。先在游戏的主菜单或暂停设定中导出存档，避免把安装成功理解为旧战局一定兼容。

| 现象／错误 | 含义和处理 |
| --- | --- |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | 已安装应用使用不同证书。检查证书指纹；原欧四、其他渠道和你以前自行换签的 APK 不能保证用本密钥覆盖。保留数据，不直接卸载。 |
| `INSTALL_FAILED_VERSION_DOWNGRADE` | 新 APK 的 versionCode 更小。改用不低于已装版本的代码值后重新签名。 |
| 签名或密钥摘要检查失败 | 恢复作者包中的原密钥和签名组件，再运行完整性检查。 |
| Mod 提示基础指纹不一致 | 改了内置资源后基底已变；按实际目标基底重新封装 Mod，不手改哈希。 |
| 安装成功但数值没变 | 检查 JSON 路径、入口注册、当前是否启用了另一套 Mod 覆盖，以及是否仍在旧局／旧将领实例。 |

## 手工签名

在作者包根目录打开 PowerShell，以下使用自带组件；把 APK 路径换成自己的文件，输出文件应选新的路径：

```powershell
.\signing\build-tools\zipalign.exe -f -P 16 4 'D:\mods\modified.apk' 'D:\mods\aligned.apk'
.\signing\java\bin\java.exe -jar .\signing\build-tools\lib\apksigner.jar sign `
  --ks .\signing\ew4-release-debug.keystore --ks-key-alias androiddebugkey `
  --ks-pass pass:android --key-pass pass:android --v4-signing-enabled false `
  --out 'D:\mods\signed.apk' 'D:\mods\aligned.apk'
.\signing\java\bin\java.exe -jar .\signing\build-tools\lib\apksigner.jar verify `
  --verbose --print-certs 'D:\mods\signed.apk'
.\signing\build-tools\zipalign.exe -c -P 16 4 'D:\mods\signed.apk'
```

证书指纹必须与上表一致。自动入口另外核对所有有效内容字节并生成凭据，通常直接使用 `03-sign-apk.cmd` 即可。组件来源及许可证保存在 `signing/THIRD_PARTY_NOTICES.md`、`signing/build-tools/NOTICE.txt` 和 `signing/java/legal/`。
