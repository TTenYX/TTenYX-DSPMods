using BepInEx.Configuration;

namespace SmartDustbin
{
    /// <summary>
    /// 全部配置项（BepInEx\config\TTenYX.DSP.SmartDustbin.cfg），修改后立即生效，无需重启。
    /// 对应《垃圾桶MOD设计书v0.1》的 6 项配置要求。
    /// </summary>
    internal static class ModConfig
    {
        /// <summary>是否启用功能1（储物仓/储液罐垃圾桶），默认开。</summary>
        internal static ConfigEntry<bool> EnableStorageVoid;
        /// <summary>功能1 是否对小型储物仓生效，默认开。</summary>
        internal static ConfigEntry<bool> EnableSmallStorage;
        /// <summary>功能1 是否对大型储物仓生效，默认开。</summary>
        internal static ConfigEntry<bool> EnableLargeStorage;
        /// <summary>功能1 是否对储液罐生效，默认开。储液罐只消耗液体类物品。</summary>
        internal static ConfigEntry<bool> EnableTank;
        /// <summary>是否启用功能2（传送带图标标签消耗），默认开。</summary>
        internal static ConfigEntry<bool> EnableBeltSignalVoid;
        /// <summary>消耗速率的统计周期（秒），默认 60（1 分钟）。</summary>
        internal static ConfigEntry<float> StatPeriodSeconds;

        internal static void Bind(ConfigFile config)
        {
            const string feature1 = "1. 功能1 储物仓/储液罐";
            const string feature2 = "2. 功能2 传送带信号";

            EnableStorageVoid = config.Bind(feature1, "EnableStorageVoid", true, new ConfigDescription(
                "是否启用功能1。默认 true。\n" +
                "小型/大型储物仓：「自动化输入限制」滑条拉到 0（可用格数 0）时，消耗所有输入的物品\n" +
                "（手动放入、分拣器、储物仓上方的传送带末端与四向分流器落料都会被消耗）；\n" +
                "储液罐：「输入」开关设为关时，消耗所有经传送带和手动注入的液体（只消耗液体类物品）。", null));

            EnableSmallStorage = config.Bind(feature1, "EnableSmallStorage", true, new ConfigDescription(
                "功能1 是否对小型储物仓生效。默认 true。", null));

            EnableLargeStorage = config.Bind(feature1, "EnableLargeStorage", true, new ConfigDescription(
                "功能1 是否对大型储物仓生效。默认 true。", null));

            EnableTank = config.Bind(feature1, "EnableTank", true, new ConfigDescription(
                "功能1 是否对储液罐生效。默认 true。储液罐只消耗液体类物品。", null));

            EnableBeltSignalVoid = config.Bind(feature2, "EnableBeltSignalVoid", true, new ConfigDescription(
                "是否启用功能2。默认 true。\n" +
                "给传送带设置图标标签信号 SIGNAL-404 后，消耗经过该传送带的物品：\n" +
                "标签数量 0（默认）= 全部消耗；填 N = 每统计周期消耗 N 个（周期见 StatPeriodSeconds）。\n" +
                "例：数量填 000600，游戏内显示 600，即每分钟消耗 600 个。", null));

            StatPeriodSeconds = config.Bind(feature2, "StatPeriodSeconds", 60f, new ConfigDescription(
                "消耗速率的统计周期（秒），默认 60（1 分钟）。\n" +
                "标签数量 N 表示每周期消耗 N 个物品；消耗按物品个数计、以整堆货物为单位扣减\n" +
                "（一堆最多 4 个：全是 4 堆叠货物时，600 个/分钟平均只需消耗约 150 堆）。",
                new AcceptableValueRange<float>(0.1f, 3600f)));
        }
    }
}
