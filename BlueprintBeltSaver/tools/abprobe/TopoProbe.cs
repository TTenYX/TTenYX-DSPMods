// 004.12 拓扑门禁探针：验证 VerifyTopology 的对象身份假设是否成立。
// 结论输出：Clone 是否共享对象引用；对真实蓝图跑"格点+插值采纳+拓扑门禁"的完整复刻，
// 观察带分拣器蓝图是否被误拒（被跨线重绑）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueprintBeltSaver;

internal static class TopoProbe
{
    private static bool IsBelt(BlueprintBuilding n) => n.itemId >= 2001 && n.itemId <= 2009;

    private static long SpanSideKey(BlueprintBuilding node)
    {
        long area = (long)(node.areaIndex & 0xFF) << 56;
        long x = ((long)Math.Round(node.localOffset_x * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
        long y = ((long)Math.Round(node.localOffset_y * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
        return area | (x << 28) | y;
    }

    private static HashSet<(long, long)> BuildSpanIndex(BlueprintBuilding[] buildings)
    {
        var spans = new HashSet<(long, long)>();
        foreach (var b in buildings)
        {
            if (b == null || !IsBelt(b) || b.outputObj == null || !IsBelt(b.outputObj)) continue;
            spans.Add((SpanSideKey(b), SpanSideKey(b.outputObj)));
        }
        return spans;
    }

    internal static string Fingerprint(BlueprintBuilding[] arr)
    {
        if (arr == null) return "null";
        ulong h = 14695981039346656037UL;
        int belts = 0;
        foreach (var b in arr)
        {
            if (b == null) { h = (h ^ 0xFF) * 1099511628211UL; continue; }
            h = (h ^ (ulong)(uint)b.itemId) * 1099511628211UL;
            long qx = (long)Math.Round(b.localOffset_x * 8192.0, MidpointRounding.AwayFromZero);
            long qy = (long)Math.Round(b.localOffset_y * 8192.0, MidpointRounding.AwayFromZero);
            long qz = (long)Math.Round(b.localOffset_z * 8192.0, MidpointRounding.AwayFromZero);
            h = (h ^ (ulong)qx) * 1099511628211UL;
            h = (h ^ (ulong)qy) * 1099511628211UL;
            h = (h ^ (ulong)qz) * 1099511628211UL;
            if (IsBelt(b))
            {
                belts++;
                var o = b.outputObj;
                if (o != null)
                {
                    h = (h ^ (ulong)(long)Math.Round(o.localOffset_x * 8192.0, MidpointRounding.AwayFromZero)) * 1099511628211UL;
                    h = (h ^ (ulong)(long)Math.Round(o.localOffset_y * 8192.0, MidpointRounding.AwayFromZero)) * 1099511628211UL;
                }
                else h = (h ^ 0xEE) * 1099511628211UL;
            }
        }
        return belts + "/" + h.ToString("X16");
    }

    internal static (long area, long xy) PosKeyA(BlueprintBuilding b)
    {
        long area = (long)(b.areaIndex & 0xFF) << 56;
        long xy = ((long)Math.Round(b.localOffset_x * 8192.0) << 20) ^ (long)Math.Round(b.localOffset_y * 8192.0);
        return (area, xy);
    }

    internal static Dictionary<BlueprintBuilding, int> LinesOf(BlueprintBuilding[] buildings)
    {
        var belts = buildings.Where(IsBelt).ToArray();
        var parent = belts.ToDictionary(x => x, x => x);
        BlueprintBuilding Find(BlueprintBuilding x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        foreach (var b in belts)
        {
            if (b.outputObj != null && IsBelt(b.outputObj) && parent.ContainsKey(b.outputObj))
            { var ra = Find(b); var rb = Find(b.outputObj); if (ra != rb) parent[ra] = rb; }
            if (b.inputObj != null && IsBelt(b.inputObj) && parent.ContainsKey(b.inputObj))
            { var ra = Find(b); var rb = Find(b.inputObj); if (ra != rb) parent[ra] = rb; }
        }
        var lineOf = new Dictionary<BlueprintBuilding, int>();
        int next = 0; var rootId = new Dictionary<BlueprintBuilding, int>();
        foreach (var b in belts)
        {
            var r = Find(b);
            if (!rootId.TryGetValue(r, out int id)) id = rootId[r] = next++;
            lineOf[b] = id;
        }
        return lineOf;
    }

    internal static Dictionary<int, (BlueprintBuilding inT, BlueprintBuilding outT, int inLine, int outLine)> RefLines(BlueprintBuilding[] buildings,
        Dictionary<BlueprintBuilding, int> lines)
    {
        // 004.15：连目标对象一起抓——Optimize 就地改写引用，事后读不到基线目标本体。
        var refs = new Dictionary<int, (BlueprintBuilding inT, BlueprintBuilding outT, int inLine, int outLine)>();
        for (int i = 0; i < buildings.Length; i++)
        {
            var n = buildings[i];
            if (n == null || IsBelt(n)) continue;
            var inT = n.inputObj != null && IsBelt(n.inputObj) ? n.inputObj : null;
            var outT = n.outputObj != null && IsBelt(n.outputObj) ? n.outputObj : null;
            int inLine = inT != null && lines.TryGetValue(inT, out int li) ? li : -1;
            int outLine = outT != null && lines.TryGetValue(outT, out int lo) ? lo : -1;
            refs[i] = (inT, outT, inLine, outLine);
        }
        return refs;
    }

    // 基线版拓扑门禁：preLines/preRefs = 合并改写引用之前抓取的 Clone 侧线路归属与引用所在线；
    // 只对账"优化器造成的跨线改写"，Clone 自身的端点重吸附（游戏拷贝行为）在基线里豁免。
    // failPos/failInput：失败附件在 preMerge 中的位置与端别（004.15 门禁反馈重跑用，-1=不可重试）。
    internal static (bool ok, string reason, int linesB, int linesA, int failPos, bool failInput) VerifyTopologyBase(
        Dictionary<BlueprintBuilding, int> preLines, Dictionary<int, (BlueprintBuilding inT, BlueprintBuilding outT, int inLine, int outLine)> preRefs,
        BlueprintBuilding[] preMerge, BlueprintBuilding[] result)
    {
        var after = LinesOf(result);
        int linesBefore = preLines.Values.Distinct().Count();
        int linesAfter = after.Values.Distinct().Count();
        var posLines = new Dictionary<(long, long), HashSet<int>>();
        int ambiguous = 0;
        foreach (var kv in preLines)
        {
            var key = PosKeyA(kv.Key);
            if (!posLines.TryGetValue(key, out var set)) set = posLines[key] = new HashSet<int>();
            set.Add(kv.Value);
        }
        var resultSet = new HashSet<BlueprintBuilding>(result);
        var o2r = new Dictionary<int, int>();
        var r2o = new Dictionary<int, int>();
        int mapped = 0;
        for (int i = 0; i < preMerge.Length; i++)
        {
            var cur = preMerge[i];
            if (cur == null || !IsBelt(cur) || !preLines.TryGetValue(cur, out int o)) continue;
            if (!after.TryGetValue(cur, out int r) || !resultSet.Contains(cur)) continue;
            if (o2r.TryGetValue(o, out var r0) && r0 != r) return (false, $"原线#{o} 分裂到结果线 {r0}/{r}（断链）[mapped={mapped}]", linesBefore, linesAfter, -1, false);
            o2r[o] = r; mapped++;
            if (r2o.TryGetValue(r, out var o0) && o0 != o) return (false, $"结果线#{r} 串接了原线 {o0}/{o}（串线）[mapped={mapped}]", linesBefore, linesAfter, -1, false);
            r2o[r] = o;
        }
        if (linesBefore != linesAfter) return (false, $"线路数 {linesBefore} → {linesAfter}", linesBefore, linesAfter, -1, false);
        int sorters = 0;
        for (int i = 0; i < preMerge.Length; i++)
        {
            var cur = preMerge[i];
            if (cur == null || IsBelt(cur) || !preRefs.TryGetValue(i, out var pr)) continue;
            foreach (var sideIsInput in new[] { true, false })
            {
                int preLine = sideIsInput ? pr.inLine : pr.outLine;
                if (preLine < 0) continue;
                sorters++;
                var targetNow = sideIsInput ? cur.inputObj : cur.outputObj;
                if (targetNow == null || !IsBelt(targetNow) || !after.ContainsKey(targetNow))
                    return (false, $"建筑#{cur.index} 的带引用丢失 [mapped={mapped}]", linesBefore, linesAfter, i, sideIsInput);
                if (!o2r.TryGetValue(preLine, out var rLine) || rLine != after[targetNow])
                {
                    // 004.12-d 位置歧义豁免
                    var keyA = PosKeyA(targetNow);
                    if (posLines.TryGetValue(keyA, out var setA) && setA.Contains(preLine)) { ambiguous++; continue; }
                    BlueprintBuilding preCounter = null;
                    foreach (var ob in preMerge)
                    {
                        if (ob == null || !IsBelt(ob)) continue;
                        if (Math.Abs(ob.localOffset_x - targetNow.localOffset_x) < 1e-4 &&
                            Math.Abs(ob.localOffset_y - targetNow.localOffset_y) < 1e-4 && ob.areaIndex == targetNow.areaIndex)
                        { preCounter = ob; break; }
                    }
                    int preLineOfNow = preCounter != null && preLines.TryGetValue(preCounter, out int pl) ? pl : -1;
                    Console.WriteLine($"    [trace] owner idx={cur.index} item={cur.itemId} side={(sideIsInput ? "in" : "out")} 基线线#{preLine} 结果线#{rLine} 现目标在Clone图的线={preLineOfNow} (新增?{preCounter == null}) 基线目标({(sideIsInput ? preMerge[i].inputObj : preMerge[i].outputObj)?.localOffset_x.ToString("F2")},{(sideIsInput ? preMerge[i].inputObj : preMerge[i].outputObj)?.localOffset_y.ToString("F2")}) 现目标({targetNow.localOffset_x:F2},{targetNow.localOffset_y:F2})");
                    return (false, $"建筑#{cur.index} 被跨线重绑（基线#{preLine}→结果#{after[targetNow]}）[mapped={mapped}]", linesBefore, linesAfter, i, sideIsInput);
                }
            }
        }
        return (true, $"OK lines {linesBefore} mapped={mapped} sorterRefs={sorters} ambiguous={ambiguous}", linesBefore, linesAfter, -1, false);
    }

    private static void Main(string[] args)
    {
        // 004.17：输出统一 UTF-8（重定向到文件后可直接读；GBK 控制台回显会乱码，见手册坑 #8）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args.Length > 0 && args[0] == "--bisect")
        {
            string btext = File.ReadAllText(args[1]).Trim();
            var bsource = new BlueprintData();
            bsource.FromBase64String(btext);
            Console.WriteLine("src=" + Fingerprint(bsource.buildings));
            var bwork = bsource.Clone();
            Console.WriteLine("clone=" + Fingerprint(bwork.buildings));
            double bcapO = 2.301172 * 0.98 * CrossLatitudeSafety.BandMargin;
            var bobs = BeltSpacingCalibration.Observation.FromYaw(0.0, 0f);
            var bcal = BeltSpacingCalibration.Resolve(bwork.areas, bwork.buildings, IsBelt, bobs);
            Func<BlueprintBuilding, BlueprintBuilding, double> bmeters =
                CrossLatitudeSafety.Wrap(bwork.areas, bwork.buildings, IsBelt, bobs, bcal, true);
            bool bpin = bcal.MultiBand;
            var bgB = BeltPathOptimizer.Optimize(bwork.buildings, IsBelt, _ => (float)bcapO, bmeters, out _, true, true, bpin, true);
            Console.WriteLine($"grid({bgB.Count(IsBelt)})=" + Fingerprint(bgB));
            var btrial = bsource.Clone();
            var brB = BeltPathOptimizer.Optimize(btrial.buildings, IsBelt, _ => (float)bcapO, bmeters, out _, true, false, bpin, true);
            Console.WriteLine($"resample({brB.Count(IsBelt)})=" + Fingerprint(brB));
            return;
        }
        if (args.Length > 0 && args[0] == "--semdiff")
        {
            var sa = new BlueprintData(); sa.FromBase64String(File.ReadAllText(args[1]));
            var sb = new BlueprintData(); sb.FromBase64String(File.ReadAllText(args[2]));
            Console.WriteLine($"A: buildings={sa.buildings.Length} belts={sa.buildings.Count(IsBelt)} | B: buildings={sb.buildings.Length} belts={sb.buildings.Count(IsBelt)}");
            var sigA = new SortedDictionary<string,int>();
            var sigB = new SortedDictionary<string,int>();
            // 建筑签名多重集：itemId + 量化坐标 + 引用目标量化坐标（输入/输出）
            string Sig(BlueprintBuilding b)
            {
                long qx = (long)Math.Round(b.localOffset_x * 8192.0, MidpointRounding.AwayFromZero);
                long qy = (long)Math.Round(b.localOffset_y * 8192.0, MidpointRounding.AwayFromZero);
                long qz = (long)Math.Round(b.localOffset_z * 8192.0, MidpointRounding.AwayFromZero);
                string r = ".";
                if (IsBelt(b))
                {
                    var o = b.outputObj;
                    r = o == null ? "x" : $"{Math.Round(o.localOffset_x * 8192.0, MidpointRounding.AwayFromZero)},{Math.Round(o.localOffset_y * 8192.0, MidpointRounding.AwayFromZero)},{Math.Round(o.localOffset_z * 8192.0, MidpointRounding.AwayFromZero)}";
                }
                return $"{b.itemId}|{qx},{qy},{qz}|{b.areaIndex}|{r}";
            }
            foreach (var b in sa.buildings) { var k = Sig(b); sigA[k] = sigA.TryGetValue(k, out var c) ? c + 1 : 1; }
            foreach (var b in sb.buildings) { var k = Sig(b); sigB[k] = sigB.TryGetValue(k, out var c) ? c + 1 : 1; }
            int onlyA = 0, onlyB = 0, common = 0;
            foreach (var kv in sigA)
            {
                sigB.TryGetValue(kv.Key, out int cb);
                int ca = kv.Value, m = Math.Min(ca, cb);
                common += m; onlyA += ca - m;
                if (ca > cb) Console.WriteLine($"  仅A: {kv.Key} ×{ca - cb}");
            }
            foreach (var kv in sigB)
            {
                sigA.TryGetValue(kv.Key, out int ca);
                int cb = kv.Value, m = Math.Min(ca, cb);
                onlyB += cb - m;
                if (cb > ca) Console.WriteLine($"  仅B: {kv.Key} ×{cb - ca}");
            }
            Console.WriteLine(onlyA == 0 && onlyB == 0 ? "SEMANTIC-IDENTICAL" : $"SEMANTIC-DIFF 仅A节点{onlyA} 仅B节点{onlyB} 共同{common}");
            return;
        }
        if (args.Length > 0 && args[0] == "--models")
        {
            foreach (var f in Directory.GetFiles(args[1], "*.txt").OrderBy(x => x, StringComparer.Ordinal))
            {
                var src = new BlueprintData();
                if (src.FromBase64String(File.ReadAllText(f)) != BlueprintDataIOError.OK) { Console.WriteLine(Path.GetFileName(f) + " parse-fail"); continue; }
                var w = src.Clone();
                var obsM = BeltSpacingCalibration.Observation.FromYaw(0.0, 0f);
                var cal = BeltSpacingCalibration.Resolve(w.areas, w.buildings, b => b.itemId >= 2001 && b.itemId <= 2009, obsM);
                Console.WriteLine(Path.GetFileNameWithoutExtension(f) + " :: " + cal);
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--export")
        {
            ExportTool.Run(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--dims")
        {
            // 004.17：区域/建筑范围/合法粘贴姿态概览（游戏 GenerateAreaGratBoxByBPData 判定）
            var dimFiles = Directory.Exists(args[1])
                ? Directory.GetFiles(args[1], "*.txt").OrderBy(x => x, StringComparer.Ordinal).ToArray()
                : new[] { args[1] };
            foreach (var f in dimFiles) PasteAudit.Dims(f, Console.Out);
            return;
        }
        if (args.Length > 0 && args[0] == "--pasteaudit")
        {
            // 004.17：游戏同式 × 全合法粘贴姿态的断链审计（准则①/⑧ 地面真值）
            Environment.ExitCode = PasteAudit.Run(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--gap")
        {
            // 004.17：省带缺口分析（锚点构成 + 逐链状态 + 相对理论下限的缺口）
            GapAnalysis.Run(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--anchorwhy")
        {
            // 004.17：结构锚点归因（分叉/合流、等级不同、转折角度分档、元数据）
            AnchorWhy.Run(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--addons")
        {
            // 004.17：附加机（喷涂机/流速监测器/炮塔）连接口普查：产物是否保住原件的接带节点
            AddonCensus.Run(args);
            return;
        }
        string file = args[0];
        string text = File.ReadAllText(file);
        var source = new BlueprintData();
        var err = source.FromBase64String(text);
        Console.WriteLine($"load err={err} buildings={source.buildings?.Length} belts={source.buildings.Count(IsBelt)} sorters={source.buildings.Count(x => x.itemId >= 2010 && x.itemId <= 2013)}");
        double capO = 2.301172 * 0.98 * CrossLatitudeSafety.BandMargin;
        var obs = BeltSpacingCalibration.Observation.FromYaw(0.0, 0f);
        // 004.15 门禁反馈重跑（与插件 TryOptimize 同步）：跨线/丢失的附件目标窗口按 source
        // 数组位置作为 extraAnchorPositions 传入重跑（≤4 轮），锚点即链边界。
        List<int> extraAnchors = null;
        BlueprintData chosen = null;
        BlueprintBuilding[] preChosen = null;
        Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)> beltBaseRefs = null;
        (bool ok, string reason, int linesB, int linesA, int failPos, bool failInput) topo = default;
        for (int attempt = 0; ; attempt++)
        {
        var work = source.Clone();
        Console.WriteLine($"clone objects shared? {ReferenceEquals(source.buildings[0], work.buildings[0])}");
        var calibration = BeltSpacingCalibration.Resolve(work.areas, work.buildings, IsBelt, obs);
        // 004.16 跨纬度安全（与插件默认配置同步）：距离取纬度带内最坏。
        Func<BlueprintBuilding, BlueprintBuilding, double> meters =
            CrossLatitudeSafety.Wrap(work.areas, work.buildings, IsBelt, obs, calibration, true);
        bool pinOverLimit = calibration.MultiBand;

        var trial = source.Clone();
        var preGrid = work.buildings;
        // 准则⑦ 基线：合并改写引用之前抓取 Clone 侧线路归属与非带引用所在线
        var preLinesT = LinesOf(trial.buildings);
        var preRefsT = RefLines(trial.buildings, preLinesT);
        // 004.15：带节点基线引用图快照（重跑窗口需要 T 的基线邻居，现读引用已被改写）
        beltBaseRefs = new Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)>();
        foreach (var n in trial.buildings)
        {
            if (n == null || !IsBelt(n)) continue;
            beltBaseRefs[n] = (n.inputObj != null && IsBelt(n.inputObj) ? n.inputObj : null,
                               n.outputObj != null && IsBelt(n.outputObj) ? n.outputObj : null);
        }
        var gB = BeltPathOptimizer.Optimize(work.buildings, IsBelt, _ => (float)capO, meters, out var repG, true, true, pinOverLimit, true, extraAnchors);
        work.buildings = gB;
        var preTrial = trial.buildings;
        var rB = BeltPathOptimizer.Optimize(trial.buildings, IsBelt, _ => (float)capO, meters, out var repR, true, false, pinOverLimit, true, extraAnchors);
        trial.buildings = rB;
        int gAfter = work.buildings.Count(IsBelt), rAfter = trial.buildings.Count(IsBelt);
        int before = source.buildings.Count(IsBelt);
        var origSpans = BuildSpanIndex(source.buildings);
        var gOver = OverNew(work.buildings, calibration, origSpans);
        var rOver = OverNew(trial.buildings, calibration, origSpans);
        bool adopt = rOver.over == 0 && rAfter < before && (gOver.over > 0 || rAfter < gAfter);
        chosen = adopt ? trial : work;
        preChosen = adopt ? preTrial : preGrid;
        Console.WriteLine($"before={before} grid→{gAfter}(OverNew={gOver.over}) resample→{rAfter}(OverNew={rOver.over}) adoptResample={adopt}");

        chosen.CheckBuildingData();
        // 悬空审计：哪些非带建筑的引用指向了不在结果里的节点
        var resSet = new HashSet<BlueprintBuilding>(chosen.buildings);
        foreach (var b in preChosen)
        {
            if (b == null || IsBelt(b)) continue;
            foreach (var isIn in new[] { true, false })
            {
                var t = isIn ? b.inputObj : b.outputObj;
                if (t == null || !IsBelt(t) || resSet.Contains(t)) continue;
                Console.WriteLine($"    [dangling] pos{Array.IndexOf(preChosen,b)} item={b.itemId} {(isIn?"in":"out")}→目标({t.localOffset_x:F2},{t.localOffset_y:F2}) item{t.itemId} 在原件源数组中存在={Array.IndexOf(source.buildings, t) >= 0}");
            }
        }
        var resultLinesForBinder = TopoProbe.LinesOf(chosen.buildings);
        var r2oForBinder = new Dictionary<int, int>();
        foreach (var kv in resultLinesForBinder)
        {
            if (!preLinesT.TryGetValue(kv.Key, out int o)) continue;
            if (r2oForBinder.TryGetValue(kv.Value, out var o0) && o0 != o) continue;
            r2oForBinder[kv.Value] = o;
        }
        int LineOfForBinder(BlueprintBuilding n)
            => n == null ? -1
             : preLinesT.TryGetValue(n, out int pl) ? pl
             : resultLinesForBinder.TryGetValue(n, out int rl) && r2oForBinder.TryGetValue(rl, out int bl) ? bl
             : -1;
        var ownerBaseTargets = new Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)>();
        for (int i = 0; i < preChosen.Length; i++)
        {
            if (preChosen[i] != null && !IsBelt(preChosen[i]) && preRefsT.TryGetValue(i, out var prB))
                ownerBaseTargets[preChosen[i]] = (prB.inT, prB.outT);
        }
        var binder = InserterBinder.Repair(chosen.buildings, IsBelt, meters, LineOfForBinder,
            (o, isInput, cand) => ownerBaseTargets.TryGetValue(o, out var t) &&
                (isInput ? ReferenceEquals(t.inT, cand) : ReferenceEquals(t.outT, cand)));
        Console.WriteLine($"binder: {binder}");
        // 深度解剖：找失败分拣器（clone idx 1840），追其 output 目标 T 在原件图与结果图中的邻居
        {
            int ownerPos = -1;
            for (int i = 0; i < preChosen.Length; i++)
                if (preChosen[i] != null && !IsBelt(preChosen[i]) && preChosen[i].index == 1840) { ownerPos = i; break; }
            if (ownerPos >= 0)
            {
                // 非带建筑跨优化幸存（对象同一、引用就地改写），直接用 preChosen 侧对象；
                // chosen.buildings 是压缩重建后的数组，按下标取会错位（曾致 NRE）。
                var ownerRes = preChosen[ownerPos];
                var T = ownerRes.outputObj;
                Console.WriteLine($"    [T] item={T?.itemId} pos=({T?.localOffset_x:F2},{T?.localOffset_y:F2}) afterLine={(T != null && resultLinesForBinder.ContainsKey(T) ? resultLinesForBinder[T].ToString() : "—")}");
                if (T != null)
                {
                    Console.WriteLine($"    [T.result邻居] in=({T.inputObj?.localOffset_x:F2},{T.inputObj?.localOffset_y:F2}) after{(T.inputObj != null && resultLinesForBinder.ContainsKey(T.inputObj) ? resultLinesForBinder[T.inputObj].ToString() : "?")} | out=({T.outputObj?.localOffset_x:F2},{T.outputObj?.localOffset_y:F2}) after{(T.outputObj != null && resultLinesForBinder.ContainsKey(T.outputObj) ? resultLinesForBinder[T.outputObj].ToString() : "?")}");
                    // 原件图里同位置节点（源侧，未被改写）
                    foreach (var ob in source.buildings)
                    {
                        if (ob == null || !IsBelt(ob)) continue;
                        if (Math.Abs(ob.localOffset_x - T.localOffset_x) < 1e-4 && Math.Abs(ob.localOffset_y - T.localOffset_y) < 1e-4 && ob.areaIndex == T.areaIndex)
                        {
                            Console.WriteLine($"    [原件同位置] item={ob.itemId} in=({ob.inputObj?.localOffset_x:F2},{ob.inputObj?.localOffset_y:F2}) out=({ob.outputObj?.localOffset_x:F2},{ob.outputObj?.localOffset_y:F2})");
                        }
                    }
                }
            }
        }
        topo = VerifyTopologyBase(preLinesT, preRefsT, preChosen, chosen.buildings);
        Console.WriteLine($"repG: {repG}");
        Console.WriteLine($"repR: {repR}");
        Console.WriteLine($"IsInserter(2014)={InserterBinder.IsInserterItem(2014)}");
        Console.WriteLine($"topology: ok={topo.ok} lines {topo.linesB}→{topo.linesA} :: {topo.reason}");
        if (!topo.ok && topo.failPos >= 0 && attempt < 3 && preRefsT.TryGetValue(topo.failPos, out var failRef))
        {
            // 004.15 门禁反馈重跑：锚定基线目标窗口（基线目标本体+前后邻居，按位置传下一轮；
            // preChosen 的引用已被就地改写，必须用 RefLines 快照里的目标）。
            var failT = topo.failInput ? failRef.inT : failRef.outT;
            Console.WriteLine($"=== 门禁反馈重跑（第 {attempt + 2} 轮）：锚定目标窗口 failPos={topo.failPos} ===");
            BeltPathOptimizer.TraceIndex = preChosen[topo.failPos].index;
            if (failT != null)
            {
                int pT = Array.IndexOf(preChosen, failT);
                var bIn = beltBaseRefs != null && beltBaseRefs.TryGetValue(failT, out var br) ? br.inT : null;
                var bOut = beltBaseRefs != null && beltBaseRefs.TryGetValue(failT, out var br2) ? br2.outT : null;
                Console.WriteLine($"    [window] T pos={pT} idx={failT.index} ({failT.localOffset_x:F2},{failT.localOffset_y:F2}) | baseIn={(bIn == null ? "null" : $"{Array.IndexOf(preChosen, bIn)}:({bIn.localOffset_x:F2},{bIn.localOffset_y:F2})")} | baseOut={(bOut == null ? "null" : $"{Array.IndexOf(preChosen, bOut)}:({bOut.localOffset_x:F2},{bOut.localOffset_y:F2})")}");
            }
            if (extraAnchors == null) extraAnchors = new List<int>();
            void AddAnchor(BlueprintBuilding n)
            {
                if (n == null || !IsBelt(n)) return;
                int p = Array.IndexOf(preChosen, n);
                if (p >= 0 && !extraAnchors.Contains(p)) extraAnchors.Add(p);
            }
            AddAnchor(failT);
            if (beltBaseRefs != null && beltBaseRefs.TryGetValue(failT, out var baseRefs))
            {
                AddAnchor(baseRefs.inT);
                AddAnchor(baseRefs.outT);
            }
            continue;
        }
        break;
        } // 004.15 门禁反馈重跑循环

        // 阶段追踪：分流器 #238 的 outputObj 在各阶段指向谁（Clone 会重编号 index，按数组位置对齐）
        int pos238 = -1;
        for (int i = 0; i < source.buildings.Length; i++)
        {
            var b = source.buildings[i];
            if (b != null && !IsBelt(b) && b.index == 238) { pos238 = i; break; }
        }
        Dump238("source(原件)", source.buildings, pos238);
        Dump238("preMerge(Clone后)", preChosen, pos238);
        Dump238("result(优化后)", chosen.buildings, pos238);
    }

    private static void Dump238(string tag, BlueprintBuilding[] arr, int pos)
    {
        if (pos < 0 || pos >= arr.Length) { Console.WriteLine($"[{tag}] pos {pos} 越界"); return; }
        var b = arr[pos];
        if (b == null || IsBelt(b)) { Console.WriteLine($"[{tag}] pos {pos} 非非带建筑"); return; }
        var o = b.outputObj;
        Console.WriteLine($"[{tag}] pos{pos} idx={b.index} item={b.itemId} out→{(o == null ? "<null>" : $"({o.localOffset_x:F2},{o.localOffset_y:F2}) item{o.itemId}")}");
    }

    private static (int over, double worst) OverNew(BlueprintBuilding[] buildings, BeltSpacingCalibration.Result model,
        HashSet<(long, long)> origSpans)
    {
        var set = new HashSet<BlueprintBuilding>(buildings);
        int over = 0; double worst = 0;
        foreach (var b in buildings)
        {
            if (b == null || !IsBelt(b) || b.outputObj == null || !IsBelt(b.outputObj) || !set.Contains(b.outputObj)) continue;
            double d = BeltSpacingCalibration.Distance(b, b.outputObj, model);
            if (double.IsNaN(d) || double.IsInfinity(d)) continue;
            bool pre = origSpans.Contains((SpanSideKey(b), SpanSideKey(b.outputObj)));
            if (d > BeltPathOptimizer.MaximumMeters) { if (!pre) { over++; if (d > worst) worst = d; } }
            else if (!pre && d > worst) worst = d;
        }
        return (over, worst);
    }
}
