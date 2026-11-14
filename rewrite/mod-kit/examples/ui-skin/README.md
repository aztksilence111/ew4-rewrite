# UI 换肤最小示例

将本目录的 `rewrite/ui-presentation.json` 放进作者源码目录，用现有 ModTool seal／export 流程生成内容包。也可将 `rewrite/` 放入原版 APK 的 assets 目录后一同导入。此例只复用原图集：主菜单向内移、战役和征服换蓝按钮、退出换红按钮，不包含测试资源或脚本。

独立 PNG 放在 `rewrite/ui/`，把 `image` 改为例如 `rewrite/ui/campaign.png`。图片会随 Mod 封装、校验和按内容版本寻址。完整字段、坐标和覆盖边界见[通用 UI 换肤](../../docs/porting/UI_PRESENTATION.md)。指挥部整页树继续使用既有 [HQ 接口](../../docs/porting/HEADQUARTERS_LAYOUT.md)。
