# 自定义机场与双层单位示例

需要包含新接口的源码构建，现有 0.2.30 不支持。本示例使用原版步兵、民兵、马厩作为占位资源，不能作为完成的历史兵种或机场美术。

把 `rewrite/` 三个配置合并到 Mod 中。`client/unit-visuals.fragment.json` 是片段，须把 `composites` 合并到完整 `client/unit-visuals.json`，不能用片段覆盖完整文件。

- Guards grade0 仅作为工兵资格演示，须在正式 Mod 改为全部工兵连完整 profile ID。
- conquest1 的机场开局不生成，指定工兵在己方空闲陆地建造。所有原工事也仅限指定工兵；炮垒类单位由相邻一格工兵修建并消耗其行动，机场在工兵脚下建造。
- 机场一级可招募双层协同单位，二级再开放空层单位。升级无需工兵或驻军。
- 协同单位只占一个部队实例，同时占两层；水上保留两层容量，只显示空中部件，地面部件不能发起攻击或被地面通道攻击。
- 同一部分需要按目标层更换动作库时，在它的 visual definition 中配置 `attackVisuals:{"air":"其他visual键","surface":"其他visual键"}`。覆盖库沿用该部分坐标；其 `motions` 按既有定义提供 attack/reload/finish，反击也沿同一规则。

详见 [自定义建造接口](../../docs/porting/CUSTOM_CONSTRUCTION.md) 和 [分层接口](../../docs/porting/UNIT_LAYERS.md)。
