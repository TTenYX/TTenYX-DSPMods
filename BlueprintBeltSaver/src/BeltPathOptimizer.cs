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
        internal readonly List<string> Chains = new List<string> { "start,end,nodes_before,nodes_after,length_m,cap_m,status" };
        public override string ToString() => $"路径 {Runs}，已优化 {ReducedRuns}，无需减少 {TooShort}，固定点 {Protected}，异常连接 {Invalid}，重绑附属连接 {Rebound}，因朝向不一致跳过 {AttachmentYawSkipped}，修正越界偏移 {OffsetClamped}" +
            (OverLimitLinks > 0 ? $"，超限存储连接 {OverLimitLinks} 条（固定其邻域 {OverLimitPinned} 点）" : "");
    }
    internal static BlueprintBuilding[] Optimize(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
        Func<BlueprintBuilding, float> limit) => Optimize(buildings, isBelt, limit,
            (a,b) => GridDistance(a,b) * 1.2566370614359173, out _);

    internal static BlueprintBuilding[] Optimize(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
        Func<BlueprintBuilding, float> limit, Func<BlueprintBuilding, BlueprintBuilding, double> meters, out Report report,
        bool rebindAttachments = false, bool gridExact = true, bool pinOverLimitLinks = false, bool projectionRebind = false)
    {
        report = new Report();
        var belts = new HashSet<BlueprintBuilding>(buildings.Where(isBelt));
        var next = belts.ToDictionary(b => b, b => b.outputObj);
        var conflicts = new HashSet<BlueprintBuilding>();
        // A belt input may encode an edge not present in the predecessor output.
        foreach (var b in belts)
            if (b.inputObj != null && belts.Contains(b.inputObj))
            {
                var prev = b.inputObj;
                if (next[prev] == null) next[prev] = b;
                else if (next[prev] != b) { conflicts.Add(prev); conflicts.Add(b); }
            }
        var incoming = belts.ToDictionary(b => b, b => new List<BlueprintBuilding>());
        foreach (var b in belts)
            if (next[b] != null && belts.Contains(next[b])) incoming[next[b]].Add(b);
        var anchors = new HashSet<BlueprintBuilding>(conflicts);
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
            // 忽略分拣器与机器：不做任何绑定点锚定。
        }
        else if (rebindAttachments)
        {
            foreach (var attachment in attachments)
            {
                if (attachment.Target != null && belts.Contains(attachment.Target)) anchors.Add(attachment.Target);
            }
        }
        else
        {
            foreach (var b in buildings.Where(b => !belts.Contains(b)))
            {
                if (b.inputObj != null && belts.Contains(b.inputObj)) AnchorPiece(b.inputObj, next, belts, anchors);
                if (b.outputObj != null && belts.Contains(b.outputObj)) AnchorPiece(b.outputObj, next, belts, anchors);
            }
        }

        // Iron rule guard (A), restricted to blueprints that span more than one latitude band.
        //
        // A stored link that is longer than the hard limit cannot survive the paste: the game rejects
        // the whole paste ("距离太远"), or, when the link is missing/refused, rebuilds the chain from
        // whatever belt ends happen to be nearby (PlanetFactory proximity matching). Deleting the nodes
        // around such a link would take away the only anchors that rebuild can use, so the link and its
        // immediate neighbours are pinned. With a correct length model this fires zero times on the
        // blueprints measured here - it is a defence in depth, not the fix for the equator problem.
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
        // Smooth cycles need at least two fixed points to retain their extent.
        var visited = new HashSet<BlueprintBuilding>();
        foreach (var b in belts)
        {
            if (visited.Contains(b)) continue;
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
        report.Protected = anchors.Count; report.Invalid = conflicts.Count;
        var retained = new HashSet<BlueprintBuilding>(buildings);
        var added = new List<BlueprintBuilding>();
        var pending = new Dictionary<BlueprintBuilding,BlueprintBuilding>();
        var inputPending = new Dictionary<BlueprintBuilding,BlueprintBuilding>();
        // 落点投影重绑清单：跨路径收集，路径循环结束后统一应用（见循环尾部的 rebinds.Add）。
        var rebinds = new List<(BlueprintBuilding owner, AttachmentSide side, BlueprintBuilding node, int offset)>();
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
            var replacements = new List<BlueprintBuilding>{start};
            List<int> kept = null;
            bool createdNodes = false;
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
                // 贪心切割（插值，003.2）：按上限逐段切割，切点留 1 mm 余量——插值坐标 4 位
                // 舍入后实测段长会略超切点间距，贴着 cap 切会被 valid 检查整条拒绝
                //（实测 10 节点直链复现）。
                bool unfit = false;
                int bracket = 0;
                double cutArc = 0.0;
                while (true)
                {
                    double cutLimit = Math.Min(cutArc + cap - 0.001, length);
                    if (cutLimit >= length - 1e-9) break;
                    int k = bracket;
                    while (k < path.Count - 1 && cumulative[k + 1] <= cutLimit) k++;
                    double delta = cumulative[k + 1] - cumulative[k];
                    double tCut = delta < 1e-9 ? 0 : (cutLimit - cumulative[k]) / delta;
                    // Metadata (yaw/tilt/pitch/model) comes from an INTERIOR node, never from a run
                    // endpoint: a run's first/last node is frequently a diagonal corner belt.
                    int sourceIndex = path.Count >= 3 ? Math.Min(Math.Max(k + 1, 1), path.Count - 2) : k;
                    var node = Copy(path[sourceIndex]);
                    node.localOffset_x = Round(path[k].localOffset_x + (path[k + 1].localOffset_x - path[k].localOffset_x) * tCut);
                    node.localOffset_y = Round(path[k].localOffset_y + (path[k + 1].localOffset_y - path[k].localOffset_y) * tCut);
                    node.localOffset_z = Round(path[k].localOffset_z + (path[k + 1].localOffset_z - path[k].localOffset_z) * tCut);
                    node.localOffset_x2 = node.localOffset_x; node.localOffset_y2 = node.localOffset_y; node.localOffset_z2 = node.localOffset_z;
                    replacements.Add(node);
                    cutArc = cutLimit;
                    bracket = k;
                }
                if (unfit)
                {
                    report.Invalid++;
                    report.Chains.Add(Row(start,end,path.Count,path.Count,length,cap,"unfit"));
                    continue;
                }
                replacements.Add(end);
                createdNodes = true;
            }
            bool valid=true;
            for(int i=1;i<replacements.Count;i++)
            {
                double distance=meters(replacements[i-1],replacements[i]);
                if(distance<MinimumMeters || distance>cap+0.0002 || distance>MaximumMeters) { valid=false; break; }
            }
            if(!valid)
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
                Console.WriteLine(dbg.ToString());
                continue;
            }
            // 002→004 落点投影重绑（玩家方案）：合并**完全忽略分拣器**（不再原位锚定、不再
            // 弧长覆盖重指），合并后按"分拣器取货点坐标 → 就近带节点 + 偏移重锚定"重新接上。
            // 取货点弧位 = 旧段中心 - 存储偏移×0.06（符号约定由原图统计得出：397:0 / 128:4，
            // offset 正方向 = 指向段首/上游）。
            var pathRebinds = new List<(BlueprintBuilding owner, AttachmentSide side, BlueprintBuilding node, int offset)>();
            if (freeMerge)
            {
                var pathIndex = new Dictionary<BlueprintBuilding, int>();
                for (int i = 0; i < path.Count; i++) pathIndex[path[i]] = i;
                foreach (var attachment in attachments)
                {
                    if (attachment.Target == null || !pathIndex.TryGetValue(attachment.Target, out _)) continue;
                    // 就近强制绑定（003.3）：以**旧目标节点 T 的位置**为基准（T 必在分拣器要绑
                    // 的那条带上；用分拣器中心做基准会在"一进一出双带"场景跨带错绑），绑定到
                    // 最近的替换节点，沿带偏移归零。依据：原图审计（--pickaudit）证明蓝图存储
                    // 偏移恒为 0（1080 图 443/443 全 0），取货点由游戏按分拣器位置向带路径投影
                    // 决定——写入任何非零偏移都会把取货点人为挪离分拣器（003.1 失效的直接原因）。
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
                    pathRebinds.Add((attachment.Owner, attachment.Side, bestNode, 0));
                }
            }
            foreach (var rb in pathRebinds) rebinds.Add(rb);
            report.ReducedRuns++; report.Chains.Add(Row(start,end,path.Count,replacements.Count,length,cap,gridExact?"optimized":"optimized_resampled"));
            if (createdNodes)
            {
                added.AddRange(replacements.Skip(1).Take(replacements.Count-2));
            }
            for(int i=0;i<replacements.Count-1;i++) pending[replacements[i]]=replacements[i+1];
            for(int i=1;i<replacements.Count;i++)
                if(i<replacements.Count-1 || (end.inputObj!=null && path.Contains(end.inputObj))) inputPending[replacements[i]]=replacements[i-1];
            if (gridExact)
            {
                // Surviving nodes are the original objects, so only the dropped ones leave the set.
                var keptSet = new HashSet<int>(kept);
                for(int i=1;i<path.Count-1;i++) if(!keptSet.Contains(i)) retained.Remove(path[i]);
            }
            else
            {
                for(int i=1;i<path.Count-1;i++) retained.Remove(path[i]);
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
                throw new InvalidOperationException("Belt optimization left a dangling blueprint reference.");
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
        return length>1e-8 && (ax*bx+ay*by+az*bz)/length>Math.Cos(Math.PI/180.0);
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
