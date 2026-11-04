using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace BlueprintBeltSaver;

// LongerBelts 1.5.3 spacing constants and floor(length / spacing) + 1 rule.
// Existing blueprints retain their path instead of constructing new endpoints.
internal static class BeltPathOptimizer
{
    internal const double MinimumMeters = 0.400001;
    internal const double MaximumMeters = 2.301172;

    /// <summary>Which field of a non-belt building points at a belt node.</summary>
    internal enum AttachmentSide
    {
        Input,
        Output
    }

    /// <summary>A non-belt building (sorter, machine, ...) that references a belt node.</summary>
    internal sealed class Attachment
    {
        internal BlueprintBuilding Owner;
        internal BlueprintBuilding Target;
        internal AttachmentSide Side;
    }

    internal sealed class Report
    {
        internal int Runs, ReducedRuns, TooShort, Protected, Invalid, Rebound, AttachmentYawSkipped, OffsetClamped, OverLimitPinned, OverLimitLinks;
        internal int AnchorConflicts, AnchorAttachments, AnchorSprayers, AnchorObstacles, AnchorStructural, AnchorStations, AnchorCycles, AnchorPin;
        internal int CollisionBoxHits, KeptOverSpans, GridFallbacks;
        internal readonly List<string> Chains = new List<string> { "start,end,nodes_before,nodes_after,length_m,cap_m,status" };
        public override string ToString() => $"路径 {Runs}，已优化 {ReducedRuns}，无需减少 {TooShort}，固定点 {Protected}，异常连接 {Invalid}，重绑附属连接 {Rebound}，因朝向不一致跳过 {AttachmentYawSkipped}，修正越界偏移 {OffsetClamped}" +
            (OverLimitLinks > 0 ? $"，超限存储连接 {OverLimitLinks} 条（固定其邻域 {OverLimitPinned} 点）" : "") +
            (BeltPathOptimizer.Verbose ? $"；[锚点构成] 结构{AnchorConflicts} 绑定{AnchorAttachments} 喷涂{AnchorSprayers} 障碍{AnchorObstacles} 转折{AnchorStructural} 站台{AnchorStations} 环{AnchorCycles} 超限{AnchorPin}；碰撞盒命中 {CollisionBoxHits}；格点回退 {GridFallbacks} 链；原件对越限豁免 {KeptOverSpans} 段" : "");
    }
    internal static BlueprintBuilding[] Optimize(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
        Func<BlueprintBuilding, float> limit) => Optimize(buildings, isBelt, limit,
            (a,b) => GridDistance(a,b) * 1.2566370614359173, out _);

    /// <summary>诊断开关（离线校验台用，游戏内恒为 null）：只跟踪该 index 的建筑。</summary>
    internal static int? TraceIndex = null;
    /// <summary>诊断开关：只跟踪该坐标（±0.05 格）的建筑。</summary>
    internal static float? TraceX = null;
    internal static float? TraceY = null;
    private static bool IsTraced(BlueprintBuilding b)
        => b != null && ((TraceIndex.HasValue && b.index == TraceIndex.Value)
            || (TraceX.HasValue && TraceY.HasValue
                && Math.Abs(b.localOffset_x - TraceX.Value) < 0.05f
                && Math.Abs(b.localOffset_y - TraceY.Value) < 0.05f));
    /// <summary>诊断开关（离线校验台用，游戏内恒为 false）：打印逐链拒绝明细。</summary>
    internal static bool Verbose = false;
    /// <summary>"这一段可以拉直"的余弦阈值（默认 1°）。离线实验可调大，评估曲率锚点的代价。</summary>
    internal static double SmoothCosThreshold = Math.Cos(Math.PI / 180.0);
    /// <summary>喷涂机周边钉锚窗口（格，默认 1.6）。离线实验可调，评估喷涂锚点的代价；游戏内恒为默认值。</summary>
    internal static double SprayerWindowGrids = 1.6;
    private static void VerboseLine(string message)
    {
        if (Verbose)
        {
            Console.WriteLine(message);
        }
    }
    private static void Trace(string message)
    {
        if (TraceIndex.HasValue || TraceX.HasValue)
        {
            Console.WriteLine("    [trace] " + message);
        }
    }

    internal static BlueprintBuilding[] Optimize(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
        Func<BlueprintBuilding, float> limit, Func<BlueprintBuilding, BlueprintBuilding, double> meters, out Report report,
        bool rebindAttachments = false, bool gridExact = true, bool pinOverLimitLinks = false, bool projectionRebind = false,
        List<int> extraAnchorPositions = null)
    {
        report = new Report();
        var belts = new HashSet<BlueprintBuilding>(buildings.Where(isBelt));
        var next = belts.ToDictionary(b => b, b => b.outputObj);
        var conflicts = new HashSet<BlueprintBuilding>();
        // A belt input may encode an edge not present in the predecessor output.
        // 004.13：数组序遍历（先到先得的 next 赢家必须可复现，见下方 incoming 处注释）。
        foreach (var b in buildings)
            if (belts.Contains(b) && b.inputObj != null && belts.Contains(b.inputObj))
            {
                var prev = b.inputObj;
                if (next[prev] == null) next[prev] = b;
                else if (next[prev] != b) { conflicts.Add(prev); conflicts.Add(b); }
            }
        var incoming = belts.ToDictionary(b => b, b => new List<BlueprintBuilding>());
        // 004.13：以下按 buildings 数组序遍历（而非 belts HashSet）。引用哈希随进程变化，
        // HashSet 迭代序在两次运行间会不同：冲突检测的"先到先得"赢家、环锚点的起点都会
        // 因此漂移（1080传送带 同配置三次运行 -15/+8/-45 的实测根源），结果不可复现。
        foreach (var b in buildings)
            if (belts.Contains(b) && next[b] != null && belts.Contains(next[b])) incoming[next[b]].Add(b);
        var anchors = new HashSet<BlueprintBuilding>(conflicts);
        report.AnchorConflicts = anchors.Count;
        // 004.15 门禁反馈锚点：上一轮拓扑门禁报跨线/引用丢失的附件目标窗口（目标+原始前后
        // 邻居）由调用方按 source 数组位置传入——Clone 之间对象身份不通，必须按位置传递。
        // 锚点即链边界（runs 在锚点之间构建），窗口内原折线形状原样保留，目标跨度与基线
        // 逐字相同 → binder 兼容、引用永不跨线（1080 分流器#1971 实证闭环）。
        if (extraAnchorPositions != null)
        {
            int extraHit = 0;
            foreach (int p in extraAnchorPositions)
            {
                if (p >= 0 && p < buildings.Length && belts.Contains(buildings[p])) { anchors.Add(buildings[p]); extraHit++; }
            }
            if (extraAnchorPositions.Count > 0)
            {
                Console.WriteLine($"    [extraAnchors] 请求 {extraAnchorPositions.Count} 个位置 [{string.Join(",", extraAnchorPositions)}]，命中 {extraHit}，请求前锚点 {report.AnchorConflicts}");
            }
        }
        // Non-belt buildings (sorters, machines) referencing a belt normally pin that node. With
        // rebindAttachments the reference is re-pointed to the merged segment instead, so the node
        // may be deleted - provided the guards below hold.
        var attachments = rebindAttachments ? new List<Attachment>() : null;
        foreach (var b in buildings.Where(b => !belts.Contains(b)))
        {
            if (b.inputObj != null && belts.Contains(b.inputObj))
            {
                if (rebindAttachments) attachments.Add(new Attachment { Owner = b, Target = b.inputObj, Side = AttachmentSide.Input });
                else anchors.Add(b.inputObj);
            }
            if (b.outputObj != null && belts.Contains(b.outputObj))
            {
                if (rebindAttachments) attachments.Add(new Attachment { Owner = b, Target = b.outputObj, Side = AttachmentSide.Output });
                else anchors.Add(b.outputObj);
            }
        }
        // ── 分拣器绑定点的策略（003.2 定稿）─────────────────────────────────────
        // freeMerge（两者都开）：合并完全忽略分拣器与机器的自由重布线（拐点到拐点按上限
        //   贪心切），合并后**全部**绑定点按"本体位置 → 就近替换节点 + 偏移归零"重绑
        //   （蓝图偏移恒为 0，取货点由游戏投影决定），并重排建造顺序（带节点在前）。
        // 实测：分拣器零失效；机器（喷涂机）输入失效 1 例（C1绿糖，重指跨带所致），
        //   后续"机器绑定点原位保留"试验反而更多失效 → 已回滚，喷涂机问题留待
        //   "机器周边保护气泡"方案（下一轮）。
        // 仅 rebindAttachments（002）：绑定点原位锚定，引用与坐标不变。
        // 都关：旧"节点+前驱+后继"冻结（AnchorPiece）。
        bool freeMerge = projectionRebind && rebindAttachments;
        if (freeMerge)
        {
            int anchorMark = anchors.Count;
            // 003.4 语义（回滚 003.5，玩家实测 003.5 更差：喷涂机原料带明显超长）：
            // 机器类（非分拣器，喷涂机等）绑定点原位锚定；分拣器走就近重绑（003.2 起）。
            // （004.15 曾试验「分流器目标一刀切原位锚定」：+347 锚点只救 5 个端点、省带
            //   38→22，代价失衡，已回滚——跨线拦截改由门禁反馈重跑按窗口精确锚定。）
            foreach (var attachment in attachments)
            {
                if (attachment.Target != null && belts.Contains(attachment.Target) && !IsInserter(attachment.Owner))
                    anchors.Add(attachment.Target);
            }
            report.AnchorAttachments = anchors.Count - anchorMark;
        }
        else if (rebindAttachments)
        {
            int mark = anchors.Count;
            foreach (var attachment in attachments)
            {
                if (attachment.Target != null && belts.Contains(attachment.Target)) anchors.Add(attachment.Target);
            }
            report.AnchorAttachments = anchors.Count - mark;
        }
        else
        {
            foreach (var b in buildings.Where(b => !belts.Contains(b)))
            {
                if (b.inputObj != null && belts.Contains(b.inputObj)) AnchorPiece(b.inputObj, next, belts, anchors);
                if (b.outputObj != null && belts.Contains(b.outputObj)) AnchorPiece(b.outputObj, next, belts, anchors);
            }
        }
        // 004.17：附加机窗口与杆类侧翼锚定移出 freeMerge 分支——它们保护的是**喷涂机/监测器/炮塔的
        // 几何接带**与**杆下原状段**，与分拣器重绑策略无关。旧结构只在 freeMerge（两个分拣器开关都开，
        // 非出厂默认）时生效：出厂默认配置下喷涂机周边带节点照删不误（离线实测 37.5小火箭/黑棒 货物口
        // 脱带、1440低效 增产剂口脱带，准则③）。现在三种分拣器策略一律先钉窗口。
        {
            int anchorMark = anchors.Count;
            // 喷涂机连接是纯几何重建（反编译铁证：PlanetFactory.cs:6046-6048——粘贴后在与喷涂点
            // sqrMagnitude<1f（1 米窗口）且距喷涂线<0.3f 的"带节点实体"上建立连接，蓝图不存引用）。
            // 喷涂机有 cargo/inc 两个连接位（addonAreaColPoses[0]/[1]，SpraycoaterComponent.cs:171/185），
            // 增产剂口偏离喷涂机中心——v3.6.5 的 1 格半径保住了货物带、丢了增产剂带（玩家实测）。
            // 现按玩家指示放宽到 **1.6 格 ≈ 2.0 m**（口子偏移 + 1 m 窗口 + 余量）：窗口内全部带节点
            // 钉为锚点（= 原料带与增产剂带中喷涂机判定节点均不做长带化，作为固定点）。GridDistance
            // ×1.2566 是赤道尺度，高纬度实际米数更大 → 只会多钉不少钉，纬度适应性偏保守安全。
            // 004.17：同一规则扩展到全部"带上附加机"（addonType=Belt：流速监测器 2030、带供弹炮塔
            // 3001/3003/3004/3005/3006/3010）——它们与喷涂机同一套连接逻辑（PlanetFactory.cs:6023-6072，
            // 连接位在建筑原点），此前完全没有保护，合并删掉其下带节点会让监测器/炮塔静默脱带（准则④）。
            foreach (var sprayer in buildings.Where(b => IsBeltAddon(b.itemId)))
            {
                foreach (var t in buildings)
                {
                    if (!belts.Contains(t)) continue;
                    double dx = t.localOffset_x - sprayer.localOffset_x;
                    double dy = t.localOffset_y - sprayer.localOffset_y;
                    double dz = t.localOffset_z - sprayer.localOffset_z;
                    if (dx * dx + dy * dy + dz * dz <= SprayerWindowGrids * SprayerWindowGrids) anchors.Add(t);
                }
            }
            report.AnchorSprayers = anchors.Count - anchorMark;
            anchorMark = anchors.Count;
            // 准则⑤前置（玩家《带上分拣器优化版》思路，2026-10-18）：把"压在带线上的**杆类**
            // 障碍"（电线杆 2201 / 无线输电塔 2202 / 卫星配电站 2203，最近带节点 ≤1.0 格）最近的
            // **两个**带节点钉为锚点——合并自动在障碍两侧断开，杆下原状段整体保留，两侧子链
            // 独立最优合并（同结构 玩家版 8→6 省2 vs 003.8 8→8 省0）。
            // 004.10 修正：原实现对**所有**非带非分拣建筑都锚（每台机器附近 2 个节点），
            // C1 一张图就白钉 199 个锚点、6k 钉 497 个，把链路切得过碎。机器类碰撞由后面的
            // "节点球回插"守卫兜底（新节点比原图任何节点更贴近障碍才回插），不需要侧翼锚定。
            foreach (var obs in buildings)
            {
                if (belts.Contains(obs) || IsInserter(obs) || !IsPoleLike(obs)) continue;
                BlueprintBuilding n1 = null, n2 = null;
                double d1 = 1.0 * 1.0, d2 = double.MaxValue;
                foreach (var t in buildings)
                {
                    if (!belts.Contains(t)) continue;
                    double dx = t.localOffset_x - obs.localOffset_x;
                    double dy = t.localOffset_y - obs.localOffset_y;
                    double dz = t.localOffset_z - obs.localOffset_z;
                    double dd = dx * dx + dy * dy + dz * dz;                    if (dd < d1) { n2 = n1; d2 = d1; n1 = t; d1 = dd; }
                    else if (dd < d2) { n2 = t; d2 = dd; }
                }
                if (n1 != null) anchors.Add(n1);
                if (n2 != null) anchors.Add(n2);
            }
            report.AnchorObstacles = anchors.Count - anchorMark;
        }

        // Iron rule guard (A), restricted to blueprints that span more than one latitude band.
        //
        // A stored link that is longer than the hard limit cannot survive the paste: the game rejects
        // the whole paste ("距离太远"), or, when the link is missing/refused, rebuilds the chain from
        // whatever belt ends happen to be nearby (PlanetFactory proximity matching). Deleting the nodes
        // around such a link would take away the only anchors that rebuild can use, so the link and its
        // immediate neighbours are pinned. With a correct length model this fires zero times on the
        // blueprints measured here - it is a defence in depth, not the fix for the equator problem.
        int pinMark = anchors.Count;
        if (pinOverLimitLinks)
        {
            double overLimit = MaximumMeters * 1.05;
            foreach (var b in belts)
            {
                var n = next[b];
                if (n == null || !belts.Contains(n) || ReferenceEquals(n, b))
                {
                    continue;
                }
                double distance = meters(b, n);
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= overLimit)
                {
                    continue;
                }
                report.OverLimitLinks++;
                int pinned = 0;
                if (anchors.Add(b)) pinned++;
                if (anchors.Add(n)) pinned++;
                if (incoming[b].Count == 1 && anchors.Add(incoming[b][0])) pinned++;
                foreach (var previous in incoming[n])
                {
                    if (anchors.Add(previous)) pinned++;
                }
                var after = next[n];
                if (after != null && belts.Contains(after) && anchors.Add(after)) pinned++;
                report.OverLimitPinned += pinned;
            }
        }
        report.AnchorPin = anchors.Count - pinMark;
        int structuralMark = anchors.Count;
        foreach (var b in belts)
        {
            var n = next[b];
            if (incoming[b].Count != 1 || n == null || !belts.Contains(n)) { anchors.Add(b); continue; }
            var prev = incoming[b][0];
            if (b.inputObj != null && b.inputObj != prev) anchors.Add(b);
            if (!Compatible(prev,b) || !Compatible(b,n) || !Smooth(prev,b,n)
                || b.filterId != 0 || b.recipeId != 0 || !string.IsNullOrEmpty(b.content)
                || (b.parameters != null && b.parameters.Length != 0)) anchors.Add(b);
            if (!Compatible(prev,b)) anchors.Add(prev);
            if (!Compatible(b,n)) anchors.Add(n);
        }
        report.AnchorStructural = anchors.Count - structuralMark;
        int stationMark = anchors.Count;
        var stationBelts = belts.Where(b =>
            (b.inputObj != null && b.inputObj.itemId >= 2103 && b.inputObj.itemId <= 2105)
            || (b.outputObj != null && b.outputObj.itemId >= 2103 && b.outputObj.itemId <= 2105)).ToList();
        foreach (var station in buildings.Where(b => b.itemId >= 2103 && b.itemId <= 2105))
        {
            if (station.inputObj != null && belts.Contains(station.inputObj)) stationBelts.Add(station.inputObj);
            if (station.outputObj != null && belts.Contains(station.outputObj)) stationBelts.Add(station.outputObj);
        }
        foreach (var node in stationBelts)
        {
            anchors.Add(node);
            if (next[node] != null && belts.Contains(next[node])) anchors.Add(next[node]);
            foreach (var previous in incoming[node]) anchors.Add(previous);
        }
        report.AnchorStations = anchors.Count - stationMark;
        int cycleMark = anchors.Count;
        // Smooth cycles need at least two fixed points to retain their extent.
        var visited = new HashSet<BlueprintBuilding>();
        foreach (var b in buildings)
        {
            if (!belts.Contains(b) || visited.Contains(b)) continue;
            var walk = new List<BlueprintBuilding>();
            var local = new Dictionary<BlueprintBuilding,int>();
            var current = b;
            while (current != null && belts.Contains(current) && !visited.Contains(current) && !local.ContainsKey(current))
            { local[current] = walk.Count; walk.Add(current); current = next[current]; }
            int first;
            if (current != null && local.TryGetValue(current,out first))
            {
                var cycle = walk.Skip(first).ToList();
                if (cycle.Count > 2 && cycle.Count(anchors.Contains) < 2)
                { anchors.Add(cycle[0]); anchors.Add(cycle[cycle.Count/2]); }
            }
            foreach (var node in walk) visited.Add(node);
        }
        report.AnchorCycles = anchors.Count - cycleMark;
        report.Protected = anchors.Count; report.Invalid = conflicts.Count;
        var rep = report; // 004.12：局部函数不能捕获 out 参数，用本地别名转发（引用同一对象）
        var retained = new HashSet<BlueprintBuilding>(buildings);
        var added = new List<BlueprintBuilding>();
        var pending = new Dictionary<BlueprintBuilding,BlueprintBuilding>();
        var inputPending = new Dictionary<BlueprintBuilding,BlueprintBuilding>();
        // 落点投影重绑清单：跨路径收集，路径循环结束后统一应用（见循环尾部的 rebinds.Add）。
        var rebinds = new List<(BlueprintBuilding owner, AttachmentSide side, BlueprintBuilding node, int offset)>();
        // 004.11：所有非带非分拣建筑的 Build 碰撞盒预计算（每建筑每盒一次，run 循环内只做盒测试）。
        // 旧实现的"0.8 格中心距离"对 3×3 制造台这类大建筑严重偏小：原线段中点可能落进机器
        // 碰撞盒、粘贴时该节点被游戏跳过 → 断带。现在用 F9 导出的真实 Build 碰撞盒判定。
        var collisionObstacles = buildings.Where(b => !belts.Contains(b) && !IsInserter(b)).ToList();
        var obstacleBoxMap = new Dictionary<BlueprintBuilding, List<BuildingCollision.WorldBox>>();
        foreach (var o in collisionObstacles)
        {
            var list = new List<BuildingCollision.WorldBox>();
            if (!BuildingCollision.TryGetWorldBoxes(o, list))
            {
                continue;
            }
            // 自适应豁免：若**原件**已有带节点落进这个建筑的碰撞盒（喷涂机/物流站/分流器等
            // 与带天然重叠的装置），说明游戏允许这种摆放，盒测试会误杀；该建筑退回旧口径。
            // 制造台/熔炉等"原件从不重叠"的建筑才启用真实盒判定（玩家 C1 断带正是制造台）。
            bool originalViolates = false;
            foreach (var n in belts)
            {
                if (BuildingCollision.Hits(n, list)) { originalViolates = true; break; }
            }
            if (!originalViolates)
            {
                obstacleBoxMap[o] = list;
            }
        }
        foreach (var start in buildings.Where(anchors.Contains))
        {
            var path = new List<BlueprintBuilding>{start};
            var seen = new HashSet<BlueprintBuilding>{start};
            var current = next[start];
            while (current != null && belts.Contains(current))
            {
                path.Add(current);
                if (anchors.Contains(current)) break;
                if (!seen.Add(current)) break;
                current = next[current];
            }
            if (path.Count < 2 || !anchors.Contains(path[path.Count-1])) continue;
            var end = path[path.Count-1];
            if (end == start || path.Any(b => !Compatible(start,b) || conflicts.Contains(b))) { report.Chains.Add(Row(start,end,path.Count,path.Count,0,0,"boundary_or_conflict")); continue; }
            report.Runs++;
            var cumulative = new double[path.Count];
            for (int i=1;i<path.Count;i++) cumulative[i]=cumulative[i-1]+meters(path[i-1],path[i]);
            double length = cumulative[cumulative.Length-1];
            double cap = Math.Min(MaximumMeters, path.Min(b => (double)limit(b)));
            if (double.IsNaN(cap) || double.IsInfinity(cap) || cap < MinimumMeters || double.IsNaN(length) || length < MinimumMeters)
            { report.Invalid++; continue; }
            // 004.12：本链的路径索引与实际模式标志。chainGrid 在插值链碰撞失败回退格点后置 true，
            // 后续（重绑重算/守卫/跨度校验/提交端的 retained 收尾）都按格点语义处理。
            var pathIndexMap = new Dictionary<BlueprintBuilding, int>();
            for (int i = 0; i < path.Count; i++) pathIndexMap[path[i]] = i;
            bool chainGrid = gridExact;
            var replacements = new List<BlueprintBuilding>{start};
            int rebindBase = rebinds.Count;
            int addedBase = added.Count;
            var removedThisRun = new List<BlueprintBuilding>();
            List<int> kept = null;
            int spans = 0;
            bool createdNodes = false;
            var nearKeep = new HashSet<int>(); // 004.3 就近顶替保留集（插值点 <0.5 格贴原始节点时保原节点）
            // 004.12：定段固定的原件段端点集合（碰撞/阈值命中的弧位所在段两端）+ 本链插值节点集。
            var fixedIdx = new HashSet<int>();
            var newNodes = new HashSet<BlueprintBuilding>();
            if (gridExact)
            {
                // Keep a SUBSET of the original nodes instead of inventing interpolated positions.
                // Blueprint offsets are grid-relative (BlueprintUtils writes rad/grid), and the game
                // rebuilds belt endpoints from the grid when the blueprint is pasted, so a node that
                // was invented between grid points can end up further from its neighbour than the
                // maximum belt length ("距离太远" in the paste preview). Reusing original nodes keeps
                // every surviving span exactly as measured here, at any latitude and altitude.
                kept = new List<int>();
                int cursor = 0;
                while (cursor < path.Count - 1)
                {
                    int reach = -1;
                    for (int j = cursor + 1; j < path.Count; j++)
                    {
                        if (meters(path[cursor], path[j]) > cap) break;
                        reach = j;
                    }
                    if (reach < 0)
                    {
                        // A single original step already exceeds the cap: that link predates this
                        // optimization and cannot be split here, so the whole run is left untouched.
                        kept = null;
                        break;
                    }
                    // Avoid leaving a tail shorter than the minimum belt length.
                    if (reach < path.Count - 1 && reach > cursor + 1 && (length - cumulative[reach]) < MinimumMeters)
                    {
                        reach--;
                    }
                    kept.Add(reach);
                    cursor = reach;
                }
                if (kept == null)
                {
                    report.Invalid++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"pre_existing_over_cap"));
                    continue;
                }
                // No saving unless at least one interior node can be dropped.
                if (kept.Count >= path.Count-2)
                {
                    report.TooShort++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"no_reduction"));
                    continue;
                }
                foreach (int index in kept)
                {
                    replacements.Add(path[index]);
                }
            }
            else
            {
                // 等分插值（004.12 重构为「定段固定 + 分区等分」，替代「整体等分 + 事后回填对」）：
                // 旧实现先整体等分、再把撞障碍的插值点用原始段对回填——回填在等分点之间切出
                // 0.1~0.3 m 亚最小碎片段（1080 实测 64 条链因此整链 distance_rejected），回填对
                // 共享端点时直接 collision_unresolvable 整链放弃（65.6 实测 139 条链零收益）。
                // 新实现两阶段：
                //  ① 定段：对每个候选插值弧位预判障碍冲突（真实碰撞盒 / 无盒 0.8 格回退 /
                //     节点球阈值 R_o-0.05——守卫 v3 同款判据提前到构造期），命中的弧位所在
                //     原件段 (lo,hi) 两端固定；
                //  ② 分区等分：固定点把链切成若干区域，每区域独立等分插值——区域跨度数 =
                //     ceil(Lf/cap)，每段 ∈ (cap/2, cap]，跨度校验构造性通过；固定段本身是
                //     原件相邻对（KeptOver 口径豁免）。碰撞失败与碎片跨度从结构上消失。
                // tier 继承（004.1）：插值节点继承本 run 被删内部节点的多数 tier。
                int majItem = -1; int majCnt = -1;
                {
                    var tierCount = new Dictionary<int,int>();
                    for (int k = 1; k < path.Count - 1; k++)
                    {
                        int it = path[k].itemId;
                        tierCount.TryGetValue(it, out int c);
                        tierCount[it] = c + 1;
                    }
                    foreach (var kv in tierCount) if (kv.Value > majCnt) { majCnt = kv.Value; majItem = kv.Key; }
                }
                BlueprintBuilding MakeNode(int loIdx, int hiIdx, double t, int srcJ)
                {
                    // 元数据（yaw/tilt/pitch/model）取自内部节点，绝不取 run 端点（端点常是对角带）。
                    int sourceIndex = path.Count >= 3 ? Math.Min(Math.Max(srcJ, 1), path.Count - 2) : srcJ - 1;
                    var node = Copy(path[sourceIndex]);
                    if (majItem > 0) node.itemId = (short)majItem;
                    node.localOffset_x=Round(path[loIdx].localOffset_x+(path[hiIdx].localOffset_x-path[loIdx].localOffset_x)*t);
                    node.localOffset_y=Round(path[loIdx].localOffset_y+(path[hiIdx].localOffset_y-path[loIdx].localOffset_y)*t);
                    node.localOffset_z=Round(path[loIdx].localOffset_z+(path[hiIdx].localOffset_z-path[loIdx].localOffset_z)*t);
                    node.localOffset_x2=node.localOffset_x; node.localOffset_y2=node.localOffset_y; node.localOffset_z2=node.localOffset_z;
                    return node;
                }
                // 障碍判定数据：真实盒（有碰撞盒的非分拣建筑）/ 0.8 格回退（无盒者）/
                // 节点球阈值（全部非带建筑：插值点不得比任何原始节点更贴近障碍，守卫 v3 口径）。
                var boxObstacles = new List<(BlueprintBuilding o, List<BuildingCollision.WorldBox> boxes)>();
                var fallbackObstacles = new List<BlueprintBuilding>();
                foreach (var o in collisionObstacles)
                {
                    if (obstacleBoxMap.TryGetValue(o, out var oBoxes)) boxObstacles.Add((o, oBoxes));
                    else fallbackObstacles.Add(o);
                }
                var thresholdList = new List<(BlueprintBuilding o, double th)>();
                foreach (var bld in buildings)
                {
                    if (belts.Contains(bld)) continue;
                    double m = double.MaxValue;
                    for (int s = 0; s < path.Count; s++)
                    {
                        double ex = path[s].localOffset_x - bld.localOffset_x;
                        double ey = path[s].localOffset_y - bld.localOffset_y;
                        double ez = path[s].localOffset_z - bld.localOffset_z;
                        double d = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                        if (d < m) m = d;
                    }
                    thresholdList.Add((bld, m - 0.05));
                }
                bool Violates(BlueprintBuilding node)
                {
                    foreach (var tv in thresholdList)
                    {
                        double dx = node.localOffset_x - tv.o.localOffset_x;
                        double dy = node.localOffset_y - tv.o.localOffset_y;
                        double dz = node.localOffset_z - tv.o.localOffset_z;
                        if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < tv.th) return true;
                    }
                    foreach (var bo in boxObstacles)
                    {
                        if (BuildingCollision.Hits(node, bo.boxes)) { rep.CollisionBoxHits++; return true; }
                    }
                    foreach (var o in fallbackObstacles)
                    {
                        double dx = o.localOffset_x - node.localOffset_x;
                        double dy = o.localOffset_y - node.localOffset_y;
                        double dz = o.localOffset_z - node.localOffset_z;
                        if (dx * dx + dy * dy + dz * dz <= 0.8 * 0.8) return true;
                    }
                    return false;
                }
                // 004.13：超上限的原件跨度不可切割。玩家手拉的最长带常为 1.9~2.05 m，优化上限
                // 调低（如旧配置 1.903912 → 有效 1.866）或高空带半径放大时，这些原件跨度会
                // 超过 cap。等分插值会给含它们的区域补插值点"把带数补多"（1080传送带 实测
                // 1640→1674 反增），004.8 采纳规则随即整体拒绝插值结果，最终交付零合并——
                // 这正是"很多（高空）带未长带化"的直接来源。改为把超上限原件跨度的两端预先
                // 固定：该跨度按"原件同跨度"口径原样保留（游戏对原件同样接受或拒绝），两侧
                // 子区域继续正常等分合并，插值结果不再净增带。
                for (int s = 1; s < path.Count; s++)
                {
                    if (cumulative[s] - cumulative[s - 1] > cap)
                    {
                        fixedIdx.Add(s - 1);
                        fixedIdx.Add(s);
                    }
                }
                for (int pass = 0; ; pass++)
                {
                    var anchors2 = new List<int>{0};
                    foreach (int fi in fixedIdx) if (fi > 0 && fi < path.Count - 1) anchors2.Add(fi);
                    anchors2.Sort();
                    anchors2.Add(path.Count - 1);
                    bool changed = false;
                    for (int ai = 0; ai + 1 < anchors2.Count; ai++)
                    {
                        int a = anchors2[ai], b = anchors2[ai + 1];
                        double Lf = cumulative[b] - cumulative[a];
                        if (Lf <= cap) continue; // 区域单跨度可达，无插值点
                        // 004.13：插值点数不超过被替换的内部原件数（含超上限原件跨度时
                        // ceil(Lf/cap)-1 可能越过该界 → 补切增带；上方预固定后正常不会触发）。
                        int k = Math.Min((int)Math.Ceiling(Lf / cap) - 1, b - a - 1);
                        for (int j = 1; j <= k; j++)
                        {
                            double T = cumulative[a] + Lf * j / (k + 1);
                            int lo = a; while (lo < b && cumulative[lo + 1] < T) lo++;
                            int hi = lo + 1;
                            double delta = cumulative[hi] - cumulative[lo];
                            double t = delta < 1e-9 ? 0 : (T - cumulative[lo]) / delta;
                            if (Violates(MakeNode(lo, hi, t, hi)))
                            {
                                // 命中：固定该弧位所在原件段两端（已固定过的段不重复触发）。
                                changed |= fixedIdx.Add(lo);
                                changed |= fixedIdx.Add(hi);
                            }
                        }
                    }
                    if (!changed || pass >= 8)
                    {
                        // 收敛（或迭代上限）：按最终固定点布局构造替换链。
                        if (changed)
                        {
                            anchors2 = new List<int>{0};
                            foreach (int fi in fixedIdx) if (fi > 0 && fi < path.Count - 1) anchors2.Add(fi);
                            anchors2.Sort();
                            anchors2.Add(path.Count - 1);
                        }
                        for (int ai = 0; ai + 1 < anchors2.Count; ai++)
                        {
                            int a = anchors2[ai], b = anchors2[ai + 1];
                            double Lf = cumulative[b] - cumulative[a];
                            if (b > a + 1 && Lf > cap)
                            {
                                int k = Math.Min((int)Math.Ceiling(Lf / cap) - 1, b - a - 1);
                                if (k >= 1)
                                for (int j = 1; j <= k; j++)
                                {
                                    double T = cumulative[a] + Lf * j / (k + 1);
                                    int lo = a; while (lo < b && cumulative[lo + 1] < T) lo++;
                                    int hi = lo + 1;
                                    double delta = cumulative[hi] - cumulative[lo];
                                    double t = delta < 1e-9 ? 0 : (T - cumulative[lo]) / delta;
                                    var node = MakeNode(lo, hi, t, hi);
                                    replacements.Add(node);
                                    newNodes.Add(node);
                                }
                                else
                                {
                                    // 004.13 兜底：k 被压到 0 时不能丢内部原件（跨度会爆），
                                    // 原样保留本区域全部节点（等价于本区域不合并）。
                                    for (int q = a + 1; q < b; q++) replacements.Add(path[q]);
                                }
                            }
                            if (b < path.Count - 1) replacements.Add(path[b]);
                        }
                        replacements.Add(end);
                        spans = replacements.Count - 1;
                        createdNodes = true;
                        break;
                    }
                }
            }
            // 004.12：旧的「整体等分 + 撞障碍回填原始对」已并入上面 else 分支的
            // 「定段固定 + 分区等分」构造期判定（回填曾产生亚最小碎片段与共享端点整链放弃）。
            // 004.12 碰撞失败兜底（不再整链放弃）：回退格点保点模式重建本链替换集——全部沿用
            // 原始节点位置（原图可粘贴 = 位置天然合法，碰撞守卫对原始节点不适用），跨度由与
            // 格点分支完全相同的贪心（含尾段保护）保证 ≤ cap。收益不及插值，但远好于整链放弃
            // （基线矩阵：collision_unresolvable 链合计持有 ~2000 节点，全部零收益）。
            bool TryGridFallback()
            {
                if (chainGrid) return false;
                var fbKept = new List<int>();
                int cursor = 0;
                while (cursor < path.Count - 1)
                {
                    int reach = -1;
                    for (int j = cursor + 1; j < path.Count; j++)
                    {
                        if (meters(path[cursor], path[j]) > cap) break;
                        reach = j;
                    }
                    if (reach < 0) return false; // 原件单步越限：格点同样无解（与 pre_existing_over_cap 同口径）
                    if (reach < path.Count - 1 && reach > cursor + 1 && (length - cumulative[reach]) < MinimumMeters) reach--;
                    fbKept.Add(reach);
                    cursor = reach;
                }
                if (fbKept.Count >= path.Count - 2) return false; // 格点也省不出节点，放弃是诚实的
                replacements.Clear();
                replacements.Add(start);
                foreach (int index in fbKept) replacements.Add(path[index]);
                replacements.Add(end);
                kept = fbKept;
                spans = 0; createdNodes = false;
                nearKeep.Clear(); fixedIdx.Clear(); newNodes.Clear();
                chainGrid = true;
                rep.GridFallbacks++;
                return true;
            }
            // （004.12 已删除旧的「撞障碍回填原始对」循环：其判定与修复已并入 else 分支的
            //  构造期定段；这里不再有 collision_unresolvable 的产生源。）
            // 002→004 落点投影重绑（玩家方案）：合并**完全忽略分拣器**（不再原位锚定、不再
            // 弧长覆盖重指），合并后按"分拣器取货点坐标 → 就近带节点 + 偏移重锚定"重新接上。
            // 取货点弧位 = 旧段中心 - 存储偏移×0.06（符号约定由原图统计得出：397:0 / 128:4，
            // offset 正方向 = 指向段首/上游）。
            // 004.12：重绑计算抽成可重跑的局部函数——碰撞失败回退格点后，需按新替换集重算
            // 重绑与臂长守卫回插，再走守卫与跨度校验；提交到全局 rebinds 的时机不变（链全过才提交）。
            List<(BlueprintBuilding owner, AttachmentSide side, BlueprintBuilding node, int offset)> pathRebinds;
            SortedSet<int> restoreIdx;
            void BuildRebinds()
            {
                pathRebinds = new List<(BlueprintBuilding, AttachmentSide, BlueprintBuilding, int)>();
                restoreIdx = new SortedSet<int>();
                if (!freeMerge) return;
                foreach (var attachment in attachments)
                {
                    // 机器类绑定点（分流器/机器等非分拣器）：引用不做就近重绑（连接窗口语义
                    // 不同，偏移归零重绑只对分拣器成立）。但目标节点若被本次合并删除，引用会
                    // 悬空 → InserterBinder 就近兜底在密集平行带处会跨线（1080 分流器#238
                    // 实证：基线 44 线 → 兜底后 36 线，违反准则⑦）。修复：目标被删就走
                    // 臂长守卫同款 restoreIdx 通道按弧位回插原目标节点，引用原样保留 → 永远同线。
                    if (!IsInserter(attachment.Owner))
                    {
                        if (attachment.Target != null && pathIndexMap.ContainsKey(attachment.Target)
                            && !replacements.Contains(attachment.Target))
                        {
                            restoreIdx.Add(pathIndexMap[attachment.Target]);
                        }
                        continue;
                    }
                    if (TraceIndex.HasValue && attachment.Owner.index == TraceIndex.Value)
                        Trace($"run {start.index}->{end.index} owner={attachment.Owner.index} target={attachment.Target?.index} inPath={(attachment.Target != null && pathIndexMap.ContainsKey(attachment.Target))} path={path.Count}");
                    else if (IsTraced(attachment.Owner) || IsTraced(attachment.Target))
                        Trace($"run {start.index}->{end.index} owner={attachment.Owner.index}(x{attachment.Owner.localOffset_x:F2},y{attachment.Owner.localOffset_y:F2},item{attachment.Owner.itemId}) target={attachment.Target?.index}(x{attachment.Target?.localOffset_x:F2},y{attachment.Target?.localOffset_y:F2}) inPath={(attachment.Target != null && pathIndexMap.ContainsKey(attachment.Target))} path={path.Count}");
                    if (attachment.Target == null || !pathIndexMap.ContainsKey(attachment.Target)) continue;
                    // 004.15：目标节点仍在替换链中（被锚定/保留为链边界）时直接回指自身——
                    // 同格位双节点的数据病态下，就近搜索会以 0 距离并列撞上**别线**的同位
                    // 节点（先到先得），把引用改指到别的物品条（1080 #1971 实证：基线线#40
                    // → 结果线#36）。回指自身 = 引用不动，交给既有守卫与 binder 校验。
                    if (replacements.Contains(attachment.Target))
                    {
                        if (IsTraced(attachment.Owner) || IsTraced(attachment.Target))
                            Trace($"  selfbind owner={attachment.Owner.index} target={attachment.Target.index} (目标在替换链中，回指自身)");
                        pathRebinds.Add((attachment.Owner, attachment.Side, attachment.Target, 0));
                        continue;
                    }
                    // 就近强制绑定（003.3）：以旧目标节点 T 的位置为基准，绑定到最近的替换节点，
                    // 沿带偏移归零（--pickaudit 证明蓝图存储偏移恒 0，取货点由游戏投影决定）。
                    double best = double.MaxValue;
                    BlueprintBuilding bestNode = null;
                    foreach (var r in replacements)
                    {
                        double dx = r.localOffset_x - attachment.Target.localOffset_x;
                        double dy = r.localOffset_y - attachment.Target.localOffset_y;
                        double dz = r.localOffset_z - attachment.Target.localOffset_z;
                        double d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 < best) { best = d2; bestNode = r; }
                    }
                    if (bestNode == null) continue;
                    // 003.6 臂长守卫（玩家带2实测 5 处失效的根因）：合并删点后"最近替换节点"
                    // 可能比原目标离分拣器远得多（带2跨度 1.5 格 × 1.6~2.1 m/格，实测重绑目标
                    // 达 2.7~6.0 m，而 2 格分拣器臂长仅 ~2.5 m → 游戏拒连 → 输入/输出端失效）。
                    // 新目标一旦比原目标更远 0.6 m 以上，就放弃重绑：把原目标节点回插进链、
                    // 引用原样保留（原几何在游戏里验证过，天然可达）。
                    double oldDist = meters(attachment.Owner, attachment.Target);
                    double newDist = meters(attachment.Owner, bestNode);
                    if (IsTraced(attachment.Owner) || IsTraced(attachment.Target))
                        Trace($"  arm owner={attachment.Owner.index} old={oldDist:F3} new={newDist:F3} best=x{bestNode.localOffset_x:F2},y{bestNode.localOffset_y:F2},idx{bestNode.index} {(newDist > oldDist + 0.6 ? "RESTORE" : "REBIND")}");
                    if (newDist > oldDist + 0.6)
                    {
                        restoreIdx.Add(pathIndexMap[attachment.Target]);
                        continue;
                    }
                    pathRebinds.Add((attachment.Owner, attachment.Side, bestNode, 0));
                }
            }
            BuildRebinds();
            // 注意：pathRebinds 必须等本链通过全部校验（碰撞回插 → 跨度复核 → 悬空自检）后
            // 才提交到全局 rebinds。004.5 之前在这里就 AddRange，导致被 distance_rejected /
            // collision_unresolvable 放弃的链仍把分拣器改指到一条**不会进入结果**的替换节点
            // → 终检悬空（C1绿糖@72° 分拣器 → 插值节点，实测复现）。
            // 003.6 v2（§3.20a 撤销重做）：把"臂长守卫"保下的原目标节点按**弧位**回插进替换链。
            // 003.6 的括位搜索只认原始节点，插值模式（插件双模式取优会采纳插值试验）链序错乱
            // → 粘贴大面积 TooFar。v2：对每个替换节点求弧位——原始节点查 cumulative，插值节点
            // 投影到原折线取弧长——与回插节点一起按弧位整体排序，对两种模式都单调。
            // 004.12：弧位回插 + 碰撞守卫整体抽成可重跑局部函数（格点回退后按新替换集重跑）。
            bool RunRestoreAndGuard()
            {
            if (restoreIdx.Count > 0)
            {
                var pathIndexOf = new Dictionary<BlueprintBuilding, int>();
                for (int i = 0; i < path.Count; i++) pathIndexOf[path[i]] = i;
                double ArcOf(BlueprintBuilding n)
                {
                    if (pathIndexOf.TryGetValue(n, out int ri)) return cumulative[ri];
                    double best = double.MaxValue, bestArc = 0;
                    for (int s = 1; s < path.Count; s++)
                    {
                        double ax = path[s-1].localOffset_x, ay = path[s-1].localOffset_y;
                        double bx = path[s].localOffset_x, by = path[s].localOffset_y;
                        double dx = bx - ax, dy = by - ay;
                        double len2 = dx * dx + dy * dy;
                        double t = len2 < 1e-12 ? 0 : ((n.localOffset_x - ax) * dx + (n.localOffset_y - ay) * dy) / len2;
                        t = Math.Max(0.0, Math.Min(1.0, t));
                        double ex = ax + dx * t - n.localOffset_x, ey = ay + dy * t - n.localOffset_y;
                        double d = ex * ex + ey * ey;
                        if (d < best) { best = d; bestArc = cumulative[s-1] + t * (cumulative[s] - cumulative[s-1]); }
                    }
                    return bestArc;
                }
                var items = new List<(double arc, BlueprintBuilding node)>(replacements.Count + restoreIdx.Count);
                foreach (var r in replacements) items.Add((ArcOf(r), r));
                foreach (int k in restoreIdx) items.Add((cumulative[k], path[k]));
                items.Sort((a, b) => a.arc.CompareTo(b.arc));
                replacements.Clear();
                foreach (var it in items) replacements.Add(it.node);
            }
            // 准则⑤碰撞守卫 v2（可燃冰实测漏洞修复）：003.8 只查"两端都是原始节点"的跨度，
            // 插值模式（双模式取优会采纳插值试验）的插值端点跨度全部漏检 → 弦线压杆。v2：
            // 迭代 [回插→全跨度扫描] 直到无违例：违例跨度回插其弧位区间内离障碍最近的原始
            // 节点（插值端点的弧位由投影求出）；无可回插 → 整链放弃合并。原始相邻对（原状）
            // 豁免；阈值 0.6 赤道格。
            var obstacleSet = new List<BlueprintBuilding>();
            foreach (var bld in buildings) if (!belts.Contains(bld)) obstacleSet.Add(bld);
            if (obstacleSet.Count > 0)
            {
                var pathIndexOf = new Dictionary<BlueprintBuilding, int>();
                for (int i = 0; i < path.Count; i++) pathIndexOf[path[i]] = i;
                // 每杆动态阈值（贴线杆教训：固定 0.6 会把"与原状同样贴近"的跨度也判违规）：
                // R_o = 原始折线离该杆的最近距离 − 0.05 → 只拦比原状更近的跨度。
                var thresholds = new Dictionary<BlueprintBuilding, double>();
                foreach (var o in obstacleSet)
                {
                    // 节点球模型（源码 2179 行：带粘贴碰撞=节点球 r0.23@上浮0.2，非跨度）：
                    // 阈值 = 原始路径**节点**到该障碍的最近 3D 距离（原图节点全部合法）。
                    double m = double.MaxValue;
                    for (int s = 0; s < path.Count; s++)
                    {
                        double ex = path[s].localOffset_x - o.localOffset_x;
                        double ey = path[s].localOffset_y - o.localOffset_y;
                        double ez = path[s].localOffset_z - o.localOffset_z;
                        double d = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                        if (d < m) m = d;
                    }
                    thresholds[o] = m - 0.05;
                }
                double ArcOf(BlueprintBuilding n)
                {
                    if (pathIndexOf.TryGetValue(n, out int ri)) return cumulative[ri];
                    double best = double.MaxValue, bestArc = 0;
                    for (int s = 1; s < path.Count; s++)
                    {
                        double ax = path[s-1].localOffset_x, ay = path[s-1].localOffset_y;
                        double bx = path[s].localOffset_x, by = path[s].localOffset_y;
                        double dx = bx - ax, dy = by - ay;
                        double len2 = dx * dx + dy * dy;
                        double t = len2 < 1e-12 ? 0 : ((n.localOffset_x - ax) * dx + (n.localOffset_y - ay) * dy) / len2;
                        t = Math.Max(0.0, Math.Min(1.0, t));
                        double ex = ax + dx * t - n.localOffset_x, ey = ay + dy * t - n.localOffset_y;
                        if (ex * ex + ey * ey < best) { best = ex * ex + ey * ey; bestArc = cumulative[s-1] + t * (cumulative[s] - cumulative[s-1]); }
                    }
                    return bestArc;
                }
                bool failed = false;
                for (int iter = 0; iter < 12 && !failed; iter++)
                {
                    if (restoreIdx.Count > 0)
                    {
                        var repSet = new HashSet<BlueprintBuilding>(replacements);
                        var items = new List<(double arc, BlueprintBuilding node)>(replacements.Count + restoreIdx.Count);
                        foreach (var r in replacements) items.Add((ArcOf(r), r));
                        foreach (int k in restoreIdx) if (!repSet.Contains(path[k])) items.Add((cumulative[k], path[k]));
                        items.Sort((a, b) => a.arc.CompareTo(b.arc));
                        replacements.Clear();
                        foreach (var it in items) replacements.Add(it.node);
                    }
                    bool restoredAny = false;
                    var repSet2 = new HashSet<BlueprintBuilding>(replacements);
                    for (int i = 1; i < replacements.Count && !failed; i++)
                    {
                        var r = replacements[i];
                        if (pathIndexOf.ContainsKey(r)) continue; // 原始节点=原图合法位置
                        foreach (var o in obstacleSet)
                        {
                            double ex = r.localOffset_x - o.localOffset_x;
                            double ey = r.localOffset_y - o.localOffset_y;
                            double ez = r.localOffset_z - o.localOffset_z;
                            if (Math.Sqrt(ex * ex + ey * ey + ez * ez) >= thresholds[o]) continue;
                            // 该插值节点比原图任何节点都更贴近障碍 → 回插原节点恢复原状密度
                            int bestK = -1; double bestD = double.MaxValue;
                            for (int k = 1; k < path.Count - 1; k++)
                            {
                                // 过滤口径必须是"已在替换链/已回插"，不是 pathIndexOf（它包含全部原始节点）
                                if (repSet2.Contains(path[k]) || restoreIdx.Contains(k)) continue;
                                double ddx = path[k].localOffset_x - o.localOffset_x, ddy = path[k].localOffset_y - o.localOffset_y, ddz = path[k].localOffset_z - o.localOffset_z;
                                double dd = ddx * ddx + ddy * ddy + ddz * ddz;
                                if (dd < bestD) { bestD = dd; bestK = k; }
                            }
                            if (bestK < 0) { failed = true; break; }
                            restoreIdx.Add(bestK);
                            repSet2.Add(path[bestK]);
                            restoredAny = true;
                            break;
                        }
                    }
                    if (!restoredAny) break;
                }
                if (failed) return false;
            }
            return true;
            }
            if (!RunRestoreAndGuard())
            {
                // 004.12：守卫失败不再整链放弃——回退格点保点（全原始节点，守卫对原始节点
                // 不适用、必然通过），重算重绑与守卫后继续走跨度校验；格点也救不了才放弃。
                bool guardRecovered = TryGridFallback();
                if (guardRecovered) { BuildRebinds(); guardRecovered = RunRestoreAndGuard(); }
                if (!guardRecovered)
                {
                    report.Invalid++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"collision_unresolvable"));
                    continue;
                }
            }
            // 跨度校验必须在回插之后（守卫 v2 教训：回插产生的跨度此前未被校验，刺笋@72°
            // 格点模式漏出 3 条越限链节——违反基准准则①）。
            // 004.12 两点强化：
            //  ① 原件相邻对豁免——跨度两端是 path 中相邻的两个原始节点时，该跨度在原件里
            //     逐字存在（碰撞回填/格点保点产物），游戏对原件同样接受或拒绝它，按"原件
            //     同跨度越限(KeptOver)"口径豁免（与离线矩阵 BuildSpanIndex 判定一致），只
            //     记账不拒绝；越上限的计入 KeptOverSpans。
            //  ② 校验失败先试格点回退（重建替换集 + 重算重绑 + 重跑守卫）再复检，不行才拒绝。
            bool SpansValid()
            {
                for(int i=1;i<replacements.Count;i++)
                {
                    double distance=meters(replacements[i-1],replacements[i]);
                    if(pathIndexMap.TryGetValue(replacements[i-1], out int pa) && pathIndexMap.TryGetValue(replacements[i], out int pb) && pb==pa+1)
                    {
                        if(distance>MaximumMeters) rep.KeptOverSpans++;
                        continue;
                    }
                    if(distance<MinimumMeters || distance>cap+0.0002 || distance>MaximumMeters) return false;
                }
                return true;
            }
            if(!SpansValid())
            {
                bool spanRecovered = false;
                if (!chainGrid && TryGridFallback())
                {
                    BuildRebinds();
                    if (RunRestoreAndGuard()) spanRecovered = SpansValid();
                }
                if(!spanRecovered)
                {
                    report.Invalid++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"distance_rejected"));
                    var dbg = new System.Text.StringBuilder("    [distance_rejected] span=");
                    for(int i=0;i<replacements.Count;i++)
                    {
                        if(i>0)
                        {
                            double d=meters(replacements[i-1],replacements[i]);
                            dbg.Append(d.ToString("F4")).Append(' ');
                        }
                        dbg.Append('[').Append(replacements[i].localOffset_x.ToString("F3")).Append(']');
                    }
                    VerboseLine(dbg.ToString());
                    continue;
                }
            }
            // 本链确认交付：现在才提交重绑（见上面"链通过全部校验后才提交"的说明）。
            foreach (var rb in pathRebinds) rebinds.Add(rb);
            report.ReducedRuns++; report.Chains.Add(Row(start,end,path.Count,replacements.Count,length,cap,gridExact?"optimized":(chainGrid?"optimized_grid_fallback":"optimized_resampled")));
            if (createdNodes)
            {
                added.AddRange(replacements.Skip(1).Take(replacements.Count-2).Where(n => newNodes.Contains(n)));
            }
            for(int i=0;i<replacements.Count-1;i++) pending[replacements[i]]=replacements[i+1];
            for(int i=1;i<replacements.Count;i++)
                if(i<replacements.Count-1 || (end.inputObj!=null && path.Contains(end.inputObj))) inputPending[replacements[i]]=replacements[i-1];
            if (chainGrid)
            {
                // Surviving nodes are the original objects, so only the dropped ones leave the set.
                var keptSet = new HashSet<int>(kept);
                for(int i=1;i<path.Count-1;i++) if(!keptSet.Contains(i) && !restoreIdx.Contains(i)) { retained.Remove(path[i]); removedThisRun.Add(path[i]); }
            }
            else
            {
                for(int i=1;i<path.Count-1;i++)
                    if(!fixedIdx.Contains(i) && !restoreIdx.Contains(i) && !nearKeep.Contains(i))
                    {
                        if (TraceIndex.HasValue || TraceX.HasValue)
                        {
                            foreach (var a in attachments)
                                if (ReferenceEquals(a.Target, path[i]) && (IsTraced(a.Owner) || IsTraced(path[i])))
                                    Trace($"REMOVE target of owner {a.Owner.index} (item{a.Owner.itemId}) node {path[i].index} in run {start.index}->{end.index} i={i}/{path.Count} restore={restoreIdx.Count}");
                        }
                        retained.Remove(path[i]); removedThisRun.Add(path[i]);
                    }
                // 004.4 每链自检（可燃冰@68° 悬空异常零星复现）：链的任何 pending 目标若不在
                // retained 内，回滚本链全部改动（恢复被删节点、撤销 pending/rebinds），只丢这一
                // 链的收益——绝不让整图优化因一条链报废。
                bool runOk = true;
                // 合法目标集 = retained ∪ added（插值节点在 added，不在 retained——
                // 只查 retained 会把一切含插值的链误回滚 = 全图无法长带化）。
                var okSet = new HashSet<BlueprintBuilding>(retained);
                foreach (var a in added) okSet.Add(a);
                foreach (var n in replacements)
                {
                    if (pending.TryGetValue(n, out var pv) && !okSet.Contains(pv)) { runOk = false; break; }
                    if (inputPending.TryGetValue(n, out var iv) && !okSet.Contains(iv)) { runOk = false; break; }
                }
                if (!runOk)
                {
                    foreach (var n in removedThisRun) retained.Add(n);
                    foreach (var n in replacements) { pending.Remove(n); inputPending.Remove(n); }
                    if (rebinds.Count > rebindBase) rebinds.RemoveRange(rebindBase, rebinds.Count - rebindBase);
                    // 本链插值节点已追加进全局 added（带原节点陈旧引用）——必须一并撤除，
                    // 否则孤儿节点进结果即悬空（可燃冰@68° 10 条问题链的共同根因）。
                    if (added.Count > addedBase) added.RemoveRange(addedBase, added.Count - addedBase);
                    report.Invalid++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"dangling_guard"));
                    VerboseLine("    [dangling_guard] 本链回滚保图 " + start.localOffset_x.ToString("F2"));
                    continue;
                }
            }
        }
        foreach(var pair in pending) { pair.Key.outputObj=pair.Value; pair.Key.outputFromSlot=0; pair.Key.outputToSlot=1; pair.Key.outputOffset=0; }
        foreach(var pair in inputPending) { pair.Key.inputObj=pair.Value; pair.Key.inputFromSlot=0; pair.Key.inputToSlot=1; pair.Key.inputOffset=0; }
        if (rebindAttachments)
        {
            // 绑定点偏移归零（002 锚定模式的关键配套，003.2 实测有效）：
            // 蓝图里的非零 offset 是"取货点相对目标节点"的位置（0.06 m/格，正 = 指向段下游，
            // 原图统计 32:0 零反例）。合并使目标节点相邻段变长后，游戏按新段几何 + 旧偏移
            // 数值计算取货点 → 取货点漂移出分拣器臂范围 → pickTarget 被置 0 →「输入端失效」。
            // 偏移归零 = 取货点回到目标节点自身位置——该位置在合并前后完全一致，投影必然命中。
            foreach (var attachment in attachments)
            {
                if (attachment.Owner.inputObj != null && belts.Contains(attachment.Owner.inputObj)) attachment.Owner.inputOffset = 0;
                if (attachment.Owner.outputObj != null && belts.Contains(attachment.Owner.outputObj)) attachment.Owner.outputOffset = 0;
            }
        }
        if (rebindAttachments)
        {
            // 就近强制绑定：引用改为离分拣器最近的替换节点，偏移归零（与原图一致——
            // 蓝图存储偏移恒为 0，取货点由游戏按分拣器位置向带路径投影决定）。
            // 分拣器槽位号（belt 侧为 -1）不动。
            foreach (var rb in rebinds)
            {
                int clipped = Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, rb.offset));
                if (rb.side == AttachmentSide.Input)
                {
                    rb.owner.inputObj = rb.node;
                    rb.owner.inputOffset = (sbyte)clipped;
                }
                else
                {
                    rb.owner.outputObj = rb.node;
                    rb.owner.outputOffset = (sbyte)clipped;
                }
                report.Rebound++;
            }
        }
        var result=buildings.Where(retained.Contains).Concat(added).ToArray();
        if (freeMerge)
        {
            // 建造顺序修正：带节点（含插值节点）全部排在非带建筑之前——分拣器的引用一定
            // 指向排在前面的带节点，粘贴逐个放建筑时目标带先于分拣器存在。原有相对顺序
            // 不变（稳定划分）。重排后统一重编号。
            result = result.Where(isBelt).Concat(result.Where(b => !isBelt(b))).ToArray();
        }
        var resultSet=new HashSet<BlueprintBuilding>(result);
        for(int i=0;i<result.Length;i++)
        {
            var b=result[i]; b.index=i;
            if((b.inputObj!=null && !resultSet.Contains(b.inputObj)) || (b.outputObj!=null && !resultSet.Contains(b.outputObj)))
            {
                // 悬空探针：打印违规者与目标的归属（retained/added/已删）。正常路径不应触发；
                // 触发即说明有引用指向了不在结果里的对象，必须让上层放弃本次省带（fail-soft）。
                string Side(BlueprintBuilding r)
                {
                    if (r == null) return "null";
                    string where = retained.Contains(r) ? "retained" : added.Contains(r) ? "added" : "REMOVED";
                    return $"x={r.localOffset_x:F3},y={r.localOffset_y:F3},z={r.localOffset_z:F3},item{r.itemId},{where}";
                }
                Console.WriteLine($"    [悬空探针] {b.index} item{b.itemId} 本体 x={b.localOffset_x:F3},y={b.localOffset_y:F3},z={b.localOffset_z:F3}");
                if (b.inputObj != null && !resultSet.Contains(b.inputObj)) Console.WriteLine($"        输入端 → {Side(b.inputObj)}（悬空）");
                if (b.outputObj != null && !resultSet.Contains(b.outputObj)) Console.WriteLine($"        输出端 → {Side(b.outputObj)}（悬空）");
                throw new InvalidOperationException("Belt optimization left a dangling blueprint reference.");
            }
        }
        return result;
    }
    private static string Row(BlueprintBuilding a, BlueprintBuilding b,int before,int after,double length,double cap,string status) => string.Join(",", a.index,b.index,before,after,length.ToString("F6",System.Globalization.CultureInfo.InvariantCulture),cap.ToString("F6",System.Globalization.CultureInfo.InvariantCulture),status);

    /// <summary>Length of one cargo buffer, from the game's cargo-path data (1 buffer ≈ 0.06 m).</summary>
    private const double BufferMetres = 0.06;

    /// <summary>
    /// Keeps an inserter's along-belt offset inside the segment it was re-pointed to. An inserter
    /// targets the centre buffer of that segment and stores its pick/insert point as an offset from
    /// that centre, so a merge that moves the centre can push the point off the belt. The bound uses
    /// the most permissive buffer size, so a value that could still be valid is never changed.
    /// Returns true when the offset had to be clamped.
    /// </summary>
    private static bool ClampAttachmentOffset(Attachment attachment, BlueprintBuilding segment,
        Func<BlueprintBuilding, BlueprintBuilding, double> meters)
    {
        var following = segment.outputObj;
        if (following == null)
        {
            return false;
        }
        double length = meters(segment, following);
        if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0.0)
        {
            return false;
        }
        int halfBuffers = (int)(length * 0.5 / BufferMetres) - 1;
        if (halfBuffers < 0)
        {
            halfBuffers = 0;
        }
        if (halfBuffers > 127)
        {
            halfBuffers = 127;
        }
        if (attachment.Side == AttachmentSide.Input)
        {
            int current = attachment.Owner.inputOffset;
            int clamped = current < -halfBuffers ? -halfBuffers : (current > halfBuffers ? halfBuffers : current);
            if (clamped != current)
            {
                attachment.Owner.inputOffset = clamped;
                return true;
            }
            return false;
        }
        int value = attachment.Owner.outputOffset;
        int fixedValue = value < -halfBuffers ? -halfBuffers : (value > halfBuffers ? halfBuffers : value);
        if (fixedValue != value)
        {
            attachment.Owner.outputOffset = fixedValue;
            return true;
        }
        return false;
    }
    private static bool Compatible(BlueprintBuilding a,BlueprintBuilding b)=>a.areaIndex==b.areaIndex && a.itemId==b.itemId && a.modelIndex==b.modelIndex;

    /// <summary>杆类小障碍（电线杆/无线输电塔/卫星配电站）：只有它们需要"侧翼锚定"把杆下段原样保留。</summary>
    private static bool IsPoleLike(BlueprintBuilding b) => b.itemId >= 2201 && b.itemId <= 2203;

    /// <summary>
    /// 004.17 带上附加机（PrefabDesc.addonType == EAddonType.Belt，游戏内 addon-dump 实测清单）：
    /// 喷涂机 2313、流速监测器 2030、炮塔 3001/3003/3004/3005/3006/3010。粘贴建成后按连接位 1 m 窗口
    /// 就近接带（蓝图不存引用），其窗口内带节点必须原样保留。用静态清单而非运行时 LDB 查询，
    /// 保证游戏内与离线复刻（A/B 对比）同口径。
    /// </summary>
    internal static bool IsBeltAddon(int itemId)
        => itemId == 2313 || itemId == 2030 || itemId == 3001 || itemId == 3003 || itemId == 3004
           || itemId == 3005 || itemId == 3006 || itemId == 3010;

    /// <summary>分拣器判定（与 InserterBinder 一致）：itemId 2011~2019。</summary>
    private static bool IsInserter(BlueprintBuilding b)=>b.itemId > 2010 && b.itemId < 2020;

    /// <summary>
    /// Fixes the belt piece an inserter/machine attaches to: the node itself plus the neighbours that
    /// shape the local path, so a merge cannot move that geometry away from the inserter.
    /// </summary>
    private static void AnchorPiece(BlueprintBuilding node, Dictionary<BlueprintBuilding, BlueprintBuilding> next,
        HashSet<BlueprintBuilding> belts, HashSet<BlueprintBuilding> anchors)
    {
        if (node == null || !belts.Contains(node))
        {
            return;
        }
        anchors.Add(node);
        BlueprintBuilding following;
        if (next.TryGetValue(node, out following) && following != null && belts.Contains(following))
        {
            anchors.Add(following);
        }
        if (node.inputObj != null && belts.Contains(node.inputObj))
        {
            anchors.Add(node.inputObj);
        }
    }

    /// <summary>Kept for reference; the merge no longer restricts itself around inserters.</summary>
    private static void ProtectNeighbourhood(BlueprintBuilding node, HashSet<BlueprintBuilding> belts, HashSet<BlueprintBuilding> protectedNodes)
    {
        if (node == null || !belts.Contains(node))
        {
            return;
        }
        protectedNodes.Add(node);
        if (node.outputObj != null && belts.Contains(node.outputObj))
        {
            protectedNodes.Add(node.outputObj);
            if (node.outputObj.outputObj != null && belts.Contains(node.outputObj.outputObj))
            {
                protectedNodes.Add(node.outputObj.outputObj);
            }
        }
        if (node.inputObj != null && belts.Contains(node.inputObj))
        {
            protectedNodes.Add(node.inputObj);
            if (node.inputObj.inputObj != null && belts.Contains(node.inputObj.inputObj))
            {
                protectedNodes.Add(node.inputObj.inputObj);
            }
        }
    }
    private static double GridDistance(BlueprintBuilding a,BlueprintBuilding b)
    { double x=b.localOffset_x-a.localOffset_x,y=b.localOffset_y-a.localOffset_y,z=b.localOffset_z-a.localOffset_z; return Math.Sqrt(x*x+y*y+z*z); }
    private static bool Smooth(BlueprintBuilding a,BlueprintBuilding b,BlueprintBuilding c)
    {
        double ax=b.localOffset_x-a.localOffset_x,ay=b.localOffset_y-a.localOffset_y,az=b.localOffset_z-a.localOffset_z;
        double bx=c.localOffset_x-b.localOffset_x,by=c.localOffset_y-b.localOffset_y,bz=c.localOffset_z-b.localOffset_z;
        double length=GridDistance(a,b)*GridDistance(b,c);
        // Numerical noise and gently curved geodesics are resampled along their
        // original polyline; sharp turns and slope transitions remain anchors.
        return length>1e-8 && (ax*bx+ay*by+az*bz)/length>SmoothCosThreshold;
    }
    private static float Round(double value)=>(float)Math.Round(value,4);
    private static BlueprintBuilding Copy(BlueprintBuilding b)=>new BlueprintBuilding
    {
        areaIndex=b.areaIndex,itemId=b.itemId,modelIndex=b.modelIndex,yaw=b.yaw,yaw2=b.yaw2,pitch=b.pitch,pitch2=b.pitch2,
        tilt=b.tilt,tilt2=b.tilt2,filterId=b.filterId,recipeId=b.recipeId,inputToSlot=b.inputToSlot,inputFromSlot=b.inputFromSlot,
        outputToSlot=b.outputToSlot,outputFromSlot=b.outputFromSlot,inputOffset=b.inputOffset,outputOffset=b.outputOffset,
        parameters=b.parameters==null?null:(int[])b.parameters.Clone(),content=b.content,tempInputObjIdx=-1,tempOutputObjIdx=-1
    };
}
