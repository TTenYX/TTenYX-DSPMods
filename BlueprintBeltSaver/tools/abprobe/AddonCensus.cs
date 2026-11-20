// 004.17 附加机连接普查（--addons <产物目录> <原件目录>）：喷涂机 2313 / 流速监测器 2030 / 炮塔 3001~3010
// 都是 addonType=Belt —— 粘贴建成后游戏按"连接位 1 m 内最近的带节点、且距连接线 < 0.3 m"重新接带
// （PlanetFactory.cs:6029-6072），蓝图不存引用。本工具按 F9/自测导出的连接位预制数据（addon-dump）
// 在格坐标里近似求连接点，统计原件与产物在每个连接点 1 m 窗口内的带节点，检查产物是否丢了原件
// 有的连接（= 附加机失效，准则③/④）。米→格按赤道尺度近似（经向在高纬会更宽，只作普查）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class AddonCensus
{
    private static bool IsBelt(BlueprintBuilding n) => n != null && n.itemId >= 2001 && n.itemId <= 2009;

    // 连接位（来自游戏内 addon-dump：addonAreaPoses[i].position，单位 m，建筑本地坐标 x=右 y=上 z=前）
    private static readonly Dictionary<int, (double x, double y, double z)[]> Ports = new Dictionary<int, (double, double, double)[]>
    {
        { 2313, new[] { (0.0, 0.0, 0.0), (0.0, 1.3333, -1.25) } },   // 喷涂机：货物口 / 增产剂口
        { 2030, new[] { (0.0, 0.0, 0.0) } },                          // 流速监测器
        { 3001, new[] { (0.0, 0.0, 0.0) } }, { 3003, new[] { (0.0, 0.0, 0.0) } }, { 3004, new[] { (0.0, 0.0, 0.0) } },
        { 3005, new[] { (0.0, 0.0, 0.0) } }, { 3006, new[] { (0.0, 0.0, 0.0) } }, { 3010, new[] { (0.0, 0.0, 0.0) } },
    };
    private const double MetresPerGrid = 1.2579; // 赤道尺度（200.2 m 半径 × 2π/1000）
    private const double MetresPerLevel = 1.3333333;

    private static (double x, double y, double z) PortGrid(BlueprintBuilding a, (double x, double y, double z) p)
    {
        double yaw = a.yaw * Math.PI / 180.0;
        // 本地 z=前（yaw 0 = 北 = +y 格向，yaw 90 = 东 = +x），本地 x=右（东向在 yaw 0 时）
        double fx = Math.Sin(yaw), fy = Math.Cos(yaw);
        double rx = Math.Cos(yaw), ry = -Math.Sin(yaw);
        double ex = p.x * rx + p.z * fx, ey = p.x * ry + p.z * fy;
        return (a.localOffset_x + ex / MetresPerGrid, a.localOffset_y + ey / MetresPerGrid, a.localOffset_z + p.y / MetresPerLevel);
    }

    private static List<(BlueprintBuilding n, double d)> Near(IEnumerable<BlueprintBuilding> belts, (double x, double y, double z) q, int area, double radiusM)
    {
        var list = new List<(BlueprintBuilding, double)>();
        foreach (var n in belts)
        {
            if (n.areaIndex != area) continue;
            double dx = (n.localOffset_x - q.x) * MetresPerGrid, dy = (n.localOffset_y - q.y) * MetresPerGrid, dz = (n.localOffset_z - q.z) * MetresPerLevel;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < radiusM) list.Add((n, d));
        }
        return list.OrderBy(t => t.Item2).ToList();
    }

    internal static void Run(string[] args)
    {
        string prodDir = args[1], origDir = args[2];
        int totalLost = 0, totalPorts = 0;
        foreach (var of in Directory.GetFiles(origDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(of);
            var orig = new BlueprintData();
            if (orig.FromBase64String(File.ReadAllText(of).Trim()) != BlueprintDataIOError.OK) continue;
            string pf = Path.Combine(prodDir, "A_lib_" + name + ".txt");
            if (!File.Exists(pf)) pf = Path.Combine(prodDir, "B_lib_" + name + ".txt");
            BlueprintData prod = null;
            if (File.Exists(pf)) { prod = new BlueprintData(); if (prod.FromBase64String(File.ReadAllText(pf).Trim()) != BlueprintDataIOError.OK) prod = null; }
            var ob = orig.buildings.Where(IsBelt).ToArray();
            var pb = prod?.buildings.Where(IsBelt).ToArray() ?? new BlueprintBuilding[0];
            var addons = orig.buildings.Where(a => Ports.ContainsKey(a.itemId)).ToArray();
            if (addons.Length == 0) { Console.WriteLine($"## {name}: 无附加机"); continue; }
            var byItem = addons.GroupBy(a => a.itemId).Select(g => $"{g.Key}×{g.Count()}");
            int ports = 0, origOk = 0, prodOk = 0, lost = 0, exact = 0, multi = 0;
            double worstOrig = 0;
            var lostSamples = new List<string>();
            foreach (var a in addons)
            {
                var portList = Ports[a.itemId];
                for (int i = 0; i < portList.Length; i++)
                {
                    ports++;
                    var q = PortGrid(a, portList[i]);
                    var on = Near(ob, q, a.areaIndex, 1.0);
                    if (on.Count == 0) continue;
                    origOk++;
                    if (on[0].d < 0.05) exact++;
                    if (on.Count > 1) multi++;
                    if (on[0].d > worstOrig) worstOrig = on[0].d;
                    if (prod == null) continue;
                    var pn = Near(pb, q, a.areaIndex, 1.0);
                    if (pn.Count > 0 && Math.Abs(pn[0].d - on[0].d) < 1e-3) prodOk++;
                    else
                    {
                        lost++;
                        if (lostSamples.Count < 4) lostSamples.Add($"item{a.itemId}@({a.localOffset_x:F2},{a.localOffset_y:F2},{a.localOffset_z:F1}) 口{i}：原件最近 {on[0].d:F3} m，产物最近 {(pn.Count > 0 ? pn[0].d.ToString("F3") : "无")}");
                    }
                }
            }
            Console.WriteLine($"## {name}: 附加机 {string.Join(" ", byItem)}；连接口 {ports}，原件 1 m 内有带 {origOk}（正压节点 {exact}，窗口内多节点 {multi}，原件最远 {worstOrig:F3} m）；产物保持原最近节点 {prodOk}，丢失/改变 {lost}");
            foreach (var s in lostSamples) Console.WriteLine("    " + s);
            totalLost += lost;
            totalPorts += origOk;
        }
        Console.WriteLine(totalLost == 0 ? $"ADDON CENSUS ALL KEPT（{totalPorts} 个有带连接口全部保持原最近节点）" : $"ADDON CENSUS HAS LOSSES ({totalLost})");
    }
}
