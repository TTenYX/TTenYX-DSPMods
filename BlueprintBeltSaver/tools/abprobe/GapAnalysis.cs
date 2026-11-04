// 004.17 省带缺口分析（--gap <蓝图目录或文件> [cap]）：按插件同口径（包络 meters + 插值模式）
// 跑一次 Optimize，输出锚点构成、逐链状态直方图、以及"已优化链"相对理论下限 ceil(L/cap)+1 的
// 缺口，用来判断剩余带子卡在哪一类锚点/约束上。只读诊断，不导出文件。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueprintBeltSaver;

internal static class GapAnalysis
{
    private static bool IsBelt(BlueprintBuilding n) => n != null && n.itemId >= 2001 && n.itemId <= 2009;

    internal static void Run(string[] args)
    {
        string target = args[1];
        double cap = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 2.301172;
        double capO = cap * 0.98;
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };
        BeltPathOptimizer.Verbose = true;
        // 实验旋钮（只影响本诊断进程）：BBS_SPRAY=喷涂窗口格数；BBS_SMOOTH_DEG=拉直阈值（度）
        if (double.TryParse(Environment.GetEnvironmentVariable("BBS_SPRAY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double spray))
            BeltPathOptimizer.SprayerWindowGrids = spray;
        if (double.TryParse(Environment.GetEnvironmentVariable("BBS_SMOOTH_DEG"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double smoothDeg))
            BeltPathOptimizer.SmoothCosThreshold = Math.Cos(smoothDeg * Math.PI / 180.0);
        Console.WriteLine($"[实验旋钮] 喷涂窗口 {BeltPathOptimizer.SprayerWindowGrids} 格，拉直阈值 {Math.Acos(BeltPathOptimizer.SmoothCosThreshold) * 180 / Math.PI:F1}°");
        int total = 0;
        var stdout = Console.Out;
        foreach (var f in files)
        {
            var bp = new BlueprintData();
            if (bp.FromBase64String(File.ReadAllText(f).Trim()) != BlueprintDataIOError.OK) continue;
            var work = bp.Clone();
            int before = work.buildings.Count(IsBelt);
            var env = CrossLatitudeSafety.Envelope(work.areas, work.primaryAreaIdx, work.buildings, IsBelt);
            var meters = CrossLatitudeSafety.Wrap(env, null, true);
            var cal = BeltSpacingCalibration.Resolve(work.areas, work.buildings, IsBelt, BeltSpacingCalibration.Observation.FromYaw(0.0, 0f));
            // 原件逐链（锚点间）统计需要优化前的 next 图
            Console.SetOut(TextWriter.Null);   // Optimize 的 Verbose 行太多，静默
            var res = BeltPathOptimizer.Optimize(work.buildings, IsBelt, _ => (float)capO, meters, out var rep, true, false, cal.MultiBand, true);
            Console.SetOut(stdout);
            int after = res.Count(IsBelt);
            var hist = new Dictionary<string, (int chains, int before, int after)>();
            double gapOpt = 0, lenOpt = 0;
            int interiorStuck = 0;
            foreach (var row in rep.Chains.Skip(1))
            {
                var p = row.Split(',');
                if (p.Length < 7) continue;
                int nb = int.Parse(p[2]), na = int.Parse(p[3]);
                double L = double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture);
                string st = p[6];
                hist.TryGetValue(st, out var h);
                hist[st] = (h.chains + 1, h.before + nb, h.after + na);
                if (st.StartsWith("optimized"))
                {
                    double opt = Math.Ceiling(L / capO - 1e-9) + 1;
                    if (na > opt) gapOpt += na - opt;
                    lenOpt += L;
                }
                else
                {
                    interiorStuck += Math.Max(0, nb - 2);
                }
            }
            Console.WriteLine($"## {Path.GetFileNameWithoutExtension(f)}：{before} → {after}（省 {before - after}）；{env}");
            Console.WriteLine($"  锚点：冲突{rep.AnchorConflicts} 机器绑定{rep.AnchorAttachments} 喷涂{rep.AnchorSprayers} 杆障碍{rep.AnchorObstacles} 结构/转折{rep.AnchorStructural} 站台{rep.AnchorStations} 环{rep.AnchorCycles} 超限{rep.AnchorPin}（合计固定点 {rep.Protected}）");
            Console.WriteLine($"  路径 {rep.Runs}，已优化 {rep.ReducedRuns}，无需减少 {rep.TooShort}，异常 {rep.Invalid}，碰撞盒命中 {rep.CollisionBoxHits}，格点回退 {rep.GridFallbacks}，原件对越限豁免 {rep.KeptOverSpans}");
            foreach (var kv in hist.OrderByDescending(k => k.Value.before))
            {
                Console.WriteLine($"    {kv.Key,-26} 链 {kv.Value.chains,5}  节点 {kv.Value.before,5} → {kv.Value.after,5}");
            }
            Console.WriteLine($"  已优化链相对理论下限 ceil(L/cap)+1 的缺口：{gapOpt:F0} 节点（总长 {lenOpt:F0} m）；未优化链内部节点 {interiorStuck}");
            total += before - after;
        }
        Console.WriteLine($"TOTAL SAVED {total}");
        BeltPathOptimizer.Verbose = false;
    }
}
