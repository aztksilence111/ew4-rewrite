# 导入失败与修改未生效

先记录原APK/ZIP名称、Mod标识、报错原文和出错步骤；保留源文件，修正作者副本再导入。不要为绕过错误手改已安装哈希目录或删除存档。

玩家可在主菜单或对局暂停后的“设定 → 存档备份 → 导出存档”导出ZIP；Android与电脑版均固定支持。反馈时附ZIP、复现步骤、版本与截图，Mod问题另附原Mod文件或来源。ZIP保留原始账户／存档及备份，不要求存档能成功读取；`export-info.json`记录版本、平台、前端源码标识、当前Mod信息与逐文件SHA-256，排除安装内容和启动缓存，不会改写原档。

| 现象/报错 | 先检查哪里 | 处理方法/源码 |
| --- | --- | --- |
| 未找到原版资源 | ZIP根目录/统一assets前缀 | 去掉多余作者文件夹；ImportSource与LegacyModImporter.Convert |
| 基础资源版本不匹配 | mod.json.baseRevision | 当前工具重新导入原资源，不手工替换hash |
| 源哈希不匹配 | original XML与legacy/catalog.json.source_sha256 | 重新走原资源转换；LegacyRuntimeCatalog.VerifySources |
| 文件类型或哈希不符 | mod.json.files列出的文件 | 修改后重新pack；不要改完直接在哈希目录刷新 |
| 未实现技能ID | def_commander.xml的skill1～4 | 原格式0～39，空位-1；新原生编号清为-1后通过[API 1](porting/RULE_EXTENSIONS.md)定义/绑定JSON与Lua，不靠改名冒充效果 |
| extension.lua_missing / lua_function | extension.json的scripts、effect/VICT的script/function | VICT专用脚本也须登记；检查大小写与全局函数名，重新打包 |
| extension.victory_reference | 条件的国家/单位/格子ID | 从该剧本初始API/存档查ID，格子用整图绝对编号；不是国家简称或裁切内编号 |
| extension.slot / instance | unitBindings与commanderBindings | 同单位合并后实例ID不可重复，建议unit./commander.前缀 |
| XML结构错误 | 报错文件、行/列及父子标签 | 非标准声明和相连属性已兼容；缺失结束标签等结构错误仍拒绝 |
| Invalid unit profile | def_army.xml的strength等字段 | 精确值-999已映射为开火后消耗的一次性武器；其他负值仍需确认语义，不能随便改成正数 |
| 城市名称不存在 | def_areaname.xml及BTL地点ID | 已保留据点并允许名称为空；同图同剧本同位置/ID/类型时才补原名，Mod已有名称优先 |
| Missing legacy unit grade | def_army.xml国家、兵种、grade及BTL | 全局卡对缺定义国家不可用；显式剧本单位仍需真实定义，不自动借用其他国家属性 |
| Unknown commander rank or title | def_commander.xml的rank/nobilityrank及规则表 | 军衔生命、爵位治疗/商业/经验表必须配套；不能只截断等级 |
| 多个剧本无法启动 | 错误列出的BTL ID及各自字段 | 会汇总全清单的开局错误；没有绕过、静默删除或隐藏问题关卡 |
| 缺少兵种显示配置 | catalog.profiles与manifest.profiles的ID | 提供def_motion和图集等配套；ConvertProfiles/ResourceValidation.Check |
| 图集裁剪超边界 | XML的x/y/w/h与实际PNG/PKM尺寸 | 使用同一套HD图片和坐标；Raster.Crop |
| 动作缺失/节点引用越界/循环 | manifest.profiles[id].motions及animations库 | 使用存在的item名，维护关键帧引用；CheckAnimation |
| 地图缺少索引/图集 | def_map.xml.tile、mapN_hd.bin、map/mapN_*@2x.pvr | 提供同分辨率的完整套件；ConvertMaps |
| 菜单没有新征服 | mod.json.scenarios与conquest-pages.json | 两层都需登记；直接pack不会默认增加新场景 |
| 标题变了但属性没变 | UI文字和original XML是否都修改 | 文字和实际规则不同层；检查新局而不是旧实例 |
| 头像/按钮还是旧图 | manifest.atlases、ui裁剪图、HD优先级 | 同时核对预裁剪图，不只改images |
| 菜单字仍旧/字体不一 | stringtable_cn.ini、def_areaname.xml、TextRenderer.element | 文字内容可改；统一使用rewrite自带字体，不再沿用Mod位图字形/描边 |
| 战役/征服背景混乱 | layout-568h.xml的back与tx/ty/tw/th、menuTextures | 选择页与关卡预览可用不同纹理；ConvertMenuAssets、MenuArt.prepare按引用及比例读取，重新导入可更新派生缓存 |
| 切回原版背景残留 | FrontendResources.Frontend/ReadContent生成的r-hash地址 | 更新客户端并刷新；原版/每个包必须有不同资源URL，不能让CSS固定地址复用旧的解码图 |
| 中文地名切不到英文 | frontend/city-names.json | 可反向匹配已知中文名；自定义名称需作者补充对应翻译，不能从字库图案推导英文 |
| 城市名全黑或描边被染色 | TextRenderer.element及调用处的填充/描边、客户端版本 | 当前自带TTF白填充黑描边；改FNT/PNG或preserveColor不能修复现行文本，见fonts说明 |
| UI图正确但点击区/返回错误 | Menus、app.js、index.html与原布局对照 | 部分交互仍由客户端绑定；按移植指南适配，不因导入layouts自动恢复 |
| 音效没响 | effects.sounds和时间轴sound、sounds文件、音量 | 文件存在不等于已绑定；Effects.sound |
| 新Mod里没有旧存档 | saves/mods/<id>/<logicRevision> | 检查ID/规则指纹，旧目录保留；不要强搬不兼容战局 |
| 已启用但变成原版 | active.json指向的包及启动报错 | 损坏包启动会回退；重新导入正确包 |

## 导入通过后仍需测试

校验验证格式、引用及后端剧本启动，不证明所有菜单/兵种/特效都可见，也不证明长期AI能完整结束战役。真实改版必须实测：主菜单/将领 → 征服选国 → 战役章节 → 新局 → 移动/攻击/生产 → 回合 → 保存重载。只测原包或只看到“导入完成”不能算兼容通过。

原生SO/DEX新增机制不会执行；若资源与原生行为强耦合，需要明确指出缺失行为。不能默默把不支持的技能替换成另一技能再称兼容。

## 报告与开发定位

安装包内`import-report.json`含输入来源hash、原生库hash和提示；`sources`逐项记录resource/source/sourceHash/resolvedHash/reason，可区分输入包、内置回退、XML规范化及精确补名。`validation-report.json`记录成功安装时各剧本的地图诊断。二者不是逐次战斗日志。管理器显示当前导入错误，当前发行版没有承诺导出完整异常堆栈。开发测试的详细异常在`rewrite/reports/real-mod-compat-20261019/.../error.txt`，用户自己的正式存档与该测试目录隔离。

具体代码文件名见[源码索引](SOURCE_INDEX.md)。先判断错误属于原格式解析、目录生成、资源验证还是前端加载，再改对应层；不要一律放宽Validate掩盖损坏输入。
