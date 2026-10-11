using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 黑雾工坊核心逻辑：挂在 PlanetTransport.GameTick 之后，每帧检查全部物流塔的槽位。
    ///
    /// 触发条件（三个都满足）：
    ///   1. 第 1 格放的是科学矩阵（电磁/能量/结构/信息/引力/宇宙）；
    ///   2. 第 1 格的【本地需求】已勾选（StationStore.localLogic == Demand）；
    ///   3. 第 2/3/4 格中至少有一格放了黑雾材料。
    ///
    /// 兑换规则：
    ///   每帧每个材料槽位各自拿到 `U × Y × 倍率` 点算力（**每种材料独立一份，互不挤占**），
    ///   花掉这些点数需要消耗第 1 格的矩阵；矩阵点数按 `算力 = X × 材料算力` 分配。
    ///   一帧花不完的算力不结转。
    ///   例：3 种材料同时在换，每种都是单种材料时的 1 倍速，矩阵消耗量也是 3 倍。
    ///
    /// 耗电：
    ///   每有一种材料在进行兑换，该塔的耗电功率就乘一倍，倍率 = 正在兑换的材料种数。
    /// </summary>
    [HarmonyPatch]
    public static class DarkFogWorkshopPatch
    {
        /// <summary>第 1 格（索引 0）必须放科学矩阵。</summary>
        internal const int MatrixSlot = 0;
        /// <summary>第 2/3/4 格（索引 1..3）默认放黑雾材料。</summary>
        internal const int OutputSlotFirst = 1;
        internal const int OutputSlotLast = 3;
        /// <summary>材料槽位数上限，实际用 OutputSlotLimit()。</summary>
        private const int OutputSlotCapacity = 20;

        /// <summary>参与兑换的最后一个槽位下标（可用配置扩展到扩容后的格子）。</summary>
        private static int OutputSlotLastIndex(StationStore[] storage)
        {
            int limit = ModConfig.OutputSlotLimit != null ? ModConfig.OutputSlotLimit.Value : (OutputSlotLast + 1);
            int last = limit - 1;
            if (last > OutputSlotCapacity)
            {
                last = OutputSlotCapacity;
            }
            if (last >= storage.Length)
            {
                last = storage.Length - 1;
            }
            return last;
        }

        /// <summary>黑雾材料 ID（取自游戏常量 ItemProto.kDF*，不是民间误传的编法）。</summary>
        internal const int DarkFogMatrixId = 5201;
        internal const int SiliconNeuronId = 5202;
        internal const int MatterReassemblerId = 5203;
        internal const int NegentropySingularityId = 5204;
        internal const int CoreElementId = 5205;
        internal const int EnergyFragmentId = 5206;

        /// <summary>每个材料算力的下限，避免 X 很小时出现"0 算力白嫖"。</summary>
        private const double MinPointsPerMaterial = 1.0;
        /// <summary>槽位空着时，进度最多攒到"够买 N 个材料"的点数（防止长期堵塞堆出天文数字）。</summary>
        private const double PartialCapUnits = 64.0;

        internal class State
        {
            /// <summary>本帧是否在工作。</summary>
            internal bool active;
            /// <summary>本帧真正消耗的矩阵个数。</summary>
            internal int consumedMatrices;
            /// <summary>本帧真正产出的材料个数。</summary>
            internal int producedTotal;
            /// <summary>本帧没用掉、直接作废的算力。</summary>
            internal double unusedPoints;
            /// <summary>不足以买下 1 个矩阵时，攒到"够 1 个矩阵"的临时进度（上限 1 个矩阵）。</summary>
            internal double partialPoints;
            /// <summary>
            /// 上次见到的第 1 格矩阵物品 id。用来识别"玩家换了矩阵类型"：
            /// 一旦变化就清空全部算力累积，用新矩阵从头开始结算。
            /// </summary>
            internal int matrixPartialItem;
            /// <summary>
            /// 每个材料槽位各自的"凑够 1 个材料"进度。
            /// 每帧每格只有 `U×Y` 点算力，而材料单价可能高达 300 点：
            /// 若不按槽位累积，`floor(97/300)` 恒为 0，核心素这类材料**永远产出 0 个**。
            /// 数组下标 = 槽位下标 - OutputSlotFirst；换物品时清零。
            /// </summary>
            internal readonly double[] materialPartial = new double[OutputSlotCapacity];
            internal readonly int[] materialPartialItem = new int[OutputSlotCapacity];
            /// <summary>正在兑换的材料种数。</summary>
            internal int activeKinds;
            /// <summary>是否启用了元数据自动兑换对应科学矩阵。</summary>
            internal bool metadataExchangeEnabled;
            /// <summary>元数据兑换的分数矩阵进度（按每分钟速率累积到整数个）。</summary>
            internal double metadataPartial;
            /// <summary>上次打诊断日志的帧号，用于限流。</summary>
            internal long lastDiagTick;
        }

        private static readonly ConditionalWeakTable<StationComponent, State> states =
            new ConditionalWeakTable<StationComponent, State>();
        private static readonly HashSet<StationComponent> metadataStations = new HashSet<StationComponent>();

        internal static void SetMetadataExchangeEnabled(StationComponent station, bool enabled)
        {
            if (station == null)
            {
                return;
            }
            State state = GetState(station);
            state.metadataExchangeEnabled = enabled;
            state.metadataPartial = 0.0;
            if (enabled)
            {
                metadataStations.Add(station);
            }
            else
            {
                metadataStations.Remove(station);
            }
        }

        internal static bool IsMetadataExchangeEnabled(StationComponent station)
        {
            return station != null && states.TryGetValue(station, out State state)
                && state.metadataExchangeEnabled;
        }

        internal static bool IsMetadataExchangeEligible(StationComponent station)
        {
            if (station == null || station.storage == null || station.storage.Length <= MatrixSlot)
            {
                return false;
            }
            StationStore matrix = station.storage[MatrixSlot];
            return matrix.localLogic == ELogisticStorage.None && IsScienceMatrix(matrix.itemId);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameMain), "Update")]
        public static void GameMain_Update_Postfix()
        {
            if (metadataStations.Count == 0 || GameMain.data == null || DSPGame.propertySystem == null)
            {
                return;
            }

            List<StationComponent> remove = null;
            foreach (StationComponent station in metadataStations)
            {
                if (station == null || !states.TryGetValue(station, out State state)
                    || !state.metadataExchangeEnabled
                    || !IsMetadataExchangeEligible(station)
                    || !TryExchangeMetadataMatrix(station))
                {
                    if (remove == null)
                    {
                        remove = new List<StationComponent>();
                    }
                    remove.Add(station);
                }
            }
            if (remove != null)
            {
                for (int i = 0; i < remove.Count; i++)
                {
                    StationComponent station = remove[i];
                    metadataStations.Remove(station);
                    if (station != null && states.TryGetValue(station, out State state))
                    {
                        state.metadataExchangeEnabled = false;
                        state.metadataPartial = 0.0;
                    }
                }
            }
        }

        private static bool TryExchangeMetadataMatrix(StationComponent station)
        {
            State state = GetState(station);
            StationStore[] storage = station.storage;
            if (storage == null || storage.Length <= MatrixSlot)
            {
                return false;
            }

            StationStore matrix = storage[MatrixSlot];
            int room = matrix.max - matrix.count;
            if (room <= 0)
            {
                state.metadataPartial = 0.0;
                return true;
            }

            long seedKey = GameMain.data.GetClusterSeedKey();
            long available = DSPGame.propertySystem.GetItemAvaliableProperty(seedKey, matrix.itemId);
            if (available <= 0)
            {
                state.metadataPartial = 0.0;
                return true;
            }

            double perFrame = Math.Max(1, ModConfig.MetadataMatrixPerMinute.Value) / 3600.0;
            state.metadataPartial += perFrame;
            int take = (int)Math.Min(Math.Floor(state.metadataPartial), Math.Min((long)room, available));
            if (take <= 0)
            {
                return true;
            }

            DSPGame.propertySystem.AddItemConsumption(seedKey, matrix.itemId, take);
            GameMain.data.history.AddPropertyItemConsumption(matrix.itemId, take, banAchievement: true);
            matrix.count += take;
            state.metadataPartial -= take;
            return true;
        }

        internal static State GetState(StationComponent station)
        {
            return states.GetOrCreateValue(station);
        }

        /// <summary>清空某座塔的运行时状态（切星、重载存档时用）。</summary>
        internal static void ResetState(StationComponent station)
        {
            if (station != null && states.TryGetValue(station, out State state))
            {
                state.active = false;
                state.consumedMatrices = 0;
                state.producedTotal = 0;
                state.unusedPoints = 0.0;
                state.partialPoints = 0.0;
                state.matrixPartialItem = 0;
                state.metadataExchangeEnabled = false;
                state.metadataPartial = 0.0;
                metadataStations.Remove(station);
                state.activeKinds = 0;
                for (int i = 0; i < state.materialPartial.Length; i++)
                {
                    state.materialPartial[i] = 0.0;
                    state.materialPartialItem[i] = 0;
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.GameTick))]
        public static void PlanetTransport_GameTick_Postfix(PlanetTransport __instance)
        {
            PlanetFactory factory = __instance.factory;
            StationComponent[] stationPool = __instance.stationPool;
            if (factory == null || stationPool == null)
            {
                return;
            }
            ProductionStatistics production = GameMain.statistics != null ? GameMain.statistics.production : null;
            FactoryProductionStat[] pool = production != null ? production.factoryStatPool : null;
            if (pool == null || factory.index < 0 || factory.index >= pool.Length)
            {
                return;
            }
            FactoryProductionStat stat = pool[factory.index];
            if (stat == null)
            {
                return;
            }

            for (int i = 1; i < __instance.stationCursor; i++)
            {
                StationComponent station = stationPool[i];
                if (station == null || station.id != i || station.isCollector || station.isVeinCollector)
                {
                    continue;
                }
                try
                {
                    Tick(station, stat.productRegister, stat.consumeRegister);
                }
                catch (Exception e)
                {
                    DarkFogWorkshop.Log.LogError($"黑雾工坊处理运输塔 {i} 时出错：{e}");
                }
            }
        }

        private static void Tick(StationComponent station, int[] productRegister, int[] consumeRegister)
        {
            StationStore[] storage = station.storage;
            State state = states.GetOrCreateValue(station);
            state.active = false;
            state.consumedMatrices = 0;
            state.producedTotal = 0;
            state.unusedPoints = 0.0;
            state.activeKinds = 0;

            if (storage == null || storage.Length <= OutputSlotFirst)
            {
                SettleDbg(station, state, "槽位不足", "");
                return;
            }

            // 条件 2：第 1 格必须是【本地需求】。
            if (storage[MatrixSlot].localLogic != ELogisticStorage.Demand)
            {
                SettleDbg(station, state, "未勾本地需求", $"第1格物品={storage[MatrixSlot].itemId} localLogic={storage[MatrixSlot].localLogic}");
                return;
            }

            int matrixId = storage[MatrixSlot].itemId;
            double matrixPoints = MatrixPointsOf(matrixId);
            if (matrixPoints <= 0.0)
            {
                SettleDbg(station, state, "第1格不是科学矩阵", $"itemId={matrixId}");
                return;
            }

            // —— 换了矩阵类型（或第 1 格被清空）⇒ 清空全部算力累积，用新矩阵从头开始 ——
            //
            // 不清空的话，旧矩阵攒下的 `partialPoints` 与各槽位
            // `materialPartial` 会带着旧口径参与新矩阵的结算，表现为"换了矩阵就不产出"。
            if (state.matrixPartialItem != matrixId)
            {
                SettleDbg(station, state, "矩阵类型变化，清空算力累积",
                    $"旧={state.matrixPartialItem} 新={matrixId} " +
                    $"旧零头={state.partialPoints:0.##}");
                state.matrixPartialItem = matrixId;
                state.partialPoints = 0.0;
                for (int i = 0; i < state.materialPartial.Length; i++)
                {
                    state.materialPartial[i] = 0.0;
                    state.materialPartialItem[i] = 0;
                }
            }

            int lastOutput = OutputSlotLastIndex(storage);

            // 每种材料独立一份每帧算力（互不挤占）。
            double creditPerMaterial = BudgetPerTick();
            state.unusedPoints = 0.0;
            if (creditPerMaterial <= 0.0)
            {
                SettleDbg(station, state, "本帧算力为0", $"矩阵单价={matrixPoints}");
                return;
            }

            double[] budget = new double[OutputSlotCapacity];
            int[] slotItem = new int[OutputSlotCapacity];
            double[] unitCost = new double[OutputSlotCapacity];
            double[] materialPoints = new double[OutputSlotCapacity];
            // 增产倍率：第 1 格矩阵带增产点数时 > 1，产出按此放大（矩阵消耗不变）。
            double incMulti = IncOutputMultiplier(station);
            if (incMulti < 1.0)
            {
                incMulti = 1.0;
            }
            int activeCount = 0;
            int roomyCount = 0;
            for (int s = OutputSlotFirst; s <= lastOutput; s++)
            {
                int idx = s - OutputSlotFirst;
                int itemId = storage[s].itemId;
                double cost = MaterialPointsOf(itemId);
                slotItem[idx] = itemId;
                unitCost[idx] = cost;
                if (cost <= 0.0)
                {
                    state.materialPartial[idx] = 0.0;
                    state.materialPartialItem[idx] = 0;
                    continue;
                }
                budget[idx] = creditPerMaterial;
                activeCount++;

                // 槽位是否还有空间。全满时不该再扣矩阵（否则矩阵白掉）。
                double room = storage[s].max - storage[s].count;
                bool hasRoom = room > 0.0;
                if (hasRoom)
                {
                    roomyCount++;
                }

                // 每个槽位自己攒进度：本帧配额买不起 1 个时不清零，
                // 攒够 1 个材料的点数再产出（否则 floor(97/300)=0，核心素永远产不出）。
                if (ModConfig.AccumulatePartialPoints.Value)
                {
                    if (state.materialPartialItem[idx] != itemId)
                    {
                        state.materialPartial[idx] = 0.0;
                        state.materialPartialItem[idx] = itemId;
                    }
                    state.materialPartial[idx] += creditPerMaterial;
                }
                else
                {
                    state.materialPartial[idx] = creditPerMaterial;
                }
                // 进度上限：槽位空着允许攒 64 个，满了只留 1 个，
                // 否则长期堵塞会把累积点数堆到几百万（实测出现过 300 万）。
                double cap = cost * (hasRoom ? PartialCapUnits : 1.0);
                if (state.materialPartial[idx] > cap)
                {
                    state.materialPartial[idx] = cap;
                }
                materialPoints[idx] = state.materialPartial[idx];
            }
            if (activeCount == 0)
            {
                SettleDbg(station, state, "第2/3/4格没有黑雾材料", $"第1格={matrixId} 数量={storage[MatrixSlot].count}");
                return;
            }

            state.activeKinds = activeCount;

            if (roomyCount == 0)
            {
                // 所有材料槽位都满了：没有产出可放，**不要扣矩阵**。
                state.consumedMatrices = 0;
                state.producedTotal = 0;
                SettleDbg(station, state, "材料槽位全满，跳过消耗",
                    $"材料种数={activeCount} 第1格={matrixId} 存量={storage[MatrixSlot].count}");
                return;
            }

            // 各槽位的配额之和就是要花掉的矩阵点数。
            double requiredPoints = activeCount * creditPerMaterial;
            state.activeKinds = activeCount;

            int consumed = 0;
            int[] producedByItem = null;
            double usedTotal = 0.0;

            lock (storage)
            {
                int available = storage[MatrixSlot].count;
                double points = requiredPoints;
                if (points > available * matrixPoints)
                {
                    // 矩阵不够：按比例削减各材料槽位的配额。
                    points = available * matrixPoints;
                    double scale = requiredPoints > 0.0 ? points / requiredPoints : 0.0;
                    for (int i = 0; i < budget.Length; i++)
                    {
                        budget[i] *= scale;
                    }
                }

                if (points > 0.0)
                {
                    // 整场只扣整数个矩阵；不足 1 个矩阵的零头靠 partialPoints 攒，等效吞吐 = 材料种数 × U×Y。
                    int wholeSpend = (int)Math.Floor(points / matrixPoints);
                    if (wholeSpend >= 1)
                    {
                        state.partialPoints = 0.0;
                        consumed = Math.Min(wholeSpend, available);
                    }
                    else if (ModConfig.AccumulatePartialPoints.Value && available > 0)
                    {
                        state.partialPoints += points;
                        if (state.partialPoints >= matrixPoints)
                        {
                            state.partialPoints -= matrixPoints;
                            if (state.partialPoints < 0.0)
                            {
                                state.partialPoints = 0.0;
                            }
                            consumed = 1;
                        }
                    }
                    else
                    {
                        state.partialPoints = 0.0;
                    }
                }

                int produced = 0;
                double usedPoints = 0.0;
                if (consumed > 0)
                {
                    // 先扣矩阵、再花点数，顺序不能反：否则本帧的结算会算少。
                    ref StationStore matrixSlot = ref storage[MatrixSlot];
                    ConsumeMatrixWithInc(ref matrixSlot, consumed);

                    // —— 产出：**只按各槽位自己累积的算力**结算，不再用"共享池 ÷ 种数"封顶 ——
                    //
                    // ⚠️ 这里曾经把每槽上限写成 `pool ÷ 材料种数`（pool = consumed × 矩阵算力），
                    // 于是产出隐含要求 **`矩阵算力 ÷ 材料种数 ≥ 材料单价`**：
                    //   宇宙矩阵 3000 ÷ 3 = 1000 ≥ 50  ⇒ 正常；
                    //   电磁矩阵   50 ÷ 3 = 16.7 < 50  ⇒ `byShare = 0`，**一颗都产不出，永久卡死**。
                    // 玩家"换成电磁矩阵后兑换就停了"就是这个原因，而且已经付出去的
                    // `materialPartial` 累积永远花不掉。
                    //
                    // 正确做法：`materialPartial[idx]` 就是该槽位的预算（由每帧 grant 累积而来），
                    // 直接用它算能产几个；矩阵侧消耗由 `requiredPoints` 驱动，本来就已经
                    // 按 `材料种数 × 每帧算力` 扣过矩阵了，两边天然平衡，不需要额外的池子。
                    for (int s = OutputSlotFirst; s <= lastOutput; s++)
                    {
                        int idx = s - OutputSlotFirst;
                        double costPerUnit = unitCost[idx];
                        if (costPerUnit <= 0.0)
                        {
                            continue;
                        }
                        ref StationStore slot = ref storage[s];
                        double room = slot.max - slot.count;
                        if (room <= 0.0)
                        {
                            continue;
                        }

                        // 增产：同一份算力多产出材料（矩阵消耗不变）。
                        // 等价于「每个材料的实际计费成本 = 成本 / 增产倍率」。
                        double effectiveCost = costPerUnit / incMulti;

                        double want = Math.Floor(state.materialPartial[idx] / effectiveCost);
                        if (want > room)
                        {
                            want = room;
                        }
                        if (want <= 0.0)
                        {
                            continue;
                        }
                        int add = (int)want;
                        slot.count += add;
                        double spent = add * effectiveCost;
                        state.materialPartial[idx] = Math.Max(0.0, state.materialPartial[idx] - spent);
                        produced += add;
                        producedByItem = AddCount(producedByItem, slot.itemId, add);
                        usedPoints += spent;
                    }
                }

                // 买整颗矩阵花出去、但本帧没花在材料上的点数（找零），仅用于诊断。
                state.unusedPoints = Math.Max(0.0, consumed * matrixPoints - usedPoints);
                state.consumedMatrices = consumed;
                state.producedTotal = produced;
                usedTotal = usedPoints;

                if (consumed > 0)
                {
                    RegisterConsume(consumeRegister, matrixId, consumed);
                }
                if (produced > 0 && producedByItem != null && productRegister != null)
                {
                    for (int i = 0; i < producedByItem.Length; i += 2)
                    {
                        RegisterProduce(productRegister, producedByItem[i], producedByItem[i + 1]);
                    }
                }

                state.active = true;
            }

            SettleDbg(station, state, "已结算",
                $"第1格={matrixId} 槽内存量={storage[MatrixSlot].count} 单价={matrixPoints:0.##} " +
                $"需算力={requiredPoints:0.##} 用掉算力={usedTotal:0.##} 材料种数={state.activeKinds} " +
                $"本帧消耗={consumed} 本帧产出={state.producedTotal}");
            if (ModConfig.VerboseLog.Value && (consumed > 0 || state.producedTotal > 0))
            {
                LogSlotDetail(station, storage, lastOutput, materialPoints, unitCost, consumed, state);
            }
        }

        /// <summary>诊断用：把每个材料槽位的累积点数、单价、本帧产出打出来。</summary>
        private static void LogSlotDetail(StationComponent station, StationStore[] storage, int lastOutput,
            double[] materialPoints, double[] unitCost, int consumed, State state)
        {
            if (!ModConfig.VerboseLog.Value)
            {
                return;
            }
            var sb = new StringBuilder();
            sb.Append($"[槽位明细] 塔{station.id} 本帧消耗矩阵={consumed} 产出={state.producedTotal} " +
                      $"storage.Length={storage.Length} lastOutput下标={lastOutput}");
            for (int s = OutputSlotFirst; s <= lastOutput; s++)
            {
                int idx = s - OutputSlotFirst;
                ref StationStore slot = ref storage[s];
                double cost = unitCost[idx];
                sb.Append($"\n    第{s + 1}格 itemId={slot.itemId} 数量={slot.count}/{slot.max} " +
                          $"单价={cost:0.##} 累积点数={materialPoints[idx]:0.##} " +
                          $"可换={(cost > 0.0 ? Math.Floor(materialPoints[idx] / cost) : 0)}");
            }
            DarkFogWorkshop.Log.LogInfo(sb.ToString());
        }



        /// <summary>诊断日志：同一座塔每 2 秒最多一条。</summary>
        private static void SettleDbg(StationComponent station, State state, string stage, string extra)
        {
            if (!ModConfig.DiagnosticLog.Value)
            {
                return;
            }
            if (GameMain.gameTick - state.lastDiagTick < 120)
            {
                return;
            }
            state.lastDiagTick = GameMain.gameTick;
            DarkFogWorkshop.Log.LogInfo(
                $"[诊断] 塔{station.id} 阶段={stage} {extra} 每分钟算力={CreditPerMinute():0.##} " +
                $"消耗矩阵={state.consumedMatrices} 产出={state.producedTotal} 材料种数={state.activeKinds}");
        }

        private static int[] AddCount(int[] pairs, int itemId, int count)
        {
            if (pairs == null)
            {
                return new[] { itemId, count };
            }
            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (pairs[i] == itemId)
                {
                    pairs[i + 1] += count;
                    return pairs;
                }
            }
            var next = new int[pairs.Length + 2];
            Array.Copy(pairs, next, pairs.Length);
            next[pairs.Length] = itemId;
            next[pairs.Length + 1] = count;
            return next;
        }

        private static void RegisterConsume(int[] consumeRegister, int itemId, int count)
        {
            if (consumeRegister == null || itemId <= 0 || itemId >= consumeRegister.Length)
            {
                return;
            }
            lock (consumeRegister)
            {
                consumeRegister[itemId] += count;
            }
        }

        private static void RegisterProduce(int[] productRegister, int itemId, int count)
        {
            if (productRegister == null || itemId <= 0 || itemId >= productRegister.Length)
            {
                return;
            }
            lock (productRegister)
            {
                productRegister[itemId] += count;
            }
        }

        /// <summary>
        /// 每分钟算力 = `U × Y × SpeedMultiplier`。
        ///
        /// `U`（配置项 `MatrixPointPerMinute`）本身就是**每分钟**口径，
        /// 所以这里不再乘任何帧率系数。
        /// </summary>
        internal static double CreditPerMinute()
        {
            double perMinute = ModConfig.MatrixPointPerMinute.Value
                               * TechSpeedScale()
                               * ModConfig.SpeedMultiplier.Value;
            return perMinute > 0.0 ? perMinute : 0.0;
        }

        /// <summary>
        /// 内部**单帧**算力 = 每分钟算力 ÷ <see cref="TicksPerMinute"/>。
        ///
        /// 只给逐帧结算用；对外的说明/日志一律用 <see cref="CreditPerMinute"/>。
        /// </summary>
        internal static double BudgetPerTick()
        {
            return CreditPerMinute() / TicksPerMinute;
        }

        /// <summary>游戏仿真帧率（帧/秒）。DSP 目标 60 帧。</summary>
        internal const double TicksPerSecond = 60.0;
        /// <summary>
        /// 每分钟的帧数（= 60 × 60 = 3600）。
        ///
        /// ⚠️ **换算务必用这个常量**：`每帧算力 × 60` 得到的是**每秒**算力，不是每分钟。
        /// </summary>
        internal const double TicksPerMinute = TicksPerSecond * 60.0;

        /// <summary>
        /// 矩阵转化算力速度 `Y`，返回**倍率**（`1.0` = 100%，`15.0` = 1500%）。
        ///
        /// ⚠️ **`GameMain.history.techSpeed` 本身就是倍率**，不是百分数：
        /// `ModeConfig.techSpeed` 默认 `1`（=100%），研究速度 14 级时是 `15`。
        /// 游戏面板显示 `1500%` 是因为 `UITechTree` 用
        /// `history.techSpeed.ToString("0%")` 输出 —— `ToString("0%")` 会**乘以 100**。
        ///
        /// 所以这里**直接返回 `speed`**，绝不能写成 `speed / 100`
        /// （写成 /100 会让研究速度的贡献小 100 倍）。
        ///
        /// 配置项 `TechSpeedPercent` 的口径则是百分数（`100` = 100%），
        /// 因此只在回退分支里做 `/100`。
        /// </summary>
        internal static double TechSpeedScale()
        {
            if (ModConfig.UseGameTechSpeed.Value)
            {
                try
                {
                    int speed = GameMain.history != null ? GameMain.history.techSpeed : 0;
                    if (speed > 0)
                    {
                        return speed;
                    }
                }
                catch (Exception)
                {
                    // 存档未就绪时退回配置值。
                }
            }
            return Math.Max(1, ModConfig.TechSpeedPercent.Value) / 100.0;
        }

        // ————————————————————————————————————————————————————————————
        // 增产（Proliferator）效果
        //
        // 游戏机制（IL / 反编译实证）：
        //   · `Cargo.incTableMilli[等级]` 是**增产加成比例**表，等级 0–10：
        //       0 → 0     1 → 0.125  2 → 0.20  3 → 0.225  4 → 0.25   5 → 0.275
        //       6 → 0.30  7 → 0.325  8 → 0.35  9 → 0.375 10 → 0.40
        //     （`Cargo.kIncLevelMax = 10`；`Cargo.incTable[i] * 0.1` 就是百分数）
        //   · 等级怎么来：引擎自己用 `AssemblerComponent.split_inc_level` 算
        //       `等级 = 累计增产点数 m / 物品数 n`（`num = m / n`）。
        //     所以这里同样用 **`storage.inc / storage.count`** 求平均等级。
        //   · `StationStore.inc` 是**增产点数总量**（引擎多处 `storage[i].inc += inc` 累加），
        //     不是位掩码；`Cargo.inc` 才是位掩码（kIncLevel1/2/3 = 1/2/4），两者不同。
        //
        // 加成作用于**产出侧**：同一份矩阵点数产出 `1 + 加成` 倍的材料，
        // 矩阵消耗量不变 —— 这正是"增产"的语义（同样的输入，多出产品）。
        // ————————————————————————————————————————————————————————————

        /// <summary>增产等级上限（与 `Cargo.kIncLevelMax` 一致）。</summary>
        private const int IncLevelMax = 10;

        /// <summary>
        /// 第 1 格矩阵的**平均增产等级**（0–10）。
        /// 引擎的算法是 `累计增产点数 / 物品数`（见 `split_inc_level`）。
        /// </summary>
        internal static int IncLevelOf(in StationStore matrixSlot)
        {
            int count = matrixSlot.count;
            if (count <= 0 || matrixSlot.inc <= 0)
            {
                return 0;
            }
            int level = matrixSlot.inc / count;
            if (level < 0)
            {
                return 0;
            }
            return level > IncLevelMax ? IncLevelMax : level;
        }

        /// <summary>
        /// 从矩阵槽位扣掉 <paramref name="consume"/> 个矩阵，并**按引擎同样的方式等比扣减增产点数**。
        ///
        /// ⚠️ **`StationStore.inc` 是"增产点数总量"，不是等级，也不是个数。**
        /// 100 个各带 4 点增产的矩阵，`count = 100` 而 `inc = 400`。
        ///
        /// 早先这里写的是：
        /// <code>
        /// matrixSlot.count -= consumed;
        /// if (matrixSlot.inc &gt; matrixSlot.count) matrixSlot.inc = matrixSlot.count;   // ❌
        /// </code>
        /// 这把 `inc` 当成了"和 count 同量纲的东西"，于是**只要矩阵等级 ≥ 2 就必然触发**：
        /// `inc(400) &gt; count(99)` 成立 ⇒ `inc` 被砍成 99 ⇒ 等级从 4 掉到 1，
        /// **一次消耗就销毁掉 3/4 的增产点数**，而且每帧持续销毁。
        /// 玩家"输入 4 增产点数的物品后，塔里缓存矩阵掉增产点数"就是这个原因。
        ///
        /// 正确做法与引擎 `AssemblerComponent.split_inc_level` / `LabComponent.split_inc_level`
        /// 完全一致：先求出**每个物品的点数** `inc ÷ count`，再按消耗个数等比扣，
        /// 并用余数做一次整数除法补偿。这样 `inc ÷ count`（也就是等级）保持不变。
        ///
        /// 注意这里**必须用整数运算**（引擎同样如此）：`inc`、`count` 都是 `int`，
        /// 用浮点再取整会在余数补偿上偏离引擎行为。
        /// </summary>
        internal static void ConsumeMatrixWithInc(ref StationStore matrixSlot, int consume)
        {
            if (consume <= 0)
            {
                return;
            }
            int count = matrixSlot.count;
            int inc = matrixSlot.inc;

            // 空槽位或没喷增产剂：直接扣个数即可。
            if (count <= 0 || inc <= 0)
            {
                matrixSlot.count = Math.Max(0, count - consume);
                matrixSlot.inc = 0;
                return;
            }

            // 与引擎 split_inc_level 完全相同的算法。
            int level = inc / count;                 // 每个矩阵的增产点数
            int remainder = inc - level * count;     // 除不尽的余数
            int newCount = Math.Max(0, count - consume);
            remainder -= newCount;
            int consumedInc = remainder > 0 ? (level * consume + remainder) : (level * consume);

            matrixSlot.count = newCount;
            matrixSlot.inc = Math.Max(0, inc - consumedInc);
        }

        /// <summary>增产加成比例（0.0 = 无加成，0.25 = +25%）。按等级查 `Cargo.incTableMilli`。</summary>
        internal static double IncBonusRatio(int level)
        {
            if (level <= 0)
            {
                return 0.0;
            }
            try
            {
                if (level >= Cargo.incTableMilli.Length)
                {
                    level = Cargo.incTableMilli.Length - 1;
                }
                double ratio = Cargo.incTableMilli[level];
                return ratio > 0.0 ? ratio : 0.0;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        /// <summary>增产产出倍率（1.0 = 无加成，1.25 = +25% 产出）。</summary>
        internal static double IncOutputMultiplier(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            if (storage == null || storage.Length <= MatrixSlot)
            {
                return 1.0;
            }
            return 1.0 + IncBonusRatio(IncLevelOf(in storage[MatrixSlot]));
        }

        /// <summary>科学矩阵 → 每个的算力；不是矩阵返回 0。</summary>
        internal static double MatrixPointsOf(int itemId)
        {
            switch (itemId)
            {
                case 6001: return ModConfig.ElectromagneticMatrixPoints.Value;
                case 6002: return ModConfig.EnergyMatrixPoints.Value;
                case 6003: return ModConfig.StructureMatrixPoints.Value;
                case 6004: return ModConfig.InformationMatrixPoints.Value;
                case 6005: return ModConfig.GravityMatrixPoints.Value;
                case 6006: return ModConfig.UniverseMatrixPoints.Value;
                default: return 0.0;
            }
        }

        /// <summary>黑雾材料 → 兑换一个所需的算力（已乘上 X）；不是黑雾材料返回 0。</summary>
        internal static double MaterialPointsOf(int itemId)
        {
            int basePoints;
            switch (itemId)
            {
                case DarkFogMatrixId: basePoints = ModConfig.DarkFogMatrixPoints.Value; break;
                case SiliconNeuronId: basePoints = ModConfig.SiliconNeuronPoints.Value; break;
                case MatterReassemblerId: basePoints = ModConfig.MatterReassemblerPoints.Value; break;
                case NegentropySingularityId: basePoints = ModConfig.NegentropySingularityPoints.Value; break;
                case CoreElementId: basePoints = ModConfig.CoreElementPoints.Value; break;
                case EnergyFragmentId: basePoints = ModConfig.EnergyFragmentPoints.Value; break;
                default: return 0.0;
            }
            if (basePoints <= 0)
            {
                return 0.0;
            }
            return Math.Max(MinPointsPerMaterial, basePoints * ModConfig.ExchangeFactor.Value);
        }

        internal static bool IsDarkFogMaterial(int itemId)
        {
            return MaterialPointsOf(itemId) > 0.0;
        }

        internal static bool IsScienceMatrix(int itemId)
        {
            return MatrixPointsOf(itemId) > 0.0;
        }

        /// <summary>第 1 格是否已勾选本地需求。</summary>
        internal static bool IsMatrixDemanded(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            return storage != null && storage.Length > 0 && storage[MatrixSlot].localLogic == ELogisticStorage.Demand;
        }


        /// <summary>
        /// 是否"黑雾工坊"形态：第 1 格是科学矩阵且勾了【本地需求】，第 2 格起有黑雾材料。
        /// 兑换逻辑与提示控件的显示都用它。
        /// </summary>
        internal static bool IsWorkshopShape(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            if (storage == null || storage.Length <= OutputSlotFirst)
            {
                return false;
            }
            if (storage[MatrixSlot].localLogic != ELogisticStorage.Demand)
            {
                return false;
            }
            if (!IsScienceMatrix(storage[MatrixSlot].itemId))
            {
                return false;
            }
            int lastOutput = Math.Min(OutputSlotLast, storage.Length - 1);
            for (int s = OutputSlotFirst; s <= lastOutput; s++)
            {
                if (IsDarkFogMaterial(storage[s].itemId))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 统计材料槽位上的材料种数（用于界面显示）。
        ///
        /// ⚠️ 必须用 <see cref="OutputSlotLastIndex"/>（跟随配置项 `OutputSlotLimit`），
        /// 不能写死 `OutputSlotLast` —— 否则浮窗里的「消耗速度 合计（N 种合计）」
        /// 会只算到第 4 格，而实际参与兑换的槽位更多，与「转化速度」列表对不上。
        /// </summary>
        internal static int CountMaterials(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            if (storage == null)
            {
                return 0;
            }
            int lastOutput = OutputSlotLastIndex(storage);
            int count = 0;
            for (int s = OutputSlotFirst; s <= lastOutput; s++)
            {
                if (IsDarkFogMaterial(storage[s].itemId))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 悬停提示的内容（标题由 UI 层设置为「黑雾工坊 DarkFogWorkshop」）。
        ///
        /// 排版：
        ///   第 1 槽位 ( 消耗 ) : 物品名
        ///   消耗速度 N/分钟 ( 单种 )  合计 N/分钟 ( N种合计 )
        ///   科学矩阵若喷涂增产剂可获得【额外产出】黑雾材料
        ///   当前矩阵转化算力速度 Y NNN%  ( 升级「研究速度」可提升 )
        ///   单种材料每分钟算力 = U N × Y NNN% = NNN
        ///   U为黑雾工坊初始算力可在配置文件中修改
        ///   （空行）
        ///   第 N 槽位 ( 产出 ) : 物品名
        ///   转化速度 N/分钟
        ///   （空行）
        ///   科学矩阵算力表 → 空行 → 黑雾材料算力表 → 空行 → Tips
        ///
        /// 全部速率与算力一律为**每分钟**口径。
        ///
        /// 行距：本作文本只支持 `<color>` / `<size>` 两种富文本标签（无 line-height），
        /// 所以靠"每行外层套一个放大 1.5 倍的 &lt;size&gt;，行尾用 thin space 撑住行高"来实现 1.5 倍行距。
        ///
        /// 配色参考游戏自身的"增产/加速"表示法（IL 实证）：
        ///   生成（增产）= `<color=#61D8FF>`；消耗（加速）= `<color=#ff965e>`。
        ///
        /// 对齐：1 个汉字按 2 个半角宽计（等宽字体实测比例），用 **ASCII 空格**补齐；
        /// 算力数值列**右对齐**。测量宽度前会剔除图标转义 `\key-;`（它不占显示宽度）。
        /// </summary>
        internal static string DescribeDetail(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            var sb = new StringBuilder();
            // —— 算力：全部按**每分钟**口径 ——
            // `CreditPerMinute()` 本身就是每分钟；不要再乘 60（那会变成每秒，少 60 倍）。
            double perMinute = CreditPerMinute();

            int kinds = CountMaterials(station);
            double matrixUnit = storage != null && storage.Length > 0 ? MatrixPointsOf(storage[MatrixSlot].itemId) : 0.0;

            // 增产（吃增产剂）与研究速度：在说明里展示当前加成。
            double incMulti = IncOutputMultiplier(station);
            int incLevel = storage != null && storage.Length > MatrixSlot
                ? IncLevelOf(in storage[MatrixSlot])
                : 0;
            double techScale = TechSpeedScale();
            // Y 以百分数显示（与游戏内「研究速度」一致）：1.10 → 110%。
            double techPercent = techScale * 100.0;

            // —— 第 1 槽位（消耗）——
            if (storage != null && storage.Length > 0)
            {
                int matrixId = storage[MatrixSlot].itemId;

                // —— 算力说明：放在**第 1 槽位信息之前**（按需求）——
                sb.AppendLine(Line(DarkFogWorkshop.Tr("当前矩阵转化算力速度 Y ",
                                                     "current matrix conversion speed Y ")
                    + $"{techPercent:0.#}%"
                    + "  ( " + DarkFogWorkshop.Tr("升级「研究速度」可提升",
                                                  "upgrade Research Speed") + " )"));
                sb.AppendLine(Line(DarkFogWorkshop.Tr("单种材料每分钟算力 ",
                                                     "per-material per-minute credit ")
                    + $"= U {ModConfig.MatrixPointPerMinute.Value} × Y {techPercent:0.#}%"
                    + $" = {perMinute:0.##}"));
                sb.AppendLine(Line(DarkFogWorkshop.Tr("黑雾工坊初始算力 U 可在配置文件中修改",
                                                     "U is the base credit, configurable in the config file")));
                // 与下面的槽位信息之间空一行。
                sb.AppendLine();

                // 括号/冒号用**半角加空格**（按需求示例），只给"消耗"着色。
                sb.AppendLine(Line(DarkFogWorkshop.Tr("第 1 槽位", "Slot 1")
                                  + " ( " + Consume(DarkFogWorkshop.Tr("消耗", "input")) + " ) : "
                                  + ItemLabel(matrixId)));
                if (matrixUnit > 0.0 && kinds > 0)
                {
                    double perKind = perMinute / matrixUnit;
                    sb.AppendLine(Line(
                        DarkFogWorkshop.Tr("消耗速度 ", "consume ") + $"{perKind:0.##}" + DarkFogWorkshop.Tr("/分钟", "/min")
                        + " ( " + DarkFogWorkshop.Tr("单种", "single") + " )  "
                        + DarkFogWorkshop.Tr("合计 ", "total ") + $"{perKind * kinds:0.##}" + DarkFogWorkshop.Tr("/分钟", "/min")
                        + " ( " + kinds + DarkFogWorkshop.Tr("种合计", " kinds") + " )"));
                }
                else
                {
                    sb.AppendLine(Line(DarkFogWorkshop.Tr("消耗速度 0/分钟 ( 材料未在兑换 )",
                                                          "consume 0/min ( idle )")));
                }

                // —— 增产：第 1 格矩阵喷了增产剂时，同一份矩阵多产出材料 ——
                // "额外产出"用游戏自身的增产蓝（`#61D8FF`，与装配机的增产表示一致）。
                if (incMulti > 1.0)
                {
                    sb.AppendLine(Line(DarkFogWorkshop.Tr("科学矩阵已喷涂增产剂：", "Matrix proliferated: ")
                        + Produce(DarkFogWorkshop.Tr("额外产出", "extra output"))
                        + $" +{(incMulti - 1.0) * 100.0:0.#}%"
                        + DarkFogWorkshop.Tr($" ( 等级 {incLevel} )", $" ( lv {incLevel} )")
                        + DarkFogWorkshop.Tr($" → 转化 ×{incMulti:0.###} 黑雾材料",
                                             $" -> x{incMulti:0.###} dark fog material")));
                }
                else
                {
                    // 未喷增产剂时也说明这个机制存在，"额外产出"同样用增产蓝。
                    sb.AppendLine(Line(DarkFogWorkshop.Tr("科学矩阵若喷涂增产剂可获得",
                                                          "Spraying the slot-1 matrix grants ")
                        + Produce(DarkFogWorkshop.Tr("额外产出", "extra output"))
                        + DarkFogWorkshop.Tr("黑雾材料", " dark fog material")));
                }
            }

            // —— 第 2/3/... 槽位（产出）——
            if (storage != null)
            {
                int lastOutput = OutputSlotLastIndex(storage);
                bool any = false;
                for (int s = OutputSlotFirst; s <= lastOutput; s++)
                {
                    double cost = MaterialPointsOf(storage[s].itemId);
                    if (cost <= 0.0)
                    {
                        continue;
                    }
                    any = true;
                    sb.AppendLine();
                    sb.AppendLine(Line(DarkFogWorkshop.Tr($"第 {s + 1} 槽位", $"Slot {s + 1}")
                                      + " ( " + Produce(DarkFogWorkshop.Tr("产出", "output")) + " ) : "
                                      + ItemLabel(storage[s].itemId)));
                    // 转化速度已含增产加成。
                    sb.AppendLine(Line(
                        DarkFogWorkshop.Tr("转化速度 ", "convert ")
                        + $"{perMinute * incMulti / cost:0.##}" + DarkFogWorkshop.Tr("/分钟", "/min")));
                }
                if (!any)
                {
                    sb.AppendLine();
                    sb.AppendLine(Line(DarkFogWorkshop.Tr("( 第 2 格起还没有放黑雾材料 )",
                                                          "( no dark fog material in slot 2+ )")));
                }
            }

            // —— 算力表：**单列两列**（名称 | 算力），全部交给游戏表格模式排版 ——
            //
            // 从"四列并排"改回单列的原因：四列 + 图标 + 中文名称的宽度需求接近提示框上限，
            // 实测把列宽调大后引擎会压缩列、右对齐数值反而各行漂移（1.5.1 比 1.5.0 更乱）。
            // 单列只有 2 列、宽度需求约 260px，约束简单，能一次调准。
            //
            // 表格靠"制表符分列 + Text.tableColWidths/tableRowHeights"（游戏自带机制），
            // 行数必须与 `UIStationWindowPatch.TipTableRowHeights` 的数组长度一致。
            sb.AppendLine();
            sb.AppendLine(Line(TableHeader2()));
            sb.AppendLine(Line(TableRow2(6001, MatrixPointsOf(6001))));
            sb.AppendLine(Line(TableRow2(6002, MatrixPointsOf(6002))));
            sb.AppendLine(Line(TableRow2(6003, MatrixPointsOf(6003))));
            sb.AppendLine(Line(TableRow2(6004, MatrixPointsOf(6004))));
            sb.AppendLine(Line(TableRow2(6005, MatrixPointsOf(6005))));
            sb.AppendLine(Line(TableRow2(6006, MatrixPointsOf(6006))));
            // 两段之间空一行（表格内空行，见 `TableBlankRow`）。
            sb.AppendLine(Line(TableBlankRow()));
            sb.AppendLine(Line(TableSectionHeader(DarkFogWorkshop.Tr("黑雾材料", "Dark fog"),
                                                   DarkFogWorkshop.Tr("算力", "Pts"))));
            sb.AppendLine(Line(TableRow2(EnergyFragmentId, MaterialPointsOf(EnergyFragmentId))));
            sb.AppendLine(Line(TableRow2(DarkFogMatrixId, MaterialPointsOf(DarkFogMatrixId))));
            sb.AppendLine(Line(TableRow2(SiliconNeuronId, MaterialPointsOf(SiliconNeuronId))));
            sb.AppendLine(Line(TableRow2(NegentropySingularityId, MaterialPointsOf(NegentropySingularityId))));
            sb.AppendLine(Line(TableRow2(MatterReassemblerId, MaterialPointsOf(MatterReassemblerId))));
            sb.AppendLine(Line(TableRow2(CoreElementId, MaterialPointsOf(CoreElementId))));

            sb.AppendLine();
            sb.AppendLine(Line(DarkFogWorkshop.Tr("Tips: 各物品算力可在配置文件中修改",
                "Tips: point values are configurable in the config file")));

            return sb.ToString().TrimEnd();
        }

        // ————————————————————————————————————————————————————————————
        // 算力表：**两列**（左=图标+名称，右=算力数值），**完全交给游戏表格排版**。
        //
        // 单元格用**制表符 `\t`** 分隔；列宽、对齐由 `UIStationWindowPatch` 施加到
        // `Text` 组件上（`tableColWidths` / `tableColAlignments` / `tableRowHeights`）。
        //
        // IL 实证的引擎行为：
        //   · `Populate`：`coords[i+1] = coords[i] + widths[i]`（纯像素累加）
        //   · `ChangeTableCell`：按 `[coords[col], coords[col+1]]` 定位，按
        //     `tableColAlignments[col]` 对齐（长度不足时静默回退到 `textAnchor`）
        //   · `useTable` 要求 `tableColWidths` 与 `tableRowHeights` **都非空**
        //
        // ⚠️ **不要在文本里补空格**：列对齐由引擎完成，补空格是在重复实现它，
        //    而且必然对不准（空格与数字宽度比不等于 2:1，实测每多一位数字右移 ≈3px）。
        // ————————————————————————————————————————————————————————————

        /// <summary>表头：`科学矩阵 | 算力`。</summary>
        private static string TableHeader2()
        {
            return Cell2(DarkFogWorkshop.Tr("科学矩阵", "Matrix"), DarkFogWorkshop.Tr("算力", "Pts"), false);
        }

        /// <summary>数据行：图标 + 名称 | 算力数值。</summary>
        private static string TableRow2(int itemId, double value)
        {
            return Cell2(ItemLabel(itemId), ValueText(value), true);
        }

        /// <summary>
        /// 分段小标题行（黑雾材料算力）。**必须也走两列**：
        /// 之前把「黑雾材料算力」当成一个整串塞进第一列，结果六个字占满名称字段、
        /// 与「算力」之间没有任何空隙，看起来和上面的「科学矩阵　算力」表头不一致。
        /// 现在拆成 `黑雾材料` + 制表符 + `算力`，与表头、数据行列位完全相同。
        /// </summary>
        private static string TableSectionHeader(string name, string value)
        {
            return Cell2(name, value, false);
        }

        /// <summary>
        /// 表格内的**空行**（两段之间空一行用）。
        ///
        /// 在表格模式下不能插普通空文本行 —— 那会被当成一个"行有内容、下一行是新行"，
        /// 破坏列结构。这里给它一个**宽度为 0 的单元格对**（制表符仍在），
        /// 因此仍是一个合法表格行，只是看不见内容。
        /// </summary>
        private static string TableBlankRow()
        {
            return "\t";
        }

        /// <summary>
        /// 拼一行：**名称在左、算力在右**，交给游戏表格排版（制表符分列）。
        ///
        /// **原版机制（IL 实证）**：
        /// - `TextGenerator.Populate`：`coords[i+1] = coords[i] + tableColWidths[i]`（纯像素累加）
        /// - `TextGenerator.ChangeTableCell`：按 `[coords[col], coords[col+1]]` 定位并做水平对齐
        ///
        /// ⚠️ **制表符是按位置对齐的**：名称列内容不足时，制表符会落在列内较早的位置，
        ///    导致后续数值提前对齐。实测「核心素」（3 字名）的数值左边缘比其它行偏左 77px。
        ///
        /// 因此**所有行的名称都补齐到同一个固定宽度 `NameFieldUnits`**（表头也用同一个
        /// 函数，所以表头「算力」与所有数值落在同一列）。名称列左对齐、紧贴列左边界，
        /// 所以空格必须补在名称与制表符之间。
        /// </summary>
        private static string Cell2(string name, string valueText, bool hasIcon)
        {
            // 名称显示宽度（显示单位）：CJK=2、ASCII=1；图标另计。
            string plain = StripIconTags(name);
            int nameUnits = DisplayUnits(plain);
            if (hasIcon && name.Length > plain.Length)
            {
                nameUnits += IconUnits;
            }

            int pad = Math.Max(NameValueGapSpaces, NameFieldUnits - nameUnits);
            return name + new string(' ', pad) + "\t" + valueText;
        }

        /// <summary>
        /// 名称字段固定宽度（显示单位）。**所有行（含表头）都补齐到这里**，
        /// 这样制表符位置恒定、数值列（第二列）左边界一致。
        ///
        /// 取值依据：最长名称「物质重组器」= 图标4 + 5汉字10 = 14 单位，
        /// 再加间隔 10 单位 ≈ 70px（满足"名称与算力之间空隙要明显"的要求）。
        /// </summary>
        private const int NameFieldUnits = 24;
        /// <summary>物品图标占的显示单位（图标 ≈28px ≈ 4 单位）。</summary>
        private const int IconUnits = 4;
        /// <summary>名称与算力之间的**最小**间隔（空格数）。实测 1 空格 ≈ 7px。</summary>
        private const int NameValueGapSpaces = 4;

        /// <summary>文本的显示宽度单位：CJK/全角=2、ASCII=1（与实测 ≈13px / ≈7px 的 2:1 一致）。</summary>
        private static int DisplayUnits(string text)
        {
            int units = 0;
            for (int i = 0; i < text.Length; i++)
            {
                units += text[i] > 0x2000 ? 2 : 1;
            }
            return units;
        }

        /// <summary>
        /// 行内容 —— **重新用 `<size>` 标签撑 1.5 倍行距**。
        ///
        /// 历史教训：1.4.6 用这个写法时行距是正常的；后来我改成 `Text.lineSpacing`
        /// 属性，行距就坏了。**已改回这个可用写法，不要再动。**
        ///
        /// 做法：每行前面放一个放大到 `14 × 1.5 = 21` 的字号标签，内嵌一个 thin space
        /// （几乎不可见）把行框撑高 —— 本作文本只有 `&lt;color&gt;` / `&lt;size&gt;`
        /// 两种富文本标签，没有 line-height。
        /// </summary>
        private static string Line(string content)
        {
            int big = (int)Math.Round(BaseFontSize * LineHeightScale);
            return $"<size={big}>\u2009</size>{content}";
        }

        /// <summary>基础字号（推定 14；行高 = 14 × 1.5 = 21）。</summary>
        private const int BaseFontSize = 14;
        /// <summary>行高倍率（1.5 倍行距）。</summary>
        private const float LineHeightScale = 1.5f;
        /// <summary>生成（增产）配色——取自游戏 `UIItemTip.SetTip` / `UIReferenceSpeedTipEntry`。</summary>
        private const string ProduceColor = "#61D8FF";
        /// <summary>消耗（加速）配色——取自游戏 `UIReferenceSpeedTipEntry.Refresh` / `UIMechaWindow`。</summary>
        private const string ConsumeColor = "#ff965e";

        /// <summary>产出侧（增产色）：用于「产出」「额外产出」等字样。</summary>
        private static string Produce(string text) => $"<color={ProduceColor}>{text}</color>";

        /// <summary>消耗侧（加速色）：用于「消耗」字样。</summary>
        private static string Consume(string text) => $"<color={ConsumeColor}>{text}</color>";

        /// <summary>
        /// 物品显示名（供提示文本用）。
        ///
        /// 游戏支持在文本里用 `\图标键;` 插入物品图标（locale 里的 `\jzyjz1-;` 就是这种转义）。
        /// `ItemProto` 有已拼好的 `iconTagString` 字段，反射能拿到就内嵌图标，
        /// 拿不到就退回纯名字——**绝不因取图标失败而影响提示内容**。
        /// </summary>
        internal static string ItemLabel(int itemId)
        {
            string tag = TryGetIconTag(itemId);
            string name = DarkFogWorkshop.ItemName(itemId);
            return tag != null ? tag + name : name;
        }

        private static readonly Dictionary<int, string> iconTagCache = new Dictionary<int, string>();

        private static string TryGetIconTag(int itemId)
        {
            if (iconTagCache.TryGetValue(itemId, out string cached))
            {
                return cached;
            }
            string tag = null;
            try
            {
                ItemProto proto = LDB.items.Select(itemId);
                if (proto != null)
                {
                    var field = proto.GetType().GetField("iconTagString",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        string value = field.GetValue(proto) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            tag = value;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkFogWorkshop.Log.LogInfo($"[提示] 取物品 {itemId} 图标标签失败（将用纯名字）：{e.GetType().Name}");
            }
            iconTagCache[itemId] = tag;   // 失败也缓存（null = 确认没有），避免每帧反复反射
            return tag;
        }

        // ————————————————————————————————————————————————————————————
        // 四列算力表：**完全采用游戏自带的表格排版**
        //
        // 单元格用**制表符 `\t` 分隔**，列宽/对齐由 `Text` 组件的
        // `tableColWidths` / `tableColAlignments` 决定（见 `UIStationWindowPatch`）。
        // 依据是游戏自己的指引布局文件 `Wiki/Tutorial/tutorial-14-zh.txt`：
        //
        //     {$Text|...;tablecolwidths=0.2,0.2,0.2,0.2,0.2;tablerowheights=30,30,30,30,30,30;
        //            tablehorzalignments=0,0,0,0,0;tablevertalignments=1,1,1,1,1;tablelinecolor=#00000000}
        //     增产剂类型	增产点数	加速生产	额外产出	额外耗电
        //     Mk.I	1	25.00%	12.50%	30%
        //
        // ⚠️ 两个数组都必须非空，否则 `useTable` 为 false、表格属性被忽略。
        // ————————————————————————————————————————————————————————————

        /// <summary>算力数值的显示文本。</summary>
        private static string ValueText(double value)
        {
            return value > 0.0 ? ((long)value).ToString() : "—";
        }

        /// <summary>
        /// 诊断用（**默认关闭**）：排版自检。当前列对齐由文本层空格补齐实现，
        /// 需要排查时再填充具体内容。
        /// </summary>
        internal static string DescribeTableMetrics()
        {
            return string.Empty;
        }

        /// <summary>剔除图标转义 `\key-;`（不占显示宽度）。</summary>
        internal static string StripIconTags(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
            {
                return text ?? string.Empty;
            }
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    int end = text.IndexOf(';', i + 1);
                    if (end > i)
                    {
                        i = end;   // 跳过整段转义
                        continue;
                    }
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>按等宽字体估算显示宽度（半角字符数）。CJK 与全角标点按 2 计。</summary>
        private static int DisplayWidth(string text)
        {
            int width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                width += text[i] > 0x2000 ? 2 : 1;
            }
            return width;
        }
    }
}
