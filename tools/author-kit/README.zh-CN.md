# EW4 0.2.34 Windows 作者包

适用于 Windows 10/11 x64，配套游戏 **0.2.34**。自带运行环境，不需要安装 .NET、Python 或编译器。先将整个 ZIP 解压到可写目录，再操作其中的文件；不要在压缩软件的预览窗口里直接运行。

## 三步制作

1. 修改 `project/` 中的资源。`project/author-project.json` 填写 Mod 标识、名称和版本。覆盖路径直接是 `legacy/`、`rewrite/`、`client/`、`original/` 或 `frontend/`，没有 `data/` 前缀。工具自带的 `data/` 是配套游戏原始基底，请编辑 `project/` 中的副本。
2. 双击 **01-check-project.cmd**。它先检查 JSON 和常见字段类型，再调用正式工具生成 `project/mod.json` 并完整验证。报错会留在窗口，详细记录位于 `_work/logs/`。修好后重新双击。
3. 双击 **02-build-mod.cmd**。成功后在 **exports/** 获得 `.ew4mod` 和摘要凭据；将 `.ew4mod` 交给使用 **0.2.34** 的玩家，在游戏 Mod 页面导入并启用。每次生成新文件，不覆盖旧成品。

默认 `project/` 是最小有效空白工程，可以先运行一次熟悉流程。校验会检查所有登记剧本与资源引用，执行期间请等待窗口显示成功或失败；不要同时运行多个打包窗口。

## 修改现有 Mod

用 7-Zip 将原 `.ew4mod` 解压到一个独立文件夹，将 `project/author-project.json` 复制过去并填写原 Mod 的稳定标识、名称与新版本。在该文件夹修改资源，再把**工程文件夹拖到 01-check-project.cmd 或 02-build-mod.cmd 上**，即可操作这个工程。工具保留打包前的旧 `mod.json` 到本轮日志目录，然后重生成清单与指纹。

`author-project.json` 示例：

```json
{
  "id": "my-history-mod",
  "name": "我的历史改版",
  "version": "1.0.0"
}
```

通用脚本使用配套基础资源的全部剧本清单。只发行指定剧本的高级工程可按下文直接使用命令行及项目源码控制清单。新增或移植剧本请先阅读 `reference/docs/porting/README.md`。

## 常见配置错误

`legacy/units.json` 中 `damage_multipliers` 要写成目标类别键对象，不能用倍率数组。每个倍率对应的类别应由作者按原设计确认，工具会列出错误单位ID，并阻止导出。资源内容修正后应重新生成清单，不能只手改规则指纹。

工具不会猜测倍率顺序、删除配置或改动平衡数据。公开作者包只包含最小接口示例，不包含玩家原始附件。

## 工具、文档和目录

- **00-check-toolkit.cmd**：检查工具、基底及参考材料是否与分发摘要一致。用户工程和导出文件不在检查范围内，修改工程后仍可检查工具。
- **Ew4.ModTool.exe**：完整命令行工具，包括 pack、verify、import、export、split-catalog，以及国家／辖区／胜负配置命令。
- **data/**：正式0.2.34基础资源。全部独立规则 JSON、五张地图、原始剧本、图像、音频等均配套，支持从中复制资源到自己的工程修改。
- **reference/**：固定到同次正式发行的中文文档、配置索引和源码示例；从 `reference/README.md`、`reference/docs/CONFIG_REFERENCE.md`、`reference/docs/porting/SPLIT_CATALOG.md` 开始。
- **source/Ew4.ModTool/**：工具命令入口源码。完整游戏源码与版本见 `author-kit.json` 中的仓库链接与提交。
- **_work/**：独立工具内容库、日志和旧清单备份；不读取游戏安装目录中的账户、存档或已安装Mod。
- **exports/**：正式通过校验的作者输出。打包成功不代替实际玩法测试，仍需在目标游戏验证技能、移动、战斗及存读档。

基础版本摘要：`57c4aa3fb9627dcfb201bb168e5d1897679d0143439fa4d46e8ad58858b8426c`。同为 API 3 或相近游戏版本，不代表资源摘要相同；10月29日旧版不能直接加载按本包基底封装的覆盖包。

## 作者包何时更新

作者包不要求随每个游戏版本机械重打，但每次本体发行都应核对兼容性。基础资源、参与规则指纹的文件、Mod格式、读取／校验规则或接口变化时，需要更新配套工具和数据；仅启动、安装、性能或界面修复且相关依赖不变时，可沿用已验证的作者包。已制作的覆盖包如果依赖变了，也应按目标基底重新封装并完整验证。

作者包可独立修正文档、脚本和常见报错提示，单独增加作者包版本。每份作者包都应注明适配的游戏版本和资源指纹。本包当前只声明已验证的0.2.34，不自动承诺其他版本兼容。

## 命令行

在作者包目录打开 PowerShell：

```powershell
.\Ew4.ModTool.exe pack ".\project" --id=my-mod --name="我的Mod" --version=1.0.0
.\Ew4.ModTool.exe verify ".\project"
.\Ew4.ModTool.exe import ".\project"
.\Ew4.ModTool.exe list
.\Ew4.ModTool.exe export 实际64位key ".\exports\my-mod.ew4mod"
```

手工命令默认将工具内容库写入工具同目录的 `mods/`、`saves/`；双击脚本使用 `_work/` 下的独立目录。已有工程的稳定ID不要随意更换，文件与规则摘要由工具按真实内容生成，不手工伪造。不要用DLL／SO／DEX承载Mod新增机制；支持范围以配套接口文档为准。

此为额外作者工具包，不包含游戏账户、真实存档、签名材料或玩家已安装Mod。原游戏安装包不作替换。使用条件和第三方材料声明见 `reference/THIRD_PARTY_NOTICES.md`、`reference/licenses/` 及运行时附带声明。
