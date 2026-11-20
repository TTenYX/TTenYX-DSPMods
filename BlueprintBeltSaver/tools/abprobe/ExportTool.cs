// 蓝图A导出工具（游戏外长带化）：--export <蓝图目录> <输出目录> [纬度csv，默认 0.5,30,45,72]
// 口径与游戏内 TryOptimize 一致：Clone → 基线(线路/引用) → 格点主试+插值试验双模式采纳
// → CheckBuildingData → InserterBinder.Repair → 硬上限自检(KeptOver 口径) → 拓扑门禁(基线版)。
// 导出蓝图A（ToBase64String）并输出逐图判定表。库内场景（蓝图F2窗口省带）建模纬度=赤道。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlueprintBeltSaver;

internal static class ExportTool
{
    private static bool IsBelt(BlueprintBuilding n) => n.itemId >= 2001 && n.itemId <= 2009;
    // 004.17：插件配置复刻（默认与 TEST profile 一致：RebindSorterLinks=true、SorterForceRebind=true）。
    // BBS_REBIND=0 / BBS_FORCEREBIND=0 可切到插件出厂默认（两者 false）路径做对照。
    private static readonly bool Rebind = Environment.GetEnvironmentVariable("BBS_REBIND") != "0";
    private static readonly bool ForceRebind = Environment.GetEnvironmentVariable("BBS_FORCEREBIND") != "0";
    private const double PlanetRadius = 200.0;               // 典型星球半径（表面格单位）
    private const double GridsPerDegree = Math.PI * PlanetRadius / 180.0; // ≈3.4907 格/纬度
    private const double LatMargin = 2.0;                    // 铺设边距（度）

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

    private static (int over, double worst) OverNew(BlueprintBuilding[] buildings, Func<BlueprintBuilding, BlueprintBuilding, double> meters,
        HashSet<(long, long)> origSpans)
    {
        // 004.17：与插件 VerifyHardLimit 同口径——距离用 meters（全合法粘贴姿态包络）。
        var set = new HashSet<BlueprintBuilding>(buildings);
        int over = 0; double worst = 0;
        foreach (var b in buildings)
        {
            if (b == null || !IsBelt(b) || b.outputObj == null || !IsBelt(b.outputObj) || !set.Contains(b.outputObj)) continue;
            double d = meters(b, b.outputObj);
            if (double.IsNaN(d) || double.IsInfinity(d)) continue;
            bool pre = origSpans.Contains((SpanSideKey(b), SpanSideKey(b.outputObj)));
            if (d > BeltPathOptimizer.MaximumMeters) { if (!pre) { over++; if (d > worst) worst = d; } }
            else if (!pre && d > worst) worst = d;
        }
        return (over, worst);
    }

    internal static void Run(string[] args)
    {
        string bpDir = args[1];
        string outDir = args[2];
        double[] lats = (args.Length > 3 ? args[3] : "0.5,30,45,72").Split(',').Select(double.Parse).ToArray();
        Directory.CreateDirectory(outDir);

        var rows = new List<string>();
        rows.Add("| 蓝图 | 纬度° | 带数 | 导出 | 采纳模式 | 省 | OverNew(最坏m) | 闸门姿态/越限新 | KeptOver | 拓扑 | binder重绑 | 判定 | 文件 |");
        rows.Add("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var file in Directory.GetFiles(bpDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string text = File.ReadAllText(file);
            var source = new BlueprintData();
            if (source.FromBase64String(text) != BlueprintDataIOError.OK || source.buildings == null)
            {
                rows.Add($"| {name} | — | — | — | — | — | — | — | — | — | — | FAIL(加载) | — |");
                continue;
            }

            // —— 蓝图 footprint（判定可放置纬度带）：逐区域外包盒，取最大 y 跨度 ——
            double dy = 0, dx = 0;
            foreach (var grp in source.buildings.Where(b => b != null).GroupBy(b => b.areaIndex))
            {
                double mnx = grp.Min(b => b.localOffset_x), mxx = grp.Max(b => b.localOffset_x);
                double mny = grp.Min(b => b.localOffset_y), mxy = grp.Max(b => b.localOffset_y);
                dy = Math.Max(dy, mxy - mny); dx = Math.Max(dx, mxx - mnx);
            }
            double dLat = dy / GridsPerDegree;
            double halfBand = 90.0 - dLat / 2.0 - LatMargin;

            int before = source.buildings.Count(IsBelt);
            var origSpans = BuildSpanIndex(source.buildings);

            // 库内省带（F2 蓝图窗口，赤道建模）+ 各粘贴纬度重省
            foreach (double lat in new[] { -1.0 }.Concat(lats))
            {
                if (lat > 0 && lat > halfBand) continue; // 蓝图放不进这个纬度
                string tag = lat < 0 ? "lib" : $"lat{lat.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
                double latDeg = lat < 0 ? 0.0 : lat;
                string outName = $"A_{tag}_{name}.txt";
                try
                {
                    // 004.15 门禁反馈重跑（与插件 TryOptimize 同步）：跨线/丢失的附件目标窗口按
                    // source 数组位置作为 extraAnchorPositions 传入重跑（≤4 轮），锚点即链边界。
                    List<int> extraAnchors = null;
                    BlueprintData chosen = null;
                    BlueprintBuilding[] preChosen = null;
                    Dictionary<BlueprintBuilding, int> preLinesC = null;
                    Dictionary<int, (BlueprintBuilding inT, BlueprintBuilding outT, int inLine, int outLine)> preRefsC = null;
                    Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)> beltBaseRefs = null;
                    InserterBinder.Report binder = null;
                    (int over, double worst) fin = (0, 0.0);
                    PasteEnvelope.GateResult gate = default;
                    (bool ok, string reason, int linesB, int linesA, int failPos, bool failInput) topo = default;
                    string mode = "";
                    int after = 0;
                    PasteEnvelope envelope = null;
                    for (int attempt = 0; ; attempt++)
                    {
                    var work = source.Clone();
                    var preLinesW = TopoProbe.LinesOf(work.buildings);
                    var preRefsW = TopoProbe.RefLines(work.buildings, preLinesW);

                    // 004.17：BandMargin 退役（包络无采样缝隙），余量只剩 SafetyMargin 0.98（与插件同步）。
                    double capO = 2.301172 * 0.98;
                    var obs = BeltSpacingCalibration.Observation.FromYaw(latDeg * Math.PI / 180.0, 0f);
                    var calibration = BeltSpacingCalibration.Resolve(work.areas, work.buildings, IsBelt, obs);
                    // 004.17 跨纬度安全 = 全合法粘贴姿态包络（与插件默认配置同步，首轮 Clone 上建一次）。
                    if (envelope == null) envelope = CrossLatitudeSafety.Envelope(work.areas, work.primaryAreaIdx, work.buildings, IsBelt);
                    Func<BlueprintBuilding, BlueprintBuilding, double> meters = CrossLatitudeSafety.Wrap(envelope, obs, true);
                    bool pinOverLimit = calibration.MultiBand;

                    // 插件 trial 原文用 CreateNew(快照)，但 CreateNew 离线不可用（SecurityException）。
                    // 语义对齐说明：CreateNew 与 Clone 的 buildings 内容解析同式（ContentFromBase64），
                    // 差异仅在头部字段；离线以 Clone 等价替代（游戏内差异由 A/B 对比检出，见判定报告）。
                    string snapshot = work.ToBase64String();
                    var preGrid = work.buildings;
                    var gB = BeltPathOptimizer.Optimize(work.buildings, IsBelt, _ => (float)capO, meters, out var repG, Rebind, true, pinOverLimit, Rebind && ForceRebind, extraAnchors);
                    work.buildings = gB;
                    var trial = source.Clone();
                    var preLinesT = TopoProbe.LinesOf(trial.buildings);
                    var preRefsT = TopoProbe.RefLines(trial.buildings, preLinesT);
                    // 004.15：带节点基线引用图快照（重跑窗口需要 T 的基线邻居，现读引用已被改写）
                    beltBaseRefs = new Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)>();
                    foreach (var n in trial.buildings)
                    {
                        if (n == null || !IsBelt(n)) continue;
                        beltBaseRefs[n] = (n.inputObj != null && IsBelt(n.inputObj) ? n.inputObj : null,
                                           n.outputObj != null && IsBelt(n.outputObj) ? n.outputObj : null);
                    }
                    var preTrial = trial.buildings;
                    var rB = BeltPathOptimizer.Optimize(trial.buildings, IsBelt, _ => (float)capO, meters, out var repR, Rebind, false, pinOverLimit, Rebind && ForceRebind, extraAnchors);
                    trial.buildings = rB;

                    int gAfter = work.buildings.Count(IsBelt), rAfter = trial.buildings.Count(IsBelt);
                    var gOver = OverNew(work.buildings, meters, origSpans);
                    var rOver = OverNew(trial.buildings, meters, origSpans);
                    bool adoptR = rOver.over == 0 && rAfter < before && (gOver.over > 0 || rAfter < gAfter);
                    chosen = adoptR ? trial : work;
                    preChosen = adoptR ? preTrial : preGrid;
                    preLinesC = adoptR ? preLinesT : preLinesW;
                    preRefsC = adoptR ? preRefsT : preRefsW;
                    after = chosen.buildings.Count(IsBelt);
                    mode = adoptR ? "插值(采纳)" : "格点";

                    chosen.CheckBuildingData();
                    var resultLinesForBinder = TopoProbe.LinesOf(chosen.buildings);
                    var r2oForBinder = new Dictionary<int, int>();
                    foreach (var kv in resultLinesForBinder)
                    {
                        if (!preLinesC.TryGetValue(kv.Key, out int o)) continue;
                        if (r2oForBinder.TryGetValue(kv.Value, out var o0) && o0 != o) continue;
                        r2oForBinder[kv.Value] = o;
                    }
                    int LineOfForBinder(BlueprintBuilding n)
                        => n == null ? -1
                         : preLinesC.TryGetValue(n, out int pl) ? pl
                         : resultLinesForBinder.TryGetValue(n, out int rl) && r2oForBinder.TryGetValue(rl, out int bl) ? bl
                         : -1;
                    var ownerBaseTargets = new Dictionary<BlueprintBuilding, (BlueprintBuilding inT, BlueprintBuilding outT)>();
                    for (int i = 0; i < preChosen.Length; i++)
                    {
                        if (preChosen[i] != null && !IsBelt(preChosen[i]) && preRefsC.TryGetValue(i, out var prB))
                            ownerBaseTargets[preChosen[i]] = (prB.inT, prB.outT);
                    }
                    binder = InserterBinder.Repair(chosen.buildings, IsBelt, meters, LineOfForBinder,
                        (o, isInput, cand) => ownerBaseTargets.TryGetValue(o, out var t) &&
                            (isInput ? ReferenceEquals(t.inT, cand) : ReferenceEquals(t.outT, cand)));
                    fin = OverNew(chosen.buildings, meters, origSpans);
                    // 004.17 精确闸门（与插件同步）：保护集全部粘贴姿态逐个展开、游戏 float 同式判定。
                    gate = envelope.Gate(chosen.buildings, IsBelt, (a, b) => origSpans.Contains((SpanSideKey(a), SpanSideKey(b))));
                    topo = TopoProbe.VerifyTopologyBase(preLinesC, preRefsC, preChosen, chosen.buildings);
                    if (!topo.ok && topo.failPos >= 0 && attempt < 3 && preRefsC.TryGetValue(topo.failPos, out var failRef))
                    {
                        // 门禁反馈重跑：锚定基线目标窗口（基线目标本体+前后邻居；preChosen 的引用
                        // 已被就地改写，必须用 RefLines 快照里的目标）。
                        var failT = topo.failInput ? failRef.inT : failRef.outT;
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
                    }
                    bool ok = fin.over == 0 && gate.Over == 0 && topo.ok;
                    string outFile = "—";
                    if (ok)
                    {
                        string outText = chosen.ToBase64String();
                        var verify = new BlueprintData();
                        if (verify.FromBase64String(outText) != BlueprintDataIOError.OK || verify.buildings.Count(IsBelt) != after)
                        {
                            ok = false;
                        }
                        else
                        {
                            File.WriteAllText(Path.Combine(outDir, outName), outText, new UTF8Encoding(false));
                            outFile = outName;
                        }
                    }
                    rows.Add($"| {name} | {(lat < 0 ? "库(赤道)" : lat.ToString("0.#"))} | {before} | {(ok ? "是" : "否")} | {mode} | {before - after} | {fin.over}({fin.worst:F3}) | {gate.Frames}/{gate.Over}({gate.Worst:F3}) | {topo.ok}拓扑{topo.linesB} | {binder} | {(ok ? "PASS" : "FAIL")} | {outFile} |");
                }
                catch (Exception ex)
                {
                    rows.Add($"| {name} | {(lat < 0 ? "库(赤道)" : lat.ToString("0.#"))} | {before} | — | — | — | — | — | — | — | — | FAIL({ex.GetType().Name}) | — |");
                }
            }

            rows.Add($"| {name} footprint | x={dx:0.#}格 y={dy:0.#}格 ≈{dLat:0.0}° | 可放置中心纬度 |‖{Math.Max(0, halfBand):0.#}°| 以内 | | | | | | | | |");
        }

        File.WriteAllLines(Path.Combine(outDir, "判定报告.md"), rows, new UTF8Encoding(false));
        Console.WriteLine($"export done → {outDir}");
    }
}
