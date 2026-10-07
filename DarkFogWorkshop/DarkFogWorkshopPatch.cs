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
            /// 每个材料槽位各自的"凑够 1 个材料"进度。
            /// 每帧每格只有 `U×Y` 点算力，而材料单价可能高达 300 点：
            /// 若不按槽位累积，`floor(97/300)` 恒为 0，核心素这类材料**永远产出 0 个**。
            /// 数组下标 = 槽位下标 - OutputSlotFirst；换物品时清零。
            /// </summary>
            internal readonly double[] materialPartial = new double[OutputSlotCapacity];
            internal readonly int[] materialPartialItem = new int[OutputSlotCapacity];
            /// <summary>正在兑换的材料种数。</summary>
            internal int activeKinds;
            /// <summary>上次打诊断日志的帧号，用于限流。</summary>
            internal long lastDiagTick;
        }

        private static readonly ConditionalWeakTable<StationComponent, State> states =
            new ConditionalWeakTable<StationComponent, State>();

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
                    // 先扣矩阵、再花点数，顺序不能反：否则本帧的 pool 是上帧的剩余，会算少。
                    double pool = consumed * matrixPoints;
                    double poolBefore = pool;

                    ref StationStore matrixSlot = ref storage[MatrixSlot];
                    matrixSlot.count -= consumed;
                    if (matrixSlot.inc > matrixSlot.count)
                    {
                        matrixSlot.inc = matrixSlot.count;
                    }

                    // 先把矩阵点数按"各槽位本帧配额"分配成每槽的份额，再各花各的。
                    // 这样第一个槽位不会把共享池吃光（否则表现为"总是先兑换第 2 格"）。
                    double totalBudget = 0.0;
                    for (int s = OutputSlotFirst; s <= lastOutput; s++)
                    {
                        int idx = s - OutputSlotFirst;
                        if (unitCost[idx] > 0.0)
                        {
                            totalBudget += creditPerMaterial;
                        }
                    }

                    for (int s = OutputSlotFirst; s <= lastOutput; s++)
                    {
                        int idx = s - OutputSlotFirst;
                        double costPerUnit = unitCost[idx];
                        if (costPerUnit <= 0.0 || pool <= 0.0)
                        {
                            continue;
                        }
                        ref StationStore slot = ref storage[s];
                        double room = slot.max - slot.count;
                        if (room <= 0.0)
                        {
                            continue;
                        }
                        // 本槽位这份矩阵点数：按配额占比分，且不超过池里剩下的
                        double share = totalBudget > 0.0 ? poolBefore * (creditPerMaterial / totalBudget) : 0.0;
                        double allowed = Math.Min(share, pool);

                        double want = Math.Floor(materialPoints[idx] / costPerUnit);
                        double byShare = Math.Floor(allowed / costPerUnit);
                        if (want > byShare)
                        {
                            want = byShare;
                        }
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
                        double spent = add * costPerUnit;
                        pool -= spent;
                        state.materialPartial[idx] = Math.Max(0.0, state.materialPartial[idx] - spent);
                        produced += add;
                        producedByItem = AddCount(producedByItem, slot.itemId, add);
                    }
                    usedPoints = poolBefore - pool;
                }

                state.unusedPoints = Math.Max(0.0, points - usedPoints);
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
                $"[诊断] 塔{station.id} 阶段={stage} {extra} 本帧算力={BudgetPerTick():0.##} " +
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

        /// <summary>每帧算力 = U × Y × SpeedMultiplier。</summary>
        internal static double BudgetPerTick()
        {
            double perTick = ModConfig.MatrixPointPerFrame.Value
                             * TechSpeedScale()
                             * ModConfig.SpeedMultiplier.Value;
            return perTick > 0.0 ? perTick : 0.0;
        }

        /// <summary>科技研究速度 Y（倍率，100% → 1.0）。</summary>
        internal static double TechSpeedScale()
        {
            if (ModConfig.UseGameTechSpeed.Value)
            {
                try
                {
                    int speed = GameMain.history != null ? GameMain.history.techSpeed : 0;
                    if (speed > 0)
                    {
                        return speed / 100.0;
                    }
                }
                catch (Exception)
                {
                    // 存档未就绪时退回配置值。
                }
            }
            return Math.Max(1, ModConfig.TechSpeedPercent.Value) / 100.0;
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

        /// <summary>统计某个槽位上的材料种数（用于界面显示）。</summary>
        internal static int CountMaterials(StationComponent station)
        {
            StationStore[] storage = station?.storage;
            if (storage == null)
            {
                return 0;
            }
            int lastOutput = Math.Min(OutputSlotLast, storage.Length - 1);
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
        /// 排版（1.3.3 起）：
        ///   第 N 槽位（消耗/产出）：物品名
        ///   缩进一行写 消耗/生成速度（同一行）
        ///   空行 → 科学矩阵算力表 → 空行 → 黑雾材料算力表 → 空行 → Tips
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
            double perTick = BudgetPerTick();
            double perMinute = perTick * 60.0;

            int kinds = CountMaterials(station);
            double matrixUnit = storage != null && storage.Length > 0 ? MatrixPointsOf(storage[MatrixSlot].itemId) : 0.0;

            // —— 第 1 槽位（消耗）——
            if (storage != null && storage.Length > 0)
            {
                int matrixId = storage[MatrixSlot].itemId;
                // 只给括号里的"消耗"着色，其余文字保持原色（整行着色看不清）。
                sb.AppendLine(Line(DarkFogWorkshop.Tr("第 1 槽位", "Slot 1")
                                  + "（" + Consume(DarkFogWorkshop.Tr("消耗", "input")) + "）："
                                  + ItemLabel(matrixId)));
                if (matrixUnit > 0.0 && kinds > 0)
                {
                    double perKind = perMinute / matrixUnit;
                    sb.AppendLine(Line(
                        DarkFogWorkshop.Tr(" 消耗速度 ", " consume ") + $"{perKind:0.##}" + DarkFogWorkshop.Tr("/分钟（单种）", "/min (single)")
                        + DarkFogWorkshop.Tr(" 合计 ", " total ") + $"{perKind * kinds:0.##}"
                        + DarkFogWorkshop.Tr($"/分钟（{kinds} 种合计）", $"/min ({kinds} kinds)")));
                }
                else
                {
                    sb.AppendLine(Line(DarkFogWorkshop.Tr(" 消耗速度 0/分钟（材料未在兑换）", " consume 0/min (idle)")));
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
                                      + "（" + Produce(DarkFogWorkshop.Tr("产出", "output")) + "）："
                                      + ItemLabel(storage[s].itemId)));
                    sb.AppendLine(Line(
                        DarkFogWorkshop.Tr(" 生成速度 ", " produce ")
                        + $"{perMinute / cost:0.##}" + DarkFogWorkshop.Tr("/分钟", "/min")));
                }
                if (!any)
                {
                    sb.AppendLine();
                    sb.AppendLine(Line(DarkFogWorkshop.Tr("（第 2 格起还没有放黑雾材料）", "(no dark fog material in slot 2+)")));
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
            sb.AppendLine(Line(TableSectionHeader(DarkFogWorkshop.Tr("黑雾材料算力", "Dark fog materials"))));
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

        /// <summary>分段小标题行（黑雾材料算力）：占第一列，第二列留空。</summary>
        private static string TableSectionHeader(string title)
        {
            return Cell2(title, "", false);
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

        /// <summary>生成速度（增产色）。</summary>
        private static string Produce(string text) => $"<color={ProduceColor}>{text}</color>";

        /// <summary>消耗速度（加速色）。</summary>
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
