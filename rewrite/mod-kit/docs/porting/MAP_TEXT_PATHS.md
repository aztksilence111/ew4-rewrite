# 地理文字沿曲线排布

地图 `client/manifest.json` 中 `maps.<地图名>.labels` 的每条地理名称可添加 `textPath`。中英文均逐字沿路径排列，字符自身随局部切线旋转。Canvas、Pixi、Web 和 Android 共用实现；不需要新增字体或下载素材。

```json
{
  "libName": "my_sea_label",
  "text": "TEST SEA",
  "textZh": "测试海",
  "tx": 500,
  "ty": 500,
  "size": 50,
  "sea": true,
  "opacity": 0.72,
  "geographicStyle": "regional",
  "textPath": [[-300, -40], [0, 100], [300, -40]]
}
```

点坐标为相对 `tx, ty` 的地图像素，向右、向下为正。`textPath` 是连续二次贝塞尔曲线：起点、控制点、终点；继续添加控制点、终点即可分段延长。例如五个点形成两段曲线，共用中间端点。点数必须为 3 至 65 的奇数，路径不能为空或全部重合；坐标必须有限且绝对值不超过 32768，两轴跨度各不超过 8192，字号为 1 至 512。

按文字阅读顺序布置路径，避免折返或尖角。连续曲线的前后控制点与共用端点宜在同一直线上，使切线连续。山脉和河流应先从地形坐标取中心线，再平滑、截取适合放字的段落；仅给旧直线添加一个统一弧度不能保证贴合地形。

- `text` 为原版字母贴图支持的英文名称，仅包含 A-Z、a-z 与空格，最长 256 字符。运行时会从原版 `maptext` 字库逐字组合，即使没有同名组合动画也能绘制。
- `textZh` 沿用内置中文字体，陆地宋体、水域斜体楷体，字体加载完成后自动刷新缓存。
- `geographicStyle: "regional"` 使用大区字形；省略时使用原版局部名称字形。水域大区沿用词首大写、`of` 小写及既有特殊字形比例。
- 通常省略 `pathChordLength`，英文会自动铺满路径；东亚生成器使用它保留既有英文间距与字号比例。`letteringLength` 是生成器记录的弧长。
- 曲线路径直接决定位置与旋转，不叠加 `angle`。缺少 `textPath` 的原有标签继续按原动画及既有中文直线逻辑显示。
- 可选的 `left/top/width/height/polygon` 由制图工具写出；曲线运行时的空间索引和画布范围直接从路径计算。文字层仍位于设施、城市名及部队下方。

## 东亚的作者配置

`maps/eastasia_rewrite/cartography_plan.json` 分别提供：

- `geographicPaths`：引用山系 GeoJSON 或现有喜马拉雅主脊，按原地图投影生成路径；可用 `slice` 截取坐标、`trim` 截取线长比例、`reverse` 翻转阅读方向。当前 12 处主要山系采用该方式。
- `pathBends`：其余地理名称的轻缓弧线，数值是局部法线方向的弯曲量。湖泊与平原不使用夸张曲率。
- `regionalLayout`：沿用的字号、跨度、位置等配置；地形引用路径的锚点由实际坐标重新计算。

仅更新文字并同步生成源：

```powershell
python tools/restyle_eastasia_labels.py --lettering-only
python tools/verify_curved_map_text.py
dotnet run --project rewrite/Ew4.ModChecks -- . --map-text-paths-only
```

此模式只改文字、位置 XML 和对应资源摘要，不调整地形 BIN、浪花、城市、设施、桥梁或镜头。英文同时写成逐字组合动画，供位置 XML 使用；实际两种语言共用相同的路径采样规则。修改内置资源后，检查 Mod 须按新基础资源重新导入，不能沿用旧指纹的检查包。
