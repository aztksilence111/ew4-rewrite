# 东亚、日本、神罗三图检阅

同一Mod在征服第二页提供三个入口：东亚`conquest7`、日本`conquest8`、神罗`conquest9`。原六个征服保持。场景各有两支原版占位部队；建筑保持无归属，没有历史势力或手工辖区设定。

内置资源位于`rewrite/data/`，日本57939格／410建筑（153农田）、神罗52461格／647建筑（262农田）；东亚复用现有21488格／521建筑地图。新两图使用原版6山体／6丘陵，城市七级、农田三级及四向港口直接写入BTL。地形移动、防御和桥渡使用实际属性，近景水系按视野加载高清抗锯齿图块。

最小作者源码为仓库根的`tools/integrate_japan_hre_runtime.py`与`tools/MapReviewRuntime/`，不在示例目录重复存放图集、地理原始输入或冻结运行副本。先运行Python生成共享资源与临时场景，再运行C#工具验证、封装、重新导入并启用独立检阅档案。Python需使用项目现有的地图依赖环境。

两图地理文字用`tools/dual_map_lettering.py`复用东亚原版字形和曲线采样；真实地形轴线来自`maps/dual_map_config.json`，平原／海域路径与山名跨度调整来自`maps/dual_map_lettering.json`。只修改文字时可给生成工具加`--lettering-only`，随后仍须重新封装Mod以更新基础资源绑定。该选项保留已有临时场景，首次构建先执行完整生成。

```powershell
& 'C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' -B tools/dual_map_city_levels.py
& 'C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' -B tools/dual_map_ports.py
& 'C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' -B tools/dual_map_agriculture.py
& 'C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' -B tools/integrate_japan_hre_runtime.py
dotnet run --project tools/MapReviewRuntime -- .
```

农田规则与城市等级分别保存于`maps/dual_map_agriculture.json`和`maps/dual_map_city_levels.json`。只修改等级／农田时可使用`--scenarios-only`保留不变的共享水系／文字资源；更换城市名称、港口位置、河道或字形后仍须完整生成并重封基础资源绑定。水系缓存按地理输入摘要复用，避免重复生成未变的大图。

输出`rewrite/content/three-map-review.ew4mod`，只含附加场景、文字与菜单登记，复用当前内置三图。必须匹配本次更新后的引擎和资源；旧0.2.21不含日本／神罗底图与CELL读取能力。完整格号格式见[说明](../../docs/porting/BTL_CELL_INDICES.md)。
