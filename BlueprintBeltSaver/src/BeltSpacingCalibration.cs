using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// 传送带长度模型：**按本次实际粘贴位置与朝向精确建模**。
    ///
    /// 蓝图把每个建筑存成"相对区域锚点的格偏移"：
    ///   x = (经度 - 区域参考经度) / GetLongitudeRadPerGrid(区域参考纬度)
    ///   y = (纬度 - 区域参考纬度) / GetLatitudeRadPerGrid()
    ///   z = (半径 - realRadius - 0.2) / 1.3333333
    /// 粘贴时游戏用完全相同的逆变换重建（BlueprintUtils.RefreshBuildPreview ≈1946 行）：
    ///   refLat(根) = 粘贴锚点纬度；refLat(子) = refLat(父) + sign * anchorLocalOffsetY * LatPerGrid
    ///   refLon(子) = refLon(父) + lonSign * tropicAnchor * LonRadPerGrid(refLat(父))
    ///                          - lonSign * anchorLocalOffsetX * LonRadPerGrid(refLat(子))
    ///   lpos = GetDir(refLon + lonSign * lonAxis * LonRadPerGrid(refLat), clamp(refLat + sign * latAxis * LatPerGrid))
    ///          * (z * 1.3333333 + 0.2 + realRadius)
    /// 而超限判据是 BuildTool_BlueprintPaste ≈2087 行的
    ///   (preview.output.lpos - preview.lpos).sqrMagnitude > 5.3   → 「距离太远」
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// 为什么旧模型（每个区域一个常数格宽）会翻车
    ///
    /// 一格 x 的弧长 = 半径 × cos(节点纬度) × 2π/(区域分段×5)。**区域分段数由区域参考纬度决定，
    /// 对整片区域是一个常数**，而节点纬度 = 参考纬度 + sign·y·0.36°，可以离参考纬度很远。
    /// 6k太阳帆 是"单区域、锚点在 47° 带、节点一路铺到赤道"的图：同一个 Δx 在锚点处是 1.43 m、
    /// 在赤道端是 2.10 m。旧模型整片按锚点纬度取 cos，把赤道端的跨度算小 1.47 倍 —— 模型 1.61 m
    /// 的一节，游戏量出来 2.37 m，超过 2.3022 m 硬上限。这一版**按节点自己的纬度逐点取弧长**，
    /// 根因不再存在；同时**不再回退**到旧模型。
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// 为什么不需要"枚举所有合法粘贴姿态 × 纬度格"
    ///
    /// 上一版为了提高安全性，对每个区域枚举 4 个偏航象限、每个象限再按 0.05° 切纬度格，逐格
    /// 把全部证据链节算一遍取最坏值。这带来两个后果：① 计算量爆炸（6k 要 1~2 秒，落在按钮
    /// 与粘贴热路径上，玩家能直接感觉到掉帧）；② 判据变成"在**任何**合法纬度上都不能超限"，
    /// 于是单区域整球图（游戏允许粘到任意纬度）几乎找不到可用纬度 → 省带量被压到 0，
    /// 这正是玩家说的"绝大部分传送带无法长带化"。
    ///
    /// 本版改为**一次算清**：MOD 挂在粘贴流程上，能拿到本次粘贴的锚点位置与朝向（
    /// <see cref="Observation"/>），于是每个节点的经度格宽、纬度、3D 位置都是唯一确定的，
    /// 一趟 O(节点数) 就够（实测 3000 条带 &lt; 20 ms）。蓝图库场景（此刻还不知道粘到哪）用
    /// **蓝图自身锚点纬度**，并在粘贴工具拿到真实位置后按新位置**增量重算**。
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// 精度上限：粘贴锚点纬度是怎么来的
    ///
    /// 单区域蓝图的 tropicAnchor / anchorLocalOffsetX / anchorLocalOffsetY 全是 0（实测 37.5小火箭、
    /// 2880太阳帆、1080传送带、6k太阳帆 均如此），文件里**不存在**任何绝对纬度信息，所以"原始
    /// 纬度"在单区域图上不可反解（见 .work/belt-regression --sweep 输出：合格锚点纬度是一整段）。
    /// 因此"按原始纬度"落地为"按**本次粘贴锚点纬度**"：粘贴工具在玩家移动/旋转时会给出确切值，
    /// 此时模型误差为 0（与游戏逐点同公式）。位置未知时用蓝图自身锚点纬度，
    /// 并在位置到手后重算（见 BlueprintBeltSaverPlugin）。
    /// </summary>
    internal static class BeltSpacingCalibration
    {
        /// <summary>赤道处一格的名义弧长（200 m 半径星球，200 分段）。</summary>
        internal const double NominalMetresPerUnit = 2.0 * Math.PI / 1000.0 * (200.0 + 0.2);

        /// <summary>一格纬度的弧度（与 segmentCnt 无关，游戏 GetLatitudeRadPerGrid(200)）。</summary>
        internal const double LatPerGrid = 2.0 * Math.PI / 1000.0;

        private const double AltitudeMetresPerUnit = 1.3333333;
        private const double BaseRadius = 200.0 + 0.2;
        private const int SegmentCount = 200;

        /// <summary>
        /// 游戏 PlanetGrid.DetermineLongitudeSegmentCount 能返回的分段数及其纬度范围
        /// （由游戏自身 segmentTable 推出，.work/belt-regression --bandtable 逐点校验一致）。
        /// 只用于**报表与跨带提示**，不再参与长度计算。
        /// </summary>
        private static readonly int[] BandSegments = { 200, 160, 120, 100, 80, 60, 40, 32, 20, 16, 8, 4 };
        private static readonly double[] BandLowTable = { 0.0, 28.98, 46.98, 55.98, 64.98, 70.38, 75.78, 79.38, 82.98, 84.78, 86.58, 88.38 };
        private static readonly double[] BandHighTable = { 28.98, 46.98, 55.98, 64.98, 70.38, 75.78, 79.38, 82.98, 84.78, 86.58, 88.38, 90.0 };

        internal static double BandLowDeg(int segments)
        {
            int index = Array.IndexOf(BandSegments, segments);
            return index < 0 ? double.NaN : BandLowTable[index];
        }

        internal static double BandHighDeg(int segments)
        {
            int index = Array.IndexOf(BandSegments, segments);
            return index < 0 ? double.NaN : BandHighTable[index];
        }

        internal static int DisplayBandNumber(int segments)
        {
            int index = Array.IndexOf(BandSegments, segments);
            return index < 0 ? 0 : index + 1;
        }

        internal static int BandOfLatitude(double latitudeDegrees)
        {
            double abs = Math.Abs(latitudeDegrees);
            for (int i = 0; i < BandSegments.Length; i++)
            {
                if (abs <= BandHighTable[i] + 1e-9)
                {
                    return i + 1;
                }
            }
            return BandSegments.Length;
        }

        // ------------------------------------------------------------------ 游戏几何 ---------------

        /// <summary>
        /// 一 x 单位经度弧长的查表缓存。
        ///
        /// <see cref="LongitudeCells"/> 每次调用都要走一遍 Math.Cos + Math.Round +
        /// PlanetGrid.DetermineLongitudeSegmentCount；而合并过程会把它调用几百万次（每个节点两三次）。
        /// 纬度格索引只有 ±250 这 501 个可能取值，**先按格索引建表**就能把这件事降到一次数组读取。
        /// 表在每次建模时重建（常数时间），因此不存在跨蓝图的陈旧问题。
        /// </summary>
        private static readonly double[] LonRadPerGridCache = new double[LonCellCacheSize];
        private const int LonCellCacheSize = 513;
        private const int LonCellCacheOffset = 256;

        /// <summary>按纬度格索引建表；越界（|索引| &gt; 256）时退回直算。</summary>
        private static void BuildLonCache()
        {
            for (int index = -LonCellCacheOffset; index < LonCellCacheSize - LonCellCacheOffset; index++)
            {
                int magnitude = index < 0 ? -index : index;
                if (magnitude > 0)
                {
                    magnitude--;
                }
                int cells = PlanetGrid.DetermineLongitudeSegmentCount(magnitude / 5, SegmentCount) * 5;
                LonRadPerGridCache[index + LonCellCacheOffset] = cells <= 0 ? 2.0 * Math.PI / 1000.0 : 2.0 * Math.PI / cells;
            }
        }

        /// <summary>纬度（弧度）→ 纬度格索引；与游戏 GetSnappedLatitudeGridIdx 同式（四舍五入）。</summary>
        private static int LatitudeGridIndex(double latitudeRad)
            => (int)Math.Round(latitudeRad / LatPerGrid, MidpointRounding.AwayFromZero);

        /// <summary>一 x 单位经度弧度（走缓存）。</summary>
        private static double LonRadPerGridFast(double latitudeRad)
        {
            int index = LatitudeGridIndex(latitudeRad);
            if (index < -LonCellCacheOffset || index >= LonCellCacheSize - LonCellCacheOffset)
            {
                return LonRadPerGrid(latitudeRad);
            }
            return LonRadPerGridCache[index + LonCellCacheOffset];
        }

        /// <summary>
        /// 游戏自身的"一 x 单位经度弧度"：BlueprintUtils.GetLongitudeRadPerGrid(latRad)
        /// → GetLongitudeSegmentCount(latRad) → PlanetGrid.DetermineLongitudeSegmentCount(row, 200) * 5。
        /// </summary>
        internal static double LonRadPerGrid(double latitudeRad)
        {
            int cells = LongitudeCells(latitudeRad);
            return cells <= 0 ? 2.0 * Math.PI / 1000.0 : 2.0 * Math.PI / cells;
        }

        /// <summary>该纬度处一圈被切成多少格（= 分段数 × 5）。</summary>
        internal static int LongitudeCells(double latitudeRad)
        {
            // 必须与游戏逐字同式。BlueprintUtils.GetLongitudeSegmentCount(float)：
            //   num = GetSnappedLatitudeGridIdx(纬度)   // = round2int(纬度 / GetLatitudeRadPerGrid())，**有符号**
            //   if (num < 0) num = -num;                // 取绝对值发生在"取整"之后
            //   if (num > 0) num--;
            //   return PlanetGrid.DetermineLongitudeSegmentCount(num / 5, 200)
            // 本项目上一版写成 Max(0, |纬度/LatPerGrid| - 0.1) 再截断：先取绝对值再取整，还多减了 0.1。
            // 后果在南半球最大——实测 -18.6° 得到 20 格/圈（游戏是 1000 格/圈），把 1 m 的相邻链节
            // 虚报成 47 m；北半球在带边界上也会错。这里照抄游戏。
            int index = LatitudeGridIndex(latitudeRad);
            if (index < 0)
            {
                index = -index;
            }
            if (index > 0)
            {
                index--;
            }
            return PlanetGrid.DetermineLongitudeSegmentCount(index / 5, SegmentCount) * 5;
        }

        /// <summary>一 x 单位在给定纬度上的真实弧长（米）。</summary>
        internal static double ArcPerXUnit(double latitudeRad) => BaseRadius * LonRadPerGrid(latitudeRad);

        private static double ClampLat(double latitudeRad)
            => Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, latitudeRad));

        // ------------------------------------------------------------------ 粘贴观测 ---------------

        /// <summary>
        /// 本次粘贴的锚点信息：由粘贴工具在玩家放置蓝图时给出。
        /// 只保留"决定几何"的三件事——锚点纬度、偏航象限（决定换轴与两个符号）。
        /// </summary>
        internal readonly struct Observation
        {
            internal readonly double AnchorLatitudeRad;
            internal readonly int Quadrant;

            private Observation(double anchorLatitudeRad, int quadrant)
            {
                AnchorLatitudeRad = anchorLatitudeRad;
                Quadrant = quadrant;
            }

            /// <summary>偏航象限：num = floor(yaw / 89.9)，与 BlueprintUtils 一致。</summary>
            internal static int QuadrantOfYaw(float yaw)
            {
                int quadrant = (int)Math.Floor(yaw / 89.9f);
                return ((quadrant % 4) + 4) % 4;
            }

            internal static Observation FromYaw(double anchorLatitudeRad, float yaw)
                => new Observation(anchorLatitudeRad, QuadrantOfYaw(yaw));

            /// <summary>纬度符号：num 2/3 为 -1（RefreshBuildPreview 的 num3）。</summary>
            internal double LatitudeSign => (Quadrant == 2 || Quadrant == 3) ? -1.0 : 1.0;

            /// <summary>经度符号：num 1/2 为 -1（RefreshBuildPreview 的 num2）。</summary>
            internal double LongitudeSign => (Quadrant == 1 || Quadrant == 2) ? -1.0 : 1.0;

            /// <summary>90°/270° 偏航时 TransitionWidthAndHeight 把蓝图两轴互换。</summary>
            internal bool SwapAxes => Quadrant == 1 || Quadrant == 3;

            internal string Label => $"num={Quadrant}（纬向{LatitudeSign:+0;-0} 经向{LongitudeSign:+0;-0}{(SwapAxes ? " 换轴" : "")}），锚点纬度 {AnchorLatitudeRad * 180.0 / Math.PI:F3}°";
        }

        // ------------------------------------------------------------------ 结果 -------------------

        internal sealed class AreaInfo
        {
            internal int Segments;
            internal int BandNumber;
            internal double BandLow, BandHigh;
            internal double MinArcPerXUnit = double.MaxValue;
            internal double MaxArcPerXUnit;
            internal double MinNodeLatitude = double.MaxValue, MaxNodeLatitude = double.MinValue;
            internal int BandsSpanned = 1;
            internal int Nodes;
        }

        internal sealed class Result
        {
            /// <summary>按本次实际粘贴位置/朝向建立的唯一模型；null 表示拿不到位置。</summary>
            internal Observation Observation;
            internal bool HasObservation;
            internal double AnchorLatitudeRad;
            /// <summary>锚点纬度是否来自真实粘贴位置（false = 用蓝图自身锚点纬度的库模式）。</summary>
            internal bool AnchorFromPaste;
            internal readonly Dictionary<int, double> AreaRefLat = new Dictionary<int, double>();
            internal readonly Dictionary<int, double> AreaRefLon = new Dictionary<int, double>();
            internal readonly Dictionary<int, AreaInfo> Areas = new Dictionary<int, AreaInfo>();
            /// <summary>模型下最长的一条原件带-带链节；远大于硬上限说明锚点纬度与真实不符。</summary>
            internal double LongestOriginalLink;
            internal int EvidenceLinks;
            /// <summary>跨纬度带（多区域，或区域节点纬度跨度跨带）→ 打开铁律守卫 A。</summary>
            internal bool MultiBand;
            /// <summary>区域链里存在解析不了的父子关系（数据异常）。</summary>
            internal bool Malformed;

            // ---- 符号来源（二选一）--------------------------------------------------------------
            // 正常路径：HasObservation 为真，三个符号来自粘贴偏航象限 Observation。
            // 纬度搜索路径：Explicit* 为真，符号由搜索方给定（库模式没有 Observation）。
            internal bool ExplicitSigns;
            internal double ExplicitLatitudeSign = 1.0;
            internal double ExplicitLongitudeSign = 1.0;
            internal bool ExplicitSwapAxes;

            internal bool SwapAxes => HasObservation ? Observation.SwapAxes : (ExplicitSigns && ExplicitSwapAxes);
            internal double LatitudeSign => HasObservation ? Observation.LatitudeSign : (ExplicitSigns ? ExplicitLatitudeSign : 1.0);
            internal double LongitudeSign => HasObservation ? Observation.LongitudeSign : (ExplicitSigns ? ExplicitLongitudeSign : 1.0);

            internal double RefLatOf(int areaIndex)
                => AreaRefLat.TryGetValue(areaIndex, out double value) ? value : AnchorLatitudeRad;

            internal double RefLonOf(int areaIndex)
                => AreaRefLon.TryGetValue(areaIndex, out double value) ? value : 0.0;

            public override string ToString()
            {
                if (Malformed)
                {
                    return "★区域链异常，按赤道保守处理";
                }
                var parts = new List<string>();
                foreach (var pair in Areas.OrderBy(p => p.Key))
                {
                    var info = pair.Value;
                    parts.Add($"区域{pair.Key}:分段{info.Segments}→第{info.BandNumber}纬度带({info.BandLow:F2}°~{info.BandHigh:F2}°)，" +
                        $"节点纬度 {info.MinNodeLatitude:F2}°~{info.MaxNodeLatitude:F2}°（跨{info.BandsSpanned}带），" +
                        $"每x单位 {info.MinArcPerXUnit:F4}~{info.MaxArcPerXUnit:F4} m");
                }
                return string.Join("；", parts)
                    + $"；{(AnchorFromPaste ? "按本次粘贴位置" : "按蓝图自身锚点纬度")} {AnchorLatitudeRad * 180.0 / Math.PI:F3}°"
                    + (HasObservation ? $"，{Observation.Label}" : "（位置未知）")
                    + $"；证据链节 {EvidenceLinks} 条，最长原件链节 {LongestOriginalLink:F4} m"
                    + (MultiBand ? "；跨纬带" : "");
            }
        }

        // ------------------------------------------------------------------ 建模 -------------------

        /// <summary>
        /// 试算回调：给定锚点纬度（度），返回"在该位置省带能省掉多少条带"；返回 null 表示该纬度不合法，
        /// 或调用方无法试算。
        ///
        /// <paramref name="stride"/> 是调用方的降采样提示：&gt;1 时允许按该步长抽样处理链节，
        /// 用来把"粗扫 20 多个纬度"的代价压下来。挑纬度只需要相对排序，最终那一次合并仍然全量执行，
        /// 所以精度不受影响。实现见 BlueprintBeltSaverPlugin.MeasureSavingsAt。
        /// </summary>
        internal delegate int? SavingsProbe(double anchorLatDegrees, int stride);

        /// <summary>
        /// 建立本次的精确长度模型。
        /// </summary>
        /// <param name="areas">蓝图区域表。</param>
        /// <param name="buildings">蓝图建筑表。</param>
        /// <param name="isBelt">判定传送带。</param>
        /// <param name="observation">本次粘贴锚点；为空时用蓝图自身可用的锚点纬度。</param>
        /// <param name="search">
        /// 蓝图库场景（observation 为空）挑选默认锚点纬度的试算回调；为空时退回赤道（最保守）。
        /// </param>
        internal static Result Resolve(IEnumerable<BlueprintArea> areas, IEnumerable<BlueprintBuilding> buildings,
            Func<BlueprintBuilding, bool> isBelt, Observation? observation, SavingsProbe search = null)
        {
            BuildLonCache();
            var result = new Result();
            try
            {
                var areaList = areas == null ? new List<BlueprintArea>() : areas.ToList();
                if (areaList.Count == 0)
                {
                    result.Malformed = true;
                    return result;
                }
                var byIndex = new Dictionary<int, BlueprintArea>();
                foreach (var area in areaList)
                {
                    byIndex[area.index] = area;
                }
                int root = -1;
                foreach (var area in areaList)
                {
                    if (area.parentIndex < 0 || !byIndex.ContainsKey(area.parentIndex))
                    {
                        root = area.index;
                        break;
                    }
                }
                if (root < 0)
                {
                    result.Malformed = true;
                    root = areaList[0].index;
                }

                // 区域链：参考纬度只由 anchorLocalOffsetY 决定（根区域恒为 0）。
                var gridOffset = new Dictionary<int, double>();
                foreach (var area in areaList)
                {
                    gridOffset[area.index] = 0.0;
                }
                for (int pass = 0; pass < areaList.Count + 1; pass++)
                {
                    foreach (var area in areaList)
                    {
                        if (area.index == root || !byIndex.ContainsKey(area.parentIndex))
                        {
                            continue;
                        }
                        gridOffset[area.index] = gridOffset[area.parentIndex] + area.anchorLocalOffsetY;
                    }
                }

                // 建筑表只枚举一次并物化：调用方可能传的是惰性序列，而 Optimize 会就地改写
                // outputObj / inputObj，多次枚举就会拿到"已被合并过"的拓扑（实测会让证据链节
                // 变成合并后的长节，把最长原件链节从 2 m 报成 92 m）。
                var buildingSnapshot = buildings == null
                    ? new List<BlueprintBuilding>()
                    : buildings.Where(x => x != null).ToList();

                // 锚点纬度：有真实粘贴位置就用它（误差为 0）；否则用蓝图自身数据推出的可用纬度。
                double anchorLatitudeRad;
                if (observation.HasValue)
                {
                    anchorLatitudeRad = ClampLat(observation.Value.AnchorLatitudeRad);
                    result.Observation = observation.Value;
                    result.HasObservation = true;
                    result.AnchorFromPaste = true;
                }
                else
                {
                    var evidence = BeltLinks(buildingSnapshot, isBelt, byIndex);
                    anchorLatitudeRad = ChooseBestLatitude(areaList, gridOffset, root, buildingSnapshot, evidence, search);
                    result.AnchorFromPaste = false;
                }
                result.AnchorLatitudeRad = anchorLatitudeRad;
                double sign = result.HasObservation ? result.Observation.LatitudeSign : 1.0;

                foreach (var area in areaList)
                {
                    result.AreaRefLat[area.index] = ClampLat(anchorLatitudeRad + sign * gridOffset[area.index] * LatPerGrid);
                }

                // 区域参考经度：多区域时按游戏公式从根区域下推（单区域时相减会消掉，不影响距离）。
                double lonSign = result.HasObservation ? result.Observation.LongitudeSign : 1.0;
                foreach (var area in areaList)
                {
                    result.AreaRefLon[area.index] = 0.0;
                }
                for (int pass = 0; pass < areaList.Count + 1; pass++)
                {
                    foreach (var area in areaList)
                    {
                        if (area.index == root || !byIndex.ContainsKey(area.parentIndex))
                        {
                            continue;
                        }
                        int parent = area.parentIndex;
                        result.AreaRefLon[area.index] = result.AreaRefLon[parent]
                            + lonSign * area.tropicAnchor * LonRadPerGrid(result.AreaRefLat[parent])
                            - lonSign * area.anchorLocalOffsetX * LonRadPerGrid(result.AreaRefLat[area.index]);
                    }
                }

                // 逐区域统计（报表 + 跨带判定）：节点纬度、每 x 单位弧长。
                var nodesByArea = new Dictionary<int, List<BlueprintBuilding>>();
                foreach (var building in buildingSnapshot)
                {
                    if (!nodesByArea.TryGetValue(building.areaIndex, out var list))
                    {
                        list = new List<BlueprintBuilding>();
                        nodesByArea[building.areaIndex] = list;
                    }
                    list.Add(building);
                }
                bool swap = result.HasObservation && result.Observation.SwapAxes;
                foreach (var area in areaList)
                {
                    int bandIndex = Array.IndexOf(BandSegments, area.areaSegments);
                    var info = new AreaInfo
                    {
                        Segments = area.areaSegments,
                        BandNumber = bandIndex >= 0 ? bandIndex + 1 : 0,
                        BandLow = bandIndex >= 0 ? BandLowTable[bandIndex] : 0.0,
                        BandHigh = bandIndex >= 0 ? BandHighTable[bandIndex] : 90.0
                    };
                    double refLat = result.AreaRefLat[area.index];
                    var bands = new SortedSet<int>();
                    if (nodesByArea.TryGetValue(area.index, out var nodes))
                    {
                        foreach (var node in nodes)
                        {
                            double latAxis = swap ? node.localOffset_x : node.localOffset_y;
                            double latitude = ClampLat(refLat + sign * latAxis * LatPerGrid);
                            double degrees = latitude * 180.0 / Math.PI;
                            if (degrees < info.MinNodeLatitude) info.MinNodeLatitude = degrees;
                            if (degrees > info.MaxNodeLatitude) info.MaxNodeLatitude = degrees;
                            bands.Add(BandOfLatitude(degrees));
                        }
                    }
                    else
                    {
                        info.MinNodeLatitude = info.MaxNodeLatitude = refLat * 180.0 / Math.PI;
                    }
                    info.Nodes = nodes?.Count ?? 0;
                    // 节点纬度只由"该节点自己的纬度"决定（= 区域参考纬度 + 纬度轴偏移），弧长随之变化
                    // （越靠赤道越长）。报表给出的是该区域所有节点里最短/最长的那两处，仅用于人读；
                    // 计算本身逐点取值，不依赖这两个数字。
                    if (info.MinNodeLatitude == double.MaxValue)
                    {
                        info.MinNodeLatitude = info.MaxNodeLatitude = refLat * 180.0 / Math.PI;
                    }
                    double minAbsDeg = Math.Min(Math.Abs(info.MinNodeLatitude), Math.Abs(info.MaxNodeLatitude));
                    double maxAbsDeg = Math.Max(Math.Abs(info.MinNodeLatitude), Math.Abs(info.MaxNodeLatitude));
                    info.MaxArcPerXUnit = ArcPerXUnit(minAbsDeg * Math.PI / 180.0);
                    info.MinArcPerXUnit = ArcPerXUnit(maxAbsDeg * Math.PI / 180.0);
                    info.BandsSpanned = Math.Max(1, bands.Count);
                    result.Areas[area.index] = info;
                }
                result.MultiBand = areaList.Count > 1 || result.Areas.Values.Any(a => a.BandsSpanned > 1);

                // 证据链节：原件是游戏接受过的，所以在真实粘贴位置上它们必须都 ≤ 硬上限。
                // 只取"可能是真实带-带相邻"的链节（剔除整球图上跨几十格的派生连接）。
                {
                    var evidence = BeltLinks(buildingSnapshot, isBelt, byIndex);
                    double longest = 0.0;
                    foreach (var link in evidence)
                    {
                        double d = DistanceWith(result, link.a, link.b);
                        if (!double.IsNaN(d) && !double.IsInfinity(d) && d > longest)
                        {
                            longest = d;
                        }
                    }
                    result.EvidenceLinks = evidence.Count;
                    result.LongestOriginalLink = longest;
                }
            }
            catch
            {
                result.Malformed = true;
            }
            return result;
        }

        /// <summary>
        /// 蓝图库场景：从蓝图自身数据推出一个**可用**锚点纬度。
        ///
        /// 数据事实先说清楚：单区域蓝图的 tropicAnchor / anchorLocalOffsetX / anchorLocalOffsetY 全是 0
        /// （实测 37.5小火箭、2880太阳帆、1080传送带、6k太阳帆 均如此），文件里**没有**绝对纬度，
        /// 所以"原始纬度"在单区域图上不可反解——合格纬度是一整段（.work/belt-regression --sweep）。
        /// 因此这里的任务是：在这一段里挑一个"最像原件所在位置"的纬度当默认值。
        ///
        /// 判据（两条都必须满足）：
        ///   ① 该纬度是**合法粘贴位置**：不夹极（节点不许被 clamp 到 ±90°），且原件全部相邻链节
        ///      都在硬上限内——原件是从某个真实位置复制出来的，只有满足这两条的位置才可能是它。
        ///   ② 在这些合法位置里，**实测省带量最大**的那个。省带量随纬度强烈非单调（实测 16.4小火箭
        ///      在 32° 省 385、在 46° 省 0），因为 x 弧长随纬度收缩后同一段链节能多塞进几个节点。
        ///      只按"几何最紧"或"最保守"挑都会挑到 0 收益的纬度，所以这里直接拿省带量当目标函数。
        ///
        /// 这个值只是**默认猜测**：玩家一旦真的进入放置状态，粘贴工具给出的真实纬度会覆盖它并按
        /// 新位置重算（BlueprintBeltSaverPlugin.ReoptimizeForPastePosition），那时模型误差为 0。
        /// </summary>
        private static double ChooseBestLatitude(List<BlueprintArea> areaList, Dictionary<int, double> gridOffset,
            int root, IReadOnlyList<BlueprintBuilding> buildingList, List<(BlueprintBuilding a, BlueprintBuilding b)> links,
            SavingsProbe search)
        {
            const double fallback = 0.0;
            try
            {
                if (buildingList.Count == 0 || links.Count == 0)
                {
                    return fallback;
                }
                double minLatAxis = double.MaxValue, maxLatAxis = double.MinValue;
                foreach (var building in buildingList)
                {
                    double axis = building.localOffset_y;
                    if (axis < minLatAxis) minLatAxis = axis;
                    if (axis > maxLatAxis) maxLatAxis = axis;
                }
                double maxLatDeg = maxLatAxis * 180.0 / Math.PI * LatPerGrid;
                double minLatDeg = minLatAxis * 180.0 / Math.PI * LatPerGrid;

                // 链条：带-带后继关系。同一节点可能被多条链引用，这里按 output 关系走一遍就够
                // （代理指标只用来排序，不需要覆盖每条并行的链）。
                var next = new Dictionary<BlueprintBuilding, BlueprintBuilding>();
                var inDegree = new Dictionary<BlueprintBuilding, int>();
                foreach (var link in links)
                {
                    if (!next.ContainsKey(link.a))
                    {
                        next[link.a] = link.b;
                    }
                    inDegree[link.b] = (inDegree.TryGetValue(link.b, out int d) ? d : 0) + 1;
                }
                var starts = new List<BlueprintBuilding>();
                foreach (var pair in next)
                {
                    if (!inDegree.ContainsKey(pair.Key))
                    {
                        starts.Add(pair.Key);
                    }
                }
                if (starts.Count == 0)
                {
                    // 纯环：随便挑几个起点，保证代理能给出非零值。
                    int taken = 0;
                    foreach (var pair in next)
                    {
                        starts.Add(pair.Key);
                        if (++taken >= 32)
                        {
                            break;
                        }
                    }
                }
                var chains = new List<BlueprintBuilding[]>();
                var visited = new HashSet<BlueprintBuilding>();
                foreach (var start in starts)
                {
                    var chain = new List<BlueprintBuilding>();
                    var current = start;
                    while (current != null && visited.Add(current))
                    {
                        chain.Add(current);
                        next.TryGetValue(current, out current);
                    }
                    if (chain.Count >= 2)
                    {
                        chains.Add(chain.ToArray());
                    }
                }

                // ① 代理曲线：1° 网格（0.5° 收益有限但代价翻倍）。
                double bestProxyLat = fallback;
                double bestProxy = 0.0;
                var scored = new List<(double lat, double score)>();
                for (double latDeg = 0.0; latDeg <= 89.5; latDeg += 1.0)
                {
                    if (!IsLegalAnchor(latDeg, minLatDeg, maxLatDeg))
                    {
                        continue;
                    }
                    double latRad = latDeg * Math.PI / 180.0;
                    var frames = BuildFrames(areaList, gridOffset, root, latRad);
                    // 合法性用"区域常数格宽"的弦长预筛：常数格宽在带边界上可能高估也可能低估，
                    // 所以容差放到 2%；真正的把关是随后对少量候选纬度做的精确复核
                    // （ConfirmCandidates 那一步会跑完整模型 + 优化器，并在最终校验里逐条复核硬上限）。
                    if (!FramesSuggestLegal(frames, links))
                    {
                        continue;
                    }
                    double score = ProxySavings(latRad, chains, frames);
                    scored.Add((latDeg, score));
                    if (score > bestProxy)
                    {
                        bestProxy = score;
                        bestProxyLat = latRad;
                    }
                }
                if (scored.Count == 0 || bestProxy <= 0.0 || search == null)
                {
                    // 一个能省的合法纬度都没有：退回赤道（这个星球上格宽最大的位置——在该处通过
                    // 硬上限校验的合并，在其它任何合法纬度上只会更短，因此是安全的兜底）。
                    return bestProxyLat;
                }

                // ② 精确复核：代理得分最高的前几个纬度（彼此至少隔 2°）跑真优化器。
                var candidates = new List<double>();
                foreach (var entry in scored.OrderByDescending(s => s.score))
                {
                    bool tooClose = false;
                    foreach (double taken in candidates)
                    {
                        if (Math.Abs(taken - entry.lat) < 2.0)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose)
                    {
                        continue;
                    }
                    candidates.Add(entry.lat);
                    if (candidates.Count >= ConfirmCandidates)
                    {
                        break;
                    }
                }
                double bestLat = bestProxyLat;
                int bestSave = -1;
                foreach (double latDeg in candidates)
                {
                    // 精确合法性复核：代理预筛用的是常数格宽，会放过"原件在该纬度其实超限"的位置
                    // （实测 1080 在 81°/84° 就被放过，结果里有 3 条 2.5 m 的新链节）。最终候选必须
                    // 过一遍与生产模型同式的判定，代价只有一次 O(链节数)。
                    if (!OriginalFitsAt(areaList, gridOffset, root, latDeg * Math.PI / 180.0, links))
                    {
                        continue;
                    }
                    int? save = search(latDeg, 1);
                    if (save.HasValue && save.Value > bestSave)
                    {
                        bestSave = save.Value;
                        bestLat = latDeg * Math.PI / 180.0;
                    }
                }
                return bestSave > 0 ? bestLat : bestProxyLat;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>精确复核的候选纬度个数（每个候选要跑一次完整合并）。</summary>
        private const int ConfirmCandidates = 3;

        /// <summary>
        /// 省带量的**代理指标**：该纬度下"按原始链节长度能省掉多少节点"。
        ///
        /// 起止都在同一区域、且相邻节点的 x 跨度 ≤ 1.6 格，所以单链节的经度积分几乎等于
        /// "区域格宽 × Δx"（带边界那点误差对"排序用"完全够）。因此这里用**区域常数格宽**的弦长
        /// 连续累加，把一条链的几何从 O(L²) 降到 O(L)，并把"每格经度/参考经度"提到纬度循环外
        /// 每区域只算一次 —— 这样才扫得起 0.5° 网格。它只用来给候选纬度排序，绝对值不是省带量。
        /// </summary>
        private static double ProxySavings(double anchorLatRad, List<BlueprintBuilding[]> chains, AreaFrame[] frames)
        {
            double cap = BeltPathOptimizer.MaximumMeters;
            double total = 0.0;
            foreach (var chain in chains)
            {
                double acc = 0.0;
                double last = 0.0;
                int kept = 1;
                for (int i = 1; i < chain.Length; i++)
                {
                    acc += FrameArc(frames, chain[i - 1], chain[i]);
                    if (acc - last > cap)
                    {
                        kept++;
                        last = acc;
                    }
                }
                total += chain.Length - kept;
            }
            _ = anchorLatRad;
            return total;
        }

        /// <summary>
        /// 廉价的"这个纬度是不是合法粘贴位置"预筛：用区域常数格宽的弦长（不积分经度）逐条判断，
        /// 超限的直接淘汰。代价只有精确判定的几十分之一，能在整个 1° 网格上跑；真正把关的是随后
        /// 对少量候选纬度做的精确复核与最终硬上限校验。
        /// </summary>
        private static bool FramesSuggestLegal(AreaFrame[] frames, List<(BlueprintBuilding a, BlueprintBuilding b)> links)
        {
            double limit = BeltPathOptimizer.MaximumMeters * 1.02;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (FrameArc(frames, link.a, link.b) > limit)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>某个纬度下、某个区域的参考纬度/参考经度/每 x 单位经度（循环外算一次）。</summary>
        private readonly struct AreaFrame
        {
            internal readonly double RefLat;
            internal readonly double RefLon;
            internal readonly double LonPerGrid;
            internal AreaFrame(double refLat, double refLon)
            {
                RefLat = refLat;
                RefLon = refLon;
                LonPerGrid = LonRadPerGridFast(refLat);
            }
        }

        /// <summary>按锚点纬度给整张蓝图建一套区域参考系（单区域时参考经度恒为 0）。</summary>
        private static AreaFrame[] BuildFrames(List<BlueprintArea> areaList, Dictionary<int, double> gridOffset, int root,
            double anchorLatRad)
        {
            int count = 0;
            foreach (var area in areaList)
            {
                if (area.index + 1 > count)
                {
                    count = area.index + 1;
                }
            }
            var frames = new AreaFrame[count];
            var byIndex = areaList.ToDictionary(a => a.index);
            for (int i = 0; i < count; i++)
            {
                double g = gridOffset.TryGetValue(i, out double value) ? value : 0.0;
                frames[i] = new AreaFrame(ClampLat(anchorLatRad + g * LatPerGrid), 0.0);
            }
            if (areaList.Count <= 1)
            {
                return frames;
            }
            var refLon = new double[count];
            for (int pass = 0; pass < count + 1; pass++)
            {
                foreach (var area in areaList)
                {
                    if (area.index == root || !byIndex.TryGetValue(area.parentIndex, out var parent))
                    {
                        continue;
                    }
                    refLon[area.index] = refLon[parent.index]
                        + area.tropicAnchor * LonRadPerGridFast(frames[parent.index].RefLat)
                        - area.anchorLocalOffsetX * LonRadPerGridFast(frames[area.index].RefLat);
                }
            }
            for (int i = 0; i < count; i++)
            {
                frames[i] = new AreaFrame(frames[i].RefLat, refLon[i]);
            }
            return frames;
        }

        /// <summary>区域常数格宽下的单链节弦长（代理指标专用）。</summary>
        private static double FrameArc(AreaFrame[] frames, BlueprintBuilding a, BlueprintBuilding b)
        {
            int ai = a.areaIndex >= 0 && a.areaIndex < frames.Length ? a.areaIndex : 0;
            int bi = b.areaIndex >= 0 && b.areaIndex < frames.Length ? b.areaIndex : 0;
            double latA = ClampLat(frames[ai].RefLat + a.localOffset_y * LatPerGrid);
            double latB = ClampLat(frames[bi].RefLat + b.localOffset_y * LatPerGrid);
            double deltaLon = (b.localOffset_x - a.localOffset_x) * frames[ai].LonPerGrid
                + Math.IEEERemainder(frames[bi].RefLon - frames[ai].RefLon, 2.0 * Math.PI);
            double radiusA = BaseRadius + AltitudeMetresPerUnit * Math.Max(0f, a.localOffset_z);
            double radiusB = BaseRadius + AltitudeMetresPerUnit * Math.Max(0f, b.localOffset_z);
            double dx = -Math.Cos(latB) * Math.Sin(deltaLon) * radiusB;
            double dy = Math.Sin(latA) * radiusA - Math.Sin(latB) * radiusB;
            double dz = -Math.Cos(latA) * radiusA + Math.Cos(latB) * Math.Cos(deltaLon) * radiusB;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>该锚点纬度是否不夹极（节点不被 clamp 到 ±90°）。</summary>
        private static bool IsLegalAnchor(double latDeg, double minLatDeg, double maxLatDeg)
            => latDeg + maxLatDeg <= 90.0 - 1e-6 && latDeg + minLatDeg >= -90.0 + 1e-6;

        /// <summary>某锚点纬度下原件全部相邻链节是否都在硬上限内（该位置是否真的粘得上）。</summary>
        private static bool OriginalFitsAt(List<BlueprintArea> areaList, Dictionary<int, double> gridOffset, int root,
            double anchorLatRad, IReadOnlyList<(BlueprintBuilding a, BlueprintBuilding b)> links)
        {
            foreach (var link in links)
            {
                double d = ChordDistance(areaList, gridOffset, root, anchorLatRad, 1.0, 1.0, false, link.a, link.b);
                if (double.IsNaN(d) || d > BeltPathOptimizer.MaximumMeters)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>取"可能是真实带-带相邻"的链节：一格的 x/y 跨度都不超过 1.6 格。</summary>
        internal static List<(BlueprintBuilding a, BlueprintBuilding b)> BeltLinks(
            IReadOnlyList<BlueprintBuilding> buildings, Func<BlueprintBuilding, bool> isBelt,
            Dictionary<int, BlueprintArea> areaByIndex = null)
        {
            var links = new List<(BlueprintBuilding a, BlueprintBuilding b)>();
            foreach (var building in buildings)
            {
                if (building == null || !isBelt(building) || building.outputObj == null || !isBelt(building.outputObj))
                {
                    continue;
                }
                var other = building.outputObj;
                double dx = other.localOffset_x - building.localOffset_x;
                if (Math.Abs(dx) <= 1.6 && Math.Abs(other.localOffset_y - building.localOffset_y) <= 1.6)
                {
                    links.Add((building, other));
                    continue;
                }
                // x 接缝对：同一区域、y 仍相邻、x 跨度接近"该区域坐标绕一圈"的周期（±1.6 格邻域）。
                // 游戏的粘贴判据是两个 3D 位置的 sqrMagnitude（经度是角度，自动折叠），所以 x=599.45
                // 与 x=-0.36 在"一圈正好 600 格"的纬度带里就是真相邻。老的 |Δx|≤1.6 过滤把这类对全部
                // 丢掉，库模式挑默认纬度时就看不到"整张蓝图在哪些纬度其实粘不上"——6k太阳帆 的接缝
                // 节点在 38° 锚点下落在 800 格/圈的第 2 带，600 格的坐标周期不再等于一整圈，接缝对
                // 真实相距 90°（214 m），游戏对这些带同样报「距离太远」。把接缝对放进证据集后，
                // 合法性检查量的是折叠后的真实弦长：合法纬度上它们折回 0.2~1.2 m，非法纬度上保持
                // 巨大，默认纬度因此只会落在整张蓝图都粘得上的窗口里。距离不在这里算，
                // 由 FramesSuggestLegal / OriginalFitsAt / Resolve 的证据复核逐锚点判定。
                if (areaByIndex != null && building.areaIndex == other.areaIndex
                    && Math.Abs(other.localOffset_y - building.localOffset_y) <= 1.6
                    && areaByIndex.TryGetValue(building.areaIndex, out var area) && area != null)
                {
                    double period = LongitudeSpanGrids(area);
                    if (Math.Abs(Math.Abs(dx) - period) <= 1.6)
                    {
                        links.Add((building, other));
                    }
                }
            }
            return links;
        }

        /// <summary>
        /// 位置精确距离：与 RefreshBuildPreview 的 lpos 完全同式，再用游戏自己的
        /// sqrMagnitude &gt; 5.3 判据比较。
        /// </summary>
        internal static double DistanceWith(Result model, BlueprintBuilding a, BlueprintBuilding b)
        {
            if (model == null || a == null || b == null)
            {
                return DistanceAt(a, b, NominalMetresPerUnit);
            }
            return CoreDistance(model, a, b);
        }

        /// <summary>位置精确距离的核心：与 RefreshBuildPreview 的 lpos 完全同式。</summary>
        private static double CoreDistance(Result model, BlueprintBuilding a, BlueprintBuilding b)
        {
            bool swap = model.SwapAxes;
            double signLat = model.LatitudeSign;
            double signLon = model.LongitudeSign;

            double latA = NodeLatitude(model, a, swap, signLat);
            double latB = NodeLatitude(model, b, swap, signLat);
            // 经度差 = 沿 x 从 x_a 走到 x_b 的积分；**不做任何取模**。
            //
            // 为什么不各自积分再相减、也不要 IEEERemainder 折返：整球区域的 x 是整整一圈，
            // "从参考经度走到某节点"的经度很容易越过 ±π，两侧各自取模之后中间那道缝会落到两个
            // 节点之间——本该 1 m 的相邻节点被算成 225 m（实测 6k）。直接在两点之间积分就精确得多：
            // 起止都在同一片区域时参考经度相减消掉，积分本身是 x 的连续严格单调函数，缝只在真正
            // 绕完一整圈（|Δ经度| ≥ 2π）时出现，而那种链节本来就不可能是相邻带。
            // 区域之间那一段参考经度差只取模一次（多区域蓝图才用得上）。
            double deltaLon = LongitudeBetween(model, a, b, swap, signLat, signLon, latA, latB);
            double lonA2 = 0.0;
            double lonB2 = deltaLon;

            double radiusA = BaseRadius + AltitudeMetresPerUnit * Math.Max(0f, a.localOffset_z);
            double radiusB = BaseRadius + AltitudeMetresPerUnit * Math.Max(0f, b.localOffset_z);
            double ca = Math.Cos(latA), sa = Math.Sin(latA);
            double cb = Math.Cos(latB), sb = Math.Sin(latB);
            double xa = ca * Math.Sin(lonA2), ya = sa, za = -ca * Math.Cos(lonA2);
            double xb = cb * Math.Sin(lonB2), yb = sb, zb = -cb * Math.Cos(lonB2);
            double dx = xa * radiusA - xb * radiusB;
            double dy = ya * radiusA - yb * radiusB;
            double dz = za * radiusA - zb * radiusB;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double NodeLatitude(Result model, BlueprintBuilding building, bool swap, double signLat)
        {
            double latAxis = swap ? building.localOffset_x : building.localOffset_y;
            return ClampLat(model.RefLatOf(building.areaIndex) + signLat * latAxis * LatPerGrid);
        }

        /// <summary>沿 x 积分时的采样步长（格）。0.25 格 ≈ 0.09°，比游戏自己的纬度分带细 4 倍以上。</summary>
        private const double LongitudeSampleGrids = 0.25;

        /// <summary>
        /// 两节点之间的经度差（弧度）。
        ///
        /// 游戏侧是 <c>lon = refLon + lonSign * x * GetLongitudeRadPerGrid(节点纬度)</c>，而
        /// <c>GetLongitudeRadPerGrid</c> 又按**节点纬度查分段表**：离极点越近一圈格数越多、每格越窄。
        /// 所以经度必须**沿 x 积分**，不能用"x × 某一处的格宽"。
        ///
        /// 上一版用"x × 端点格宽"：带边界上会跳一下——实测 6k 里 x≈599 上的相邻两点（x 差 0.001）
        /// 因为两端落在 400 格/圈与 300 格/圈两个带里，被算成相隔 175°，距离虚报成 391 m。
        /// 那条假链节又触发"越限邻域固定"与合并拒绝，是"绝大部分传送带无法长带化"的直接来源之一。
        /// 这里在两点之间以 0.25 格步长累加：单格误差 &lt; 1 mm 量级，且带边界不再产生跳变。
        /// </summary>
        private static double LongitudeBetween(Result model, BlueprintBuilding a, BlueprintBuilding b,
            bool swap, double signLat, double signLon, double latitudeA, double latitudeB)
        {
            double xa = swap ? a.localOffset_y : a.localOffset_x;
            double xb = swap ? b.localOffset_y : b.localOffset_x;
            double delta = IntegralLongitude(model, a.areaIndex, xa, xb, swap, signLat, latitudeA, latitudeB);
            if (a.areaIndex != b.areaIndex)
            {
                delta += Math.IEEERemainder(model.RefLonOf(b.areaIndex) - model.RefLonOf(a.areaIndex), 2.0 * Math.PI);
            }
            return delta * signLon;
        }

        /// <summary>沿 x 从 xFrom 到 xTo 累加每格经度（同一区域内）。</summary>
        private static double IntegralLongitude(Result model, int areaIndex, double xFrom, double xTo,
            bool swap, double signLat, double latitudeA, double latitudeB)
        {
            double refLat = model.RefLatOf(areaIndex);
            double span = xTo - xFrom;
            if (Math.Abs(span) < 1e-9)
            {
                return 0.0;
            }
            if (!swap)
            {
                // 非换轴时纬度只由 y 决定，两端 y 相同 → 纬度沿 x 是常数，直接用该常数格宽。
                return span * LonRadPerGridFast(latitudeA);
            }
            // 换轴时局地 x 轴就是纬度轴，格宽沿 x 变化，必须逐步积分。
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) / LongitudeSampleGrids));
            double step = span / steps;
            double sum = 0.0;
            for (int i = 0; i < steps; i++)
            {
                double sample = xFrom + step * (i + 0.5);
                double latitude = ClampLat(refLat + signLat * sample * LatPerGrid);
                sum += LonRadPerGridFast(latitude);
            }
            _ = latitudeB;
            return sum * step;
        }

        /// <summary>
        /// 不带观测时用的弦长公式（库模式的纬度搜索）。**必须与 <see cref="DistanceWith"/> 同式**，
        /// 否则"这个纬度粘得上吗"的判定会和最终校验不一致（实测 1080 在 84° 因此漏放，
        /// 结果里有 3 条 2.5 m 的新链节）。这里直接构造一个临时 Result 复用同一套核心公式。
        /// </summary>
        private static double ChordDistance(List<BlueprintArea> areaList, Dictionary<int, double> gridOffset, int root,
            double anchorLatRad, double sign, double lonSign, bool swap, BlueprintBuilding a, BlueprintBuilding b)
            => CoreDistance(BuildScratchModel(areaList, gridOffset, anchorLatRad, sign, lonSign, swap), a, b);

        /// <summary>纬度搜索用的临时模型（不带 Observation，符号与换轴由参数给定）。</summary>
        [ThreadStatic]
        private static Result _scratchModel;
        [ThreadStatic]
        private static string _scratchKey;

        private static Result BuildScratchModel(List<BlueprintArea> areaList, Dictionary<int, double> gridOffset,
            double anchorLatRad, double sign, double lonSign, bool swap)
        {
            string key = anchorLatRad.ToString("R") + "|" + sign + "|" + lonSign + "|" + swap;
            if (_scratchModel != null && _scratchKey == key)
            {
                return _scratchModel;
            }
            var model = new Result
            {
                AnchorLatitudeRad = anchorLatRad,
                AnchorFromPaste = false,
                HasObservation = false,
                ExplicitSigns = true,
                ExplicitLatitudeSign = sign,
                ExplicitLongitudeSign = lonSign,
                ExplicitSwapAxes = swap
            };
            var byIndex = areaList.ToDictionary(x => x.index);
            int count = areaList.Count == 0 ? 0 : areaList.Max(x => x.index) + 1;
            for (int i = 0; i < count; i++)
            {
                model.AreaRefLat[i] = ClampLat(anchorLatRad + sign * (gridOffset.TryGetValue(i, out double g) ? g : 0.0) * LatPerGrid);
                model.AreaRefLon[i] = 0.0;
            }
            if (areaList.Count > 1)
            {
                int root = -1;
                foreach (var area in areaList)
                {
                    if (area.parentIndex < 0 || !byIndex.ContainsKey(area.parentIndex))
                    {
                        root = area.index;
                        break;
                    }
                }
                for (int pass = 0; pass < count + 1; pass++)
                {
                    foreach (var area in areaList)
                    {
                        if (area.index == root || !byIndex.TryGetValue(area.parentIndex, out var parent))
                        {
                            continue;
                        }
                        model.AreaRefLon[area.index] = model.AreaRefLon[parent.index]
                            + lonSign * area.tropicAnchor * LonRadPerGridFast(model.AreaRefLat[parent.index])
                            - lonSign * area.anchorLocalOffsetX * LonRadPerGridFast(model.AreaRefLat[area.index]);
                    }
                }
            }
            _scratchModel = model;
            _scratchKey = key;
            return model;
        }

        /// <summary>兼容旧签名：不带位置时按库模式建模。</summary>
        internal static Result Resolve(IEnumerable<BlueprintArea> areas, IEnumerable<BlueprintBuilding> buildings,
            Func<BlueprintBuilding, bool> isBelt)
            => Resolve(areas, buildings, isBelt, null);

        /// <summary>模型下的两点距离（供旧调用点使用）。</summary>
        internal static double Distance(BlueprintBuilding a, BlueprintBuilding b, Result model)
            => model == null ? DistanceAt(a, b, NominalMetresPerUnit) : DistanceWith(model, a, b);

        /// <summary>固定格宽的纯几何距离，只用于与旧模型对照和夹具备查。</summary>
        internal static double DistanceAt(BlueprintBuilding a, BlueprintBuilding b, double metresPerUnit)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }
            double radius = BaseRadius + AltitudeMetresPerUnit * Math.Max(0.0, Math.Max(a.localOffset_z, b.localOffset_z));
            double dx = (b.localOffset_x - a.localOffset_x) * metresPerUnit * (radius / BaseRadius);
            double dy = (b.localOffset_y - a.localOffset_y) * metresPerUnit * (radius / BaseRadius);
            double dz = (b.localOffset_z - a.localOffset_z) * AltitudeMetresPerUnit;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>一整个区域满圈的 x 跨度，用于识别整球图。</summary>
        internal static double LongitudeSpanGrids(BlueprintArea area)
            => area == null ? 0.0 : 5.0 * Math.Max(1, area.areaSegments);
    }
}




