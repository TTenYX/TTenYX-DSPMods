using System.Collections.Generic;
using HarmonyLib;

namespace SmartDustbin
{
    /// <summary>
    /// 功能2：给传送带设置图标标签信号 SIGNAL-404（原版信号，ID 404）后，消耗经过该传送带的物品。
    /// - 标签数量为 0（默认）：全部消耗；
    /// - 标签数量为 N &gt; 0：按「每统计周期（StatPeriodSeconds，默认 60s）N 个」的速率消耗。
    ///   速率按物品个数计，实际以整堆货物为单位扣减（一堆最多 4 个）：
    ///   全是 4 堆叠货物时 N=600/min，平均每分钟消耗约 150 堆，误差不超过一堆。
    ///
    /// 实现说明：
    /// - 注册表不做事件挂钩：每 tick 对每个工厂环形扫描一小段传送带（自愈式维护），
    ///   以 entitySignPool[belt.entityId].iconId0 == 404 为唯一事实来源。
    ///   原因：蓝图粘贴（BuildTool_BlueprintPaste）直接写 iconId0，不经过
    ///   CargoTraffic.SetBeltSignalIcon，纯事件法会漏登记。
    /// - 逻辑 tick 钩子挂在 GameLogic.LogicFrame() 后置：0.10.35 逻辑重构后原
    ///   GameData.GameTick 已不存在；LogicFrame 在所有工厂 tick 完成后于主线程返回，
    ///   暂停时不会被调用。取货窗口与 soarqin/Dustbin 同款（pivotOnPath-5 起 12 字节）。
    /// - 消耗量计入产量统计的「消耗」（consumeRegister），可在统计面板观察实际效率。
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.High)]
    internal static class BeltSignalVoidPatch
    {
        internal const int SignalId = 404; // SIGNAL-404（原版信号）
        private const int ScanPerTick = 16; // 每工厂每 tick 扫描的传送带数：约 2000 条带 2s 覆盖一遍

        private sealed class FactoryState
        {
            public readonly Dictionary<int, float> Budgets = new Dictionary<int, float>();
            public int ScanCursor;
        }

        private static readonly Dictionary<int, FactoryState> _states = new Dictionary<int, FactoryState>();
        private static readonly List<int> _deadBelts = new List<int>();
        private static readonly List<int> _tickBelts = new List<int>(); // TickBelts 的键快照（禁止在字典迭代中写字典）
        private static long _lastTick = -1;

        /// <summary>进新存档 / 读档时清空全部状态（预算与扫描游标全部重来）。</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameMain), nameof(GameMain.Begin))]
        private static void GameMain_Begin_Postfix()
        {
            _states.Clear();
            _lastTick = -1;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.LogicFrame))]
        private static void LogicFrame_Postfix()
        {
            var data = GameMain.data;
            if (data == null || data.factoryCount <= 0) return;

            long tick = GameMain.gameTick;
            if (_lastTick < 0) _lastTick = tick;
            long dt = tick - _lastTick;
            _lastTick = tick;
            if (dt <= 0) dt = 1;
            if (dt > 600) dt = 600; // 长时间暂停/加载后不做巨额补偿

            bool enabled = ModConfig.EnableBeltSignalVoid.Value;
            float periodTicks = ModConfig.StatPeriodSeconds.Value * 60f;

            for (int i = 0; i < data.factoryCount; i++)
            {
                var factory = data.factories[i];
                if (factory == null) continue;
                if (!_states.TryGetValue(factory.index, out var state))
                {
                    state = new FactoryState();
                    _states[factory.index] = state;
                }
                ScanStep(factory, state);
                if (enabled && state.Budgets.Count > 0)
                {
                    TickBelts(factory, state, dt, periodTicks);
                }
            }
        }

        /// <summary>环形扫描：把「带 404 标签且存活」的传送带登记进预算表，其余移除。</summary>
        private static void ScanStep(PlanetFactory factory, FactoryState state)
        {
            var traffic = factory.cargoTraffic;
            if (traffic == null) return;
            var belts = traffic.beltPool;
            int count = traffic.beltCursor - 1; // 有效传送带编号 1..beltCursor-1
            if (count <= 0)
            {
                state.Budgets.Clear();
                return;
            }
            var signs = factory.entitySignPool;
            int cursor = state.ScanCursor;
            for (int k = 0; k < ScanPerTick; k++)
            {
                cursor = cursor >= count ? 1 : cursor + 1;
                var belt = belts[cursor];
                if (belt.id != cursor)
                {
                    state.Budgets.Remove(cursor); // 槽位空闲（传送带已拆）
                    continue;
                }
                bool tagged = belt.entityId > 0 && belt.entityId < signs.Length
                    && signs[belt.entityId].iconId0 == SignalId;
                if (tagged)
                {
                    if (!state.Budgets.ContainsKey(cursor)) state.Budgets[cursor] = 0f;
                }
                else
                {
                    state.Budgets.Remove(cursor);
                }
            }
            state.ScanCursor = cursor;
        }

        private static void TickBelts(PlanetFactory factory, FactoryState state, long dt, float periodTicks)
        {
            var traffic = factory.cargoTraffic;
            var belts = traffic.beltPool;
            var signs = factory.entitySignPool;

            int[] consumeRegister = null;
            var statPool = GameMain.statistics?.production?.factoryStatPool;
            if (statPool != null && factory.index >= 0 && factory.index < statPool.Length)
            {
                consumeRegister = statPool[factory.index]?.consumeRegister;
            }

            _deadBelts.Clear();
            // 取键快照后再逐条处理：Mono 的 Dictionary 在 foreach 中即使只赋值也可能让枚举器失效
            _tickBelts.Clear();
            foreach (var key in state.Budgets.Keys) _tickBelts.Add(key);
            for (int n = 0; n < _tickBelts.Count; n++)
            {
                int beltId = _tickBelts[n];
                if (!state.Budgets.TryGetValue(beltId, out float budgetValue)) continue;
                if (beltId >= belts.Length) { _deadBelts.Add(beltId); continue; }
                var belt = belts[beltId];
                if (belt.id != beltId) { _deadBelts.Add(beltId); continue; }
                if (belt.entityId <= 0 || belt.entityId >= signs.Length) { _deadBelts.Add(beltId); continue; }
                var path = traffic.GetCargoPath(belt.segPathId);
                if (path == null) continue;

                // 与 Dustbin 同款取货窗口：覆盖本条传送带前后约一条带宽
                int index = belt.segIndex + belt.segPivotOffset - 5;
                float quota = signs[belt.entityId].count0; // 每周期配额，UI 可随时改
                byte stack, inc;
                int itemId;

                if (quota <= 0f)
                {
                    // 全部消耗：把窗口内的货物一次取空（guard 只是把关，正常窗口内不超过 2 堆）
                    int guard = 0;
                    while ((itemId = path.TryPickItem(index, 12, out stack, out inc)) > 0)
                    {
                        if (consumeRegister != null && itemId < consumeRegister.Length)
                        {
                            consumeRegister[itemId] += stack;
                        }
                        if (++guard >= 24) break;
                    }
                }
                else
                {
                    // 限速消耗：预算按 quota 个/periodTicks 连续补充，上限 quota（最多攒一个周期）
                    float budget = budgetValue + quota * dt / periodTicks;
                    if (budget > quota) budget = quota;
                    while (budget >= 1f && (itemId = path.TryPickItem(index, 12, out stack, out inc)) > 0)
                    {
                        budget -= stack; // 整堆扣减，允许一次取货略微超出预算（误差不超过一堆）
                        if (consumeRegister != null && itemId < consumeRegister.Length)
                        {
                            consumeRegister[itemId] += stack;
                        }
                    }
                    state.Budgets[beltId] = budget;
                }
            }
            foreach (var id in _deadBelts)
            {
                state.Budgets.Remove(id);
            }
        }
    }
}
