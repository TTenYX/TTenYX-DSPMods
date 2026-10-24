using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SmartDustbin
{
    /// <summary>每个储物仓的判定结果缓存；键为弱引用，储物仓对象被回收时条目随之释放。</summary>
    internal sealed class BinState
    {
        public bool Resolved;
        public bool Voidable;
        public PlanetFactory Factory;
    }

    /// <summary>
    /// 功能1（储物仓部分）：小型/大型储物仓在「自动化输入限制 = 0」（bans &gt;= size）时，
    /// 拦截全部入库路径并直接消耗：
    /// - StorageComponent.AddItem(itemId, count, inc, out remainInc, useBan)：
    ///   分拣器 / 开采器 / 无人机等自动化入库（PlanetFactory.InsertInto / InsertIntoStorage）；
    /// - StorageComponent.AddCargo(ref cargo, useBan)：传送带末端与四向分流器落料
    ///   （PlanetFactory.InsertCargoIntoStorage）；
    /// - StorageComponent.AddItemStacked：手动 Ctrl 点击快速入库（UIStorageGrid）；
    /// - StorageItem.AddItem(itemId, count, startIndex, length, inc, out remainInc)：手动拖放入库。
    ///
    /// 补丁位于调用方（PlanetFactory.InsertInto*）的 lastFullItem 缓存门控之下：
    /// 只要本补丁生效就返回「全部收下」，门控不会置位；配置热更新时再主动清理可能过期的
    /// lastFullItem，保证任何切换顺序下都不会留下挡住消耗的脏缓存。
    /// 判定条件完全基于原版持久化状态（bans / 建筑原型 ID），本 MOD 不写入任何存档数据。
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.High)] // 设计书要求：优先级高于其他垃圾桶 MOD
    internal static class StorageVoidPatch
    {
        internal const int SmallStorageProtoId = 2101; // 小型储物仓（ItemProtoSet: storage-1）
        internal const int LargeStorageProtoId = 2102; // 大型储物仓（ItemProtoSet: storage-2）

        private static ConditionalWeakTable<StorageComponent, BinState> _states =
            new ConditionalWeakTable<StorageComponent, BinState>();

        /// <summary>
        /// 配置热更新时调用：整体换表（ConditionalWeakTable 不支持清空），
        /// 并清理所有「输入限制=0」储物仓上调用方缓存的 lastFullItem。
        /// </summary>
        internal static void InvalidateCache()
        {
            _states = new ConditionalWeakTable<StorageComponent, BinState>();
            var data = GameMain.data;
            if (data?.factories == null) return;
            for (int i = 0; i < data.factoryCount; i++)
            {
                var pool = data.factories[i]?.factoryStorage?.storagePool;
                if (pool == null) continue;
                for (int j = 1; j < pool.Length; j++)
                {
                    var storage = pool[j];
                    // 只重置运行时优化标记（lastFullItem/lastEmptyItem/searchStart），无功能影响
                    if (storage != null && storage.id == j && storage.bans >= storage.size)
                    {
                        storage.ResetOptimizationFlags();
                    }
                }
            }
        }

        private static bool IsVoid(StorageComponent storage, out PlanetFactory factory)
        {
            factory = null;
            if (!ModConfig.EnableStorageVoid.Value) return false;
            var state = _states.GetValue(storage, _ => new BinState());
            if (!state.Resolved)
            {
                state.Resolved = true;
                Resolve(storage, state);
            }
            factory = state.Factory;
            return state.Voidable;
        }

        private static void Resolve(StorageComponent storage, BinState state)
        {
            state.Voidable = false;
            state.Factory = null;
            if (storage.id <= 0 || storage.entityId <= 0) return;
            if (storage.bans < storage.size) return; // 「自动化输入限制」不为 0：普通储物仓
            var factory = FindFactory(storage);
            if (factory == null) return;
            int protoId = factory.entityPool[storage.entityId].protoId;
            bool enabled =
                protoId == SmallStorageProtoId ? ModConfig.EnableSmallStorage.Value :
                protoId == LargeStorageProtoId ? ModConfig.EnableLargeStorage.Value : false;
            if (!enabled) return; // 物流站仓储 / 黑雾基地等共用 StorageComponent 的建筑不在此列
            state.Factory = factory;
            state.Voidable = true;
        }

        private static PlanetFactory FindFactory(StorageComponent storage)
        {
            var data = GameMain.data;
            if (data == null) return null;
            var factories = data.factories;
            for (int i = 0; i < data.factoryCount; i++)
            {
                var factory = factories[i];
                if (factory == null) continue;
                var pool = factory.factoryStorage?.storagePool;
                if (pool == null || storage.id >= pool.Length) continue;
                if (ReferenceEquals(pool[storage.id], storage)) return factory;
            }
            return null;
        }

        /// <summary>分拣器 / 开采器 / 无人机等自动化入库路径。</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.AddItem),
            new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
        private static bool AddItem_Prefix(StorageComponent __instance, int itemId, int count,
            ref int remainInc, ref int __result)
        {
            if (count <= 0 || !IsVoid(__instance, out var factory)) return true;
            remainInc = 0;
            __result = count;
            VoidUtil.RegisterConsume(factory, itemId, count);
            return false;
        }

        /// <summary>传送带末端 / 四向分流器上方的落料路径（与 soarqin/Dustbin 063daff 同款返回语义）。</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.AddCargo))]
        private static bool AddCargo_Prefix(StorageComponent __instance, ref Cargo cargo, ref bool __result)
        {
            if (cargo.item <= 0 || cargo.stack <= 0 || !IsVoid(__instance, out var factory)) return true;
            __result = true;
            VoidUtil.RegisterConsume(factory, cargo.item, cargo.stack);
            return false;
        }

        /// <summary>手动 Ctrl 点击快速入库路径。</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.AddItemStacked))]
        private static bool AddItemStacked_Prefix(StorageComponent __instance, int itemId, int count,
            ref int remainInc, ref int __result)
        {
            if (count <= 0 || !IsVoid(__instance, out var factory)) return true;
            remainInc = 0;
            __result = count;
            VoidUtil.RegisterConsume(factory, itemId, count);
            return false;
        }

        /// <summary>手动拖放入库（指定格范围）路径。</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.AddItem),
            new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        private static bool AddItemRange_Prefix(StorageComponent __instance, int itemId, int count,
            ref int remainInc, ref int __result)
        {
            if (count <= 0 || !IsVoid(__instance, out var factory)) return true;
            remainInc = 0;
            __result = count;
            VoidUtil.RegisterConsume(factory, itemId, count);
            return false;
        }

        /// <summary>「自动化输入限制」滑条变动时刷新已有缓存（拖动会高频触发，Resolve 开销可忽略）。</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.SetBans))]
        private static void SetBans_Postfix(StorageComponent __instance)
        {
            if (_states.TryGetValue(__instance, out var state))
            {
                state.Resolved = true;
                Resolve(__instance, state);
            }
        }
    }
}
