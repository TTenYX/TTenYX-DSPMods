using BepInEx.Configuration;

namespace SmartWarningIcons
{
    /// <summary>
    /// 矿脉枯竭警告：矿机所采矿脉量低于阈值时显示「即将减产」。
    /// 支持按矿脉总量（默认）或单矿点最小量判断；只作用于真正的矿脉矿机，抽水机/抽油机不误报。
    /// 矿物利用达到 390 级或资源无限时，可自动不再显示该图标。
    /// </summary>
    internal static class VeinDepletionSign
    {
        // 原版矿物利用是 3606 号无限级科技，每级把采矿消耗系数乘小一步（UnlockFunction 20）。
        private const int VeinUtilizationTechId = 3606;
        private const int HideUtilizationLevel = 390;

        private static ConfigEntry<bool> _enable;
        private static ConfigEntry<bool> _useTotal;
        private static ConfigEntry<long> _threshold;
        private static ConfigEntry<bool> _hideAtHighUtilization;

        internal static void Init(ConfigFile config)
        {
            _enable = config.Bind(
                "VeinDepletion",
                "Enable",
                true,
                "Take over the vein-depletion warning so its threshold and amount mode follow this mod's config.\n" +
                "接管矿脉枯竭警告，使阈值与判断方式按本插件配置生效。");

            _useTotal = config.Bind(
                "VeinDepletion",
                "UseTotalVeinAmount",
                true,
                "true = sum the amount over every vein spot the miner covers; false = use the single smallest vein spot.\n" +
                "true = 按矿机覆盖的所有矿点总量判断；false = 按单个最小矿点的量判断。");

            _threshold = config.Bind(
                "VeinDepletion",
                "WarnThreshold",
                1000L,
                "Show the cut-production-soon sign when the compared amount is below this value.\n" +
                "判断量低于该值时显示「即将减产」警告。");

            _hideAtHighUtilization = config.Bind(
                "VeinDepletion",
                "HideAtHighUtilization",
                true,
                "Hide the vein-depletion sign once Vein Utilization reaches level 390 or resources are infinite.\n" +
                "矿物利用达到 390 级或资源无限时，不再显示「即将减产」图标。");
        }

        internal static bool Enabled => _enable.Value;

        internal static void ScanMiners(FactorySystem fs)
        {
            var signPool = fs.factory.entitySignPool;
            var veinPool = fs.factory.veinPool;
            var useTotal = _useTotal.Value;
            var threshold = _threshold.Value;

            // 满级矿物利用或无限资源下，耗尽警告失去意义；整帧把原版写下的 sign 7 清成正常。
            var hideAll = _hideAtHighUtilization.Value && VeinWarningIsMeaningless();

            // 真机 minerCapacity 为 private；minerCursor 只遍历已分配槽位。
            for (var i = 1; i < fs.minerCursor; i++)
            {
                ref var miner = ref fs.minerPool[i];
                if (miner.id != i || miner.type != EMinerType.Vein)
                    continue;

                ref var sign = ref signPool[miner.entityId].signType;
                if (sign != SignData.NONE && sign != SignData.CUT_PRODUCTION_SOON)
                    continue;

                long amount;
                if (useTotal)
                {
                    miner.GetTotalVeinAmount(veinPool);
                    amount = miner.totalVeinAmount;
                }
                else
                {
                    // 另一台矿机也可能挖低共享矿点，不等本机下一次扣矿才刷新缓存。
                    miner.GetMinimumVeinAmount(fs.factory, veinPool);
                    amount = miner.minimumVeinAmount;
                }

                sign = !hideAll && amount < threshold ? SignData.CUT_PRODUCTION_SOON : SignData.NONE;
            }
        }

        private static bool VeinWarningIsMeaningless()
        {
            var data = GameMain.data;
            if (data?.gameDesc?.isInfiniteResource == true)
                return true;
            var history = data?.history;
            // 空历史对象没有 techStates 字典，history.TechState 会空引用，先行保护。
            return history?.techStates != null &&
                history.TechState(VeinUtilizationTechId).curLevel >= HideUtilizationLevel;
        }
    }
}
