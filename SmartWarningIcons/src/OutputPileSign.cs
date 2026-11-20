using BepInEx.Configuration;
using HarmonyLib;

namespace SmartWarningIcons
{
    /// <summary>在原版更新建筑状态后，只扫描当前已加载的本地工厂。</summary>
    internal static class OutputPileSign
    {
        private static ConfigEntry<bool> _enable;

        internal static void Init(ConfigFile config)
        {
            _enable = config.Bind(
                "OutputPile",
                "Enable",
                true,
                "Show the yellow warning only when production stalls because a finished batch cannot fit in the output buffer.\n" +
                "产物缓冲区装不下已完成的一批产物，导致建筑停工时，才显示黄色「产物堆砌」图标。");
        }

        private static void ScanFactory(FactorySystem fs, int currentTech, bool redEnabled, bool yellowEnabled)
        {
            var signPool = fs.factory.entitySignPool;
            for (var i = 1; i < fs.assemblerCursor; i++)
            {
                ref var machine = ref fs.assemblerPool[i];
                if (machine.id != i || machine.recipeId <= 0)
                    continue;

                UpdateProductionSign(ref signPool[machine.entityId].signType, machine.recipeExecuteData,
                    machine.time, machine.replicating, machine.served, redEnabled, yellowEnabled);
            }

            for (var i = 1; i < fs.labCursor; i++)
            {
                ref var lab = ref fs.labPool[i];
                if (lab.id != i)
                    continue;

                ref var sign = ref signPool[lab.entityId].signType;
                if (lab.researchMode)
                {
                    // 矩阵点数由原版按当前科技统一更新；科技切换当帧不拿新需求判断旧科技。
                    if (redEnabled && currentTech > 0 && lab.techId == currentTech && !lab.replicating &&
                        CanRefine(sign) && HasShortage(lab.matrixServed, LabComponent.matrixPoints))
                        sign = SignIcons.SignTypeInsufficientInput;
                }
                else if (lab.recipeId > 0)
                {
                    UpdateProductionSign(ref sign, lab.recipeExecuteData, lab.time, lab.replicating,
                        lab.served, redEnabled, yellowEnabled);
                }
            }
        }

        private static bool CanRefine(uint sign) => sign == SignData.NONE || sign == SignData.NOT_WORKING;

        private static void UpdateProductionSign(ref uint sign, RecipeExecuteData recipe, int time, bool replicating,
            int[] served, bool redEnabled, bool yellowEnabled)
        {
            // 不覆盖供电等专用状态；正在生产时，即使缓冲区已经满也不预警。
            if (!CanRefine(sign) || replicating || recipe == null || recipe.timeSpend <= 0)
                return;

            // 原版出料失败会停在完成线上；若只是缺下一轮原料，原版会把进度归零。
            // 不重复实现各类设备各自的出料容量判断，也不以存量预测停工。
            if (time >= recipe.timeSpend)
            {
                if (yellowEnabled)
                    sign = SignIcons.SignTypeOutputPile;
            }
            else if (redEnabled && HasShortage(served, recipe.requireCounts))
                sign = SignIcons.SignTypeInsufficientInput;
        }

        private static bool HasShortage(int[] served, int[] counts)
        {
            if (served == null || counts == null || served.Length != counts.Length)
                return false;
            for (var i = 0; i < counts.Length; i++)
                if (counts[i] > 0 && served[i] < counts[i])
                    return true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.OnFactoryFrameEnd))]
        private static void OnFactoryFrameEnd()
        {
            var redEnabled = InsufficientInputSign.Enabled;
            var yellowEnabled = _enable.Value;
            var veinEnabled = VeinDepletionSign.Enabled;
            if (!redEnabled && !yellowEnabled && !veinEnabled)
                return;

            var data = GameMain.data;
            var factory = data?.localLoadedPlanetFactory;
            if (factory?.factorySystem == null)
                return;

            if ((redEnabled || yellowEnabled) && SignIcons.HealAtlas())
                ScanFactory(factory.factorySystem, data.history?.currentTech ?? 0, redEnabled, yellowEnabled);
            if (veinEnabled)
                VeinDepletionSign.ScanMiners(factory.factorySystem);
        }
    }
}
