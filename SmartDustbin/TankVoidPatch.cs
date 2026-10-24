using HarmonyLib;

namespace SmartDustbin
{
    /// <summary>
    /// 功能1（储液罐部分）：「输入」开关为关（inputSwitch == false）时，储液罐变为垃圾桶，
    /// 消耗所有输入的液体（只消耗液体类物品）：
    /// - TankComponent.GameTick 前置补丁：对每条输入侧皮带执行与原版同款的取货
    ///   （CargoTraffic.TryPickItemAtRear + ItemProto.fluids，只取液体），取到即消耗，不入罐；
    ///   原方法照常运行——输入开关为关时原版的输入分支本来就不会执行，输出侧不受影响。
    /// - UITankWindow._OnUpdate 前置补丁：手持液体按住罐体时，按原版节奏（每次 1~2 个）
    ///   直接从手上消耗，不入罐。
    /// 已在罐内的存量不动：仍可手动取出，或经输出侧正常排出。
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.High)] // 设计书要求：优先级高于其他垃圾桶 MOD
    internal static class TankVoidPatch
    {
        private static readonly AccessTools.FieldRef<UITankWindow, bool> _pointerPress =
            AccessTools.FieldRefAccess<UITankWindow, bool>("pointerPress");
        private static readonly AccessTools.FieldRef<UITankWindow, bool> _pointerPut =
            AccessTools.FieldRefAccess<UITankWindow, bool>("pointerPut");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TankComponent), nameof(TankComponent.GameTick))]
        private static void TankGameTick_Prefix(ref TankComponent __instance, PlanetFactory factory)
        {
            if (!ModConfig.EnableStorageVoid.Value || !ModConfig.EnableTank.Value) return;
            if (__instance.inputSwitch || !__instance.isBottom) return;
            var traffic = factory.cargoTraffic;
            VoidInputBelt(traffic, factory, __instance.belt0, __instance.isOutput0);
            VoidInputBelt(traffic, factory, __instance.belt1, __instance.isOutput1);
            VoidInputBelt(traffic, factory, __instance.belt2, __instance.isOutput2);
            VoidInputBelt(traffic, factory, __instance.belt3, __instance.isOutput3);
        }

        private static void VoidInputBelt(CargoTraffic traffic, PlanetFactory factory, int beltId, bool isOutput)
        {
            if (beltId <= 0 || isOutput) return;
            byte stack, inc;
            // 与原版输入分支同款取法：不指定物品、用液体掩码，因此只会取到液体类货物
            int itemId = traffic.TryPickItemAtRear(beltId, 0, ItemProto.fluids, out stack, out inc);
            if (itemId > 0)
            {
                VoidUtil.RegisterConsume(factory, itemId, stack);
            }
        }

        /// <summary>
        /// 手动向「输入=关」的储液罐倒液体：按原版 1~2 个/次的节奏从手上消耗。
        /// 返回 false 跳过原方法本帧，避免原版把手里剩下的液体倒进罐里；
        /// 抽取方向（未按住放置）不受影响。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UITankWindow), nameof(UITankWindow._OnUpdate))]
        private static bool TankWindowUpdate_Prefix(UITankWindow __instance)
        {
            if (!ModConfig.EnableStorageVoid.Value || !ModConfig.EnableTank.Value) return true;
            if (__instance.tankId == 0 || __instance.factory == null || __instance.storage == null) return true;
            if (!_pointerPress(__instance) || !_pointerPut(__instance)) return true;
            var pool = __instance.storage.tankPool;
            if (__instance.tankId >= pool.Length) return true;
            var tank = pool[__instance.tankId];
            if (tank.id != __instance.tankId || tank.inputSwitch) return true;
            var player = __instance.player;
            if (player == null || player.inhandItemId <= 0 || player.inhandItemCount <= 0) return true;
            if (!ItemProto.isFluid(player.inhandItemId)) return true; // 储液罐只消耗液体

            int itemId = player.inhandItemId;
            int num = player.inhandItemCount <= 1 ? 1 : 2; // 与原版注入节奏一致
            player.UseHandItems(num, out _);
            VoidUtil.RegisterConsume(__instance.factory, itemId, num);
            if (player.inhandItemCount <= 0)
            {
                player.SetHandItems(0, 0);
            }
            return false;
        }
    }
}
