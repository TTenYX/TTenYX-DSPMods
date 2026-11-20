// 004.17 结构锚点归因（--anchorwhy <蓝图目录或文件>）：复刻 BeltPathOptimizer 的结构锚点判据，
// 按原因分类计数（分叉/合流、input≠前驱、带等级不同、转折角度分档、元数据），只读诊断。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class AnchorWhy
{
    private static bool IsBelt(BlueprintBuilding n) => n != null && n.itemId >= 2001 && n.itemId <= 2009;

    internal static void Run(string[] args)
    {
        string target = args[1];
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };
        foreach (var f in files)
        {
            var bp = new BlueprintData();
            if (bp.FromBase64String(File.ReadAllText(f).Trim()) != BlueprintDataIOError.OK) continue;
            var b = bp.buildings;
            var belts = new HashSet<BlueprintBuilding>(b.Where(IsBelt));
            var next = belts.ToDictionary(x => x, x => x.outputObj);
            foreach (var x in b)
                if (belts.Contains(x) && x.inputObj != null && belts.Contains(x.inputObj) && next[x.inputObj] == null) next[x.inputObj] = x;
            var incoming = belts.ToDictionary(x => x, x => new List<BlueprintBuilding>());
            foreach (var x in b) if (belts.Contains(x) && next[x] != null && belts.Contains(next[x])) incoming[next[x]].Add(x);
            var why = new SortedDictionary<string, int>();
            void Tag(string t) { why.TryGetValue(t, out int c); why[t] = c + 1; }
            int anchors = 0;
            var angles = new List<double>();
            foreach (var x in b)
            {
                if (!belts.Contains(x)) continue;
                var n = next[x];
                if (incoming[x].Count != 1 || n == null || !belts.Contains(n))
                {
                    anchors++;
                    Tag(incoming[x].Count == 0 ? "链首(无前驱)" : incoming[x].Count > 1 ? "合流(多前驱)" : "链尾(无后继)");
                    continue;
                }
                var prev = incoming[x][0];
                bool any = false;
                if (x.inputObj != null && x.inputObj != prev) { Tag("input≠前驱"); any = true; }
                bool comp1 = prev.itemId == x.itemId && prev.modelIndex == x.modelIndex && prev.areaIndex == x.areaIndex;
                bool comp2 = x.itemId == n.itemId && x.modelIndex == n.modelIndex && x.areaIndex == n.areaIndex;
                if (!comp1 || !comp2) { Tag("带等级/区域不同"); any = true; }
                double ax = x.localOffset_x - prev.localOffset_x, ay = x.localOffset_y - prev.localOffset_y, az = x.localOffset_z - prev.localOffset_z;
                double bx = n.localOffset_x - x.localOffset_x, by = n.localOffset_y - x.localOffset_y, bz = n.localOffset_z - x.localOffset_z;
                double la = Math.Sqrt(ax * ax + ay * ay + az * az), lb = Math.Sqrt(bx * bx + by * by + bz * bz);
                double cos = la * lb > 1e-8 ? (ax * bx + ay * by + az * bz) / (la * lb) : -2;
                if (cos <= Math.Cos(Math.PI / 180.0))
                {
                    double ang = cos < -1.5 ? 999 : Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI;
                    angles.Add(ang);
                    bool zTurn = Math.Abs(az / Math.Max(la, 1e-9) - bz / Math.Max(lb, 1e-9)) > 0.02;
                    string bucket = ang > 900 ? "零长" : ang < 3 ? "1-3°" : ang < 10 ? "3-10°" : ang < 30 ? "10-30°" : ang < 60 ? "30-60°" : ang < 100 ? "60-100°" : ">100°";
                    Tag("转折 " + bucket + (zTurn ? "(含坡度变化)" : ""));
                    any = true;
                }
                if (x.filterId != 0 || x.recipeId != 0 || !string.IsNullOrEmpty(x.content) || (x.parameters != null && x.parameters.Length != 0)) { Tag("元数据(图标/标签/参数)"); any = true; }
                if (any) anchors++;
            }
            Console.WriteLine($"## {Path.GetFileNameWithoutExtension(f)}：带 {belts.Count}，结构锚点 {anchors}");
            foreach (var kv in why.OrderByDescending(k => k.Value)) Console.WriteLine($"    {kv.Key,-24} {kv.Value}");
        }
    }
}
