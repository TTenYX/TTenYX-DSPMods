// 004.17 粘贴审计（游戏同式 · 全合法粘贴姿态）
//
// 用途：给定蓝图（原件或长带化产物），枚举游戏允许的全部粘贴姿态，按游戏自身公式逐点算出
// 每条带→带链节的真实 3D 长度，用游戏的判据 (out.lpos - lpos).sqrMagnitude > 5.3f
// （BuildTool_BlueprintPaste.cs:2087「距离太远」）逐条判定。这是准则①/⑧的地面真值：
//   · 姿态合法性直接调用游戏的 BlueprintUtils.GenerateAreaGratBoxByBPData（与粘贴预检
//     CheckBuildConditionsPrestage 同一函数）：跨回归线(x)/空间不足(y)/分段比例(w) 任一非 0 即非法；
//     对齐(z) 只取决于光标经度（SnapLongitude 会自动对齐），不影响距离，忽略 = 取超集（更保守）。
//   · 偏航：单区域蓝图可旋转 0/90/180/270（DeterminRotate 每次 +270），多区域只有 0/180。
//   · 光标纬度：粘贴光标经 planetAux.Snap 吸附到格点，枚举全部整数纬度格 c ∈ [-250, 250]。
//   · 位置：逐字复刻 BlueprintUtils.RefreshBuildPreview 1703-1778 与 1945-1967 行（float），
//     能调游戏原函数的全部调游戏原函数（GetDir / GetLongitudeRadPerGrid / TransitionWidthAndHeight /
//     RecalculateRotateStartAndEndRad / GetLongitudeLatitudeRad）。
// 判定口径（与插件 KeptOver 一致）：产物在某合法姿态越限的链节，若其两端格位跨度在原件中已存在
// （原件同跨度），游戏对原件同样拒绝 → 记"原件同跨度"；否则记"新增越限"——任何姿态出现新增越限即 FAIL。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

internal static class PasteAudit
{
    private static bool IsBelt(BlueprintBuilding n) => n != null && n.itemId >= 2001 && n.itemId <= 2009;
    internal const float RealRadius = 200f;
    internal const int Segment = 200;
    internal const float LimitSq = 5.3f;

    internal readonly struct Placement
    {
        internal readonly float Yaw;
        internal readonly int LatGrid;
        internal Placement(float yaw, int latGrid) { Yaw = yaw; LatGrid = latGrid; }
        public override string ToString() => $"yaw{Yaw:0} c{LatGrid}({LatGrid * 0.36:0.00}°)";
    }

    internal static float[] YawsOf(BlueprintData bp)
        => bp.areas.Length > 1 ? new[] { 0f, 180f } : new[] { 0f, 90f, 180f, 270f };

    internal static Vector3 CursorAt(int latGrid, float lngRad = 0.3f)
    {
        float latPer = BlueprintUtils.GetLatitudeRadPerGrid(Segment);
        return BlueprintUtils.GetDir(lngRad, latGrid * latPer) * (RealRadius + 0.2f);
    }

    /// <summary>游戏自身的姿态合法性（预检同函数）。忽略 z（经度对齐，SnapLongitude 自动满足）。</summary>
    internal static bool IsLegal(BlueprintData bp, float yaw, int latGrid)
    {
        var boxes = new BPGratBox[bp.areas.Length];
        var conds = new IntVector4[bp.areas.Length];
        var dots = new[] { CursorAt(latGrid) };
        BlueprintUtils.GenerateAreaGratBoxByBPData(bp, boxes, conds, dots, 1, yaw, Segment);
        for (int i = 0; i < conds.Length; i++)
        {
            if (conds[i].x > 0 || conds[i].y > 0 || conds[i].w > 0) return false;
        }
        return true;
    }

    internal static List<Placement> LegalPlacements(BlueprintData bp)
    {
        var list = new List<Placement>();
        foreach (float yaw in YawsOf(bp))
        {
            for (int c = -250; c <= 250; c++)
            {
                if (IsLegal(bp, yaw, c)) list.Add(new Placement(yaw, c));
            }
        }
        return list;
    }

    /// <summary>RefreshBuildPreview 1703-1778 的逐字复刻（_dotsCursor = 1）。</summary>
    internal static Vector4[] AreaFrames(BlueprintData bp, Vector3 cursor, float yaw)
    {
        var array = new Vector4[bp.areas.Length > 1 ? bp.areas.Length : 1];
        int primaryAreaIdx = bp.primaryAreaIdx;
        BlueprintArea blueprintArea = bp.areas[primaryAreaIdx];
        float latitudeRadPerGrid = BlueprintUtils.GetLatitudeRadPerGrid(Segment);
        int num = Mathf.FloorToInt(yaw / 89.9f);
        float num2 = (num == 1 || num == 2) ? -1f : 1f;
        float num3 = (num == 2 || num == 3) ? -1f : 1f;
        {
            float _longitude = 0f;
            float _latitude = 0f;
            Vector3 normalized = cursor.normalized;
            BlueprintUtils.GetLongitudeLatitudeRad(normalized, ref _longitude, ref _latitude);
            _latitude = Mathf.Clamp(_latitude, -MathF.PI / 2f, MathF.PI / 2f);
            float longitudeRadPerGrid = BlueprintUtils.GetLongitudeRadPerGrid(normalized, Segment);
            int curWidth = blueprintArea.width;
            int curHeight = blueprintArea.height;
            Vector4 vector = BlueprintUtils.RecalculateRotateStartAndEndRad(yaw, blueprintArea.width, blueprintArea.height, _longitude, _latitude, longitudeRadPerGrid, latitudeRadPerGrid, ref curWidth, ref curHeight);
            _longitude = vector.x;
            _latitude = vector.y;
            float num4 = vector.z;
            float w = vector.w;
            int num5 = primaryAreaIdx;
            if ((float)(curWidth - 1) * longitudeRadPerGrid > MathF.PI * 2f - longitudeRadPerGrid + 1E-05f)
            {
                num4 = _longitude + MathF.PI * 2f - longitudeRadPerGrid;
            }
            array[num5].x = _longitude;
            array[num5].y = _latitude;
            array[num5].z = Mathf.Repeat(num4 + MathF.PI, MathF.PI * 2f) - MathF.PI;
            array[num5].w = w;
        }
        for (int j = 0; j < bp.areas.Length; j++)
        {
            int num6 = -1;
            if (j == primaryAreaIdx) continue;
            if (j < primaryAreaIdx) num6 = primaryAreaIdx - j - 1;
            if (j > primaryAreaIdx) num6 = j;
            BlueprintArea blueprintArea2 = bp.areas[num6];
            float num7 = (num2 < 0f) ? array[blueprintArea2.parentIndex].z : array[blueprintArea2.parentIndex].x;
            float num8 = (num3 < 0f) ? array[blueprintArea2.parentIndex].w : array[blueprintArea2.parentIndex].y;
            float longitudeRadPerGrid2 = BlueprintUtils.GetLongitudeRadPerGrid(num8, Segment);
            float num9 = num8 + (float)blueprintArea2.anchorLocalOffsetY * latitudeRadPerGrid * num3;
            float longitudeRadPerGrid3 = BlueprintUtils.GetLongitudeRadPerGrid(num9, Segment);
            float startLng = num7 + (float)blueprintArea2.tropicAnchor * longitudeRadPerGrid2 * num2 - (float)blueprintArea2.anchorLocalOffsetX * longitudeRadPerGrid3 * num2;
            num9 = Mathf.Clamp(num9, -MathF.PI / 2f, MathF.PI / 2f);
            int curWidth2 = blueprintArea2.width;
            int curHeight2 = blueprintArea2.height;
            Vector4 vector2 = BlueprintUtils.RecalculateRotateStartAndEndRad(yaw, blueprintArea2.width, blueprintArea2.height, startLng, num9, longitudeRadPerGrid3, latitudeRadPerGrid, ref curWidth2, ref curHeight2);
            startLng = vector2.x;
            num9 = vector2.y;
            float num10 = vector2.z;
            float w2 = vector2.w;
            if ((float)(curWidth2 - 1) * longitudeRadPerGrid3 > MathF.PI * 2f - longitudeRadPerGrid3 + 1E-05f)
            {
                num10 = startLng + MathF.PI * 2f - longitudeRadPerGrid3;
            }
            array[num6].x = startLng;
            array[num6].y = num9;
            array[num6].z = Mathf.Repeat(num10 + MathF.PI, MathF.PI * 2f) - MathF.PI;
            array[num6].w = w2;
        }
        return array;
    }

    /// <summary>RefreshBuildPreview 1945-1967 的逐字复刻：单个建筑的 lpos。</summary>
    internal static Vector3 LPos(BlueprintBuilding b, Vector4[] frames, float yaw)
    {
        float latitudeRadPerGrid = BlueprintUtils.GetLatitudeRadPerGrid(Segment);
        int num = Mathf.FloorToInt(yaw / 89.9f);
        float num2 = (num == 1 || num == 2) ? -1f : 1f;
        float num3 = (num == 2 || num == 3) ? -1f : 1f;
        bool flag = num2 < 0f;
        bool flag2 = num3 < 0f;
        int ai = frames.Length == 1 ? 0 : b.areaIndex;
        Vector4 reference3 = frames[ai];
        float num32 = flag ? reference3.z : reference3.x;
        float num33 = flag2 ? reference3.w : reference3.y;
        float longitudeRadPerGrid4 = BlueprintUtils.GetLongitudeRadPerGrid(num33, Segment);
        Vector2 vector4 = BlueprintUtils.TransitionWidthAndHeight(yaw, b.localOffset_x, b.localOffset_y);
        float longitudeRad = num32 + vector4.x * longitudeRadPerGrid4 * num2;
        float num34 = num33 + vector4.y * latitudeRadPerGrid * num3;
        num34 = (Math.Abs(num34) > MathF.PI / 2f) ? (MathF.PI / 2f * (float)Math.Sign(num34)) : num34;
        Vector3 dir = BlueprintUtils.GetDir(longitudeRad, num34);
        return dir * (b.localOffset_z * 1.3333333f + 0.2f + RealRadius);
    }

    private static long SpanSideKey(BlueprintBuilding node)
    {
        long area = (long)(node.areaIndex & 0xFF) << 56;
        long x = ((long)Math.Round(node.localOffset_x * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
        long y = ((long)Math.Round(node.localOffset_y * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
        return area | (x << 28) | y;
    }

    internal static HashSet<(long, long)> SpanIndex(BlueprintBuilding[] buildings)
    {
        var spans = new HashSet<(long, long)>();
        foreach (var b in buildings)
        {
            if (!IsBelt(b) || !IsBelt(b.outputObj)) continue;
            spans.Add((SpanSideKey(b), SpanSideKey(b.outputObj)));
        }
        return spans;
    }

    internal static BlueprintData Load(string path)
    {
        var bp = new BlueprintData();
        if (bp.FromBase64String(File.ReadAllText(path).Trim()) != BlueprintDataIOError.OK || bp.buildings == null) return null;
        return bp;
    }

    internal sealed class Result
    {
        internal string Name;
        internal int Belts, Links, Placements, PlacementsWithNew, PlacementsWithAny, NewLinkPlacementPairs, KeptPairs;
        internal int LinksOverAnywhere, NewLinksOverAnywhere;
        internal double WorstNew, WorstAll;
        internal int OrigCleanPlacements, CleanButProductDirty;
        internal string FirstNew = "";
        internal bool Ok => PlacementsWithNew == 0;
    }

    /// <summary>审计一个产物（original 为 null 时把产物自己当原件——只看越限，不分新旧）。</summary>
    internal static Result Audit(string name, BlueprintData product, BlueprintData original, TextWriter detail = null)
    {
        var r = new Result { Name = name };
        var placements = LegalPlacements(original ?? product);
        r.Placements = placements.Count;
        var origSpans = original == null ? new HashSet<(long, long)>() : SpanIndex(original.buildings);
        var belts = product.buildings.Where(IsBelt).ToArray();
        r.Belts = belts.Length;
        var links = belts.Where(b => IsBelt(b.outputObj)).ToArray();
        r.Links = links.Length;
        var linkPre = links.Select(b => origSpans.Contains((SpanSideKey(b), SpanSideKey(b.outputObj)))).ToArray();
        var overAnywhere = new bool[links.Length];
        var origLinks = original == null ? null : original.buildings.Where(b => IsBelt(b) && IsBelt(b.outputObj)).ToArray();
        foreach (var p in placements)
        {
            var cursor = CursorAt(p.LatGrid);
            var framesP = AreaFrames(product, cursor, p.Yaw);
            int newHere = 0, anyHere = 0;
            for (int i = 0; i < links.Length; i++)
            {
                var a = links[i];
                var d2 = (LPos(a.outputObj, framesP, p.Yaw) - LPos(a, framesP, p.Yaw)).sqrMagnitude;
                double d = Math.Sqrt(d2);
                if (d > r.WorstAll) r.WorstAll = d;
                if (!linkPre[i] && d > r.WorstNew) r.WorstNew = d;
                if (d2 > LimitSq)
                {
                    anyHere++;
                    overAnywhere[i] = true;
                    if (linkPre[i]) r.KeptPairs++;
                    else
                    {
                        newHere++;
                        r.NewLinkPlacementPairs++;
                        if (r.FirstNew.Length == 0)
                        {
                            r.FirstNew = $"{p}: d={d:F4} ({a.localOffset_x:F3},{a.localOffset_y:F3},{a.localOffset_z:F2})→({a.outputObj.localOffset_x:F3},{a.outputObj.localOffset_y:F3},{a.outputObj.localOffset_z:F2}) 区域{a.areaIndex}";
                        }
                    }
                }
            }
            if (newHere > 0) r.PlacementsWithNew++;
            if (anyHere > 0) r.PlacementsWithAny++;
            if (origLinks != null)
            {
                var framesO = AreaFrames(original, cursor, p.Yaw);
                bool clean = true;
                foreach (var a in origLinks)
                {
                    if ((LPos(a.outputObj, framesO, p.Yaw) - LPos(a, framesO, p.Yaw)).sqrMagnitude > LimitSq) { clean = false; break; }
                }
                if (clean)
                {
                    r.OrigCleanPlacements++;
                    if (anyHere > 0) r.CleanButProductDirty++;
                }
            }
            detail?.WriteLine($"  {p}: 越限 {anyHere}（新增 {newHere}）");
        }
        for (int i = 0; i < links.Length; i++)
        {
            if (!overAnywhere[i]) continue;
            r.LinksOverAnywhere++;
            if (!linkPre[i]) r.NewLinksOverAnywhere++;
        }
        return r;
    }

    /// <summary>区域与合法姿态概览（--dims）。</summary>
    internal static void Dims(string path, TextWriter w)
    {
        var bp = Load(path);
        if (bp == null) { w.WriteLine($"{Path.GetFileName(path)}: 加载失败"); return; }
        w.WriteLine($"## {Path.GetFileNameWithoutExtension(path)}  建筑 {bp.buildings.Length}，带 {bp.buildings.Count(IsBelt)}，区域 {bp.areas.Length}，primary={bp.primaryAreaIdx}");
        foreach (var a in bp.areas)
        {
            var bs = bp.buildings.Where(b => b.areaIndex == a.index).ToArray();
            string range = bs.Length == 0 ? "无建筑" :
                $"x[{bs.Min(b => b.localOffset_x):F2},{bs.Max(b => b.localOffset_x):F2}] y[{bs.Min(b => b.localOffset_y):F2},{bs.Max(b => b.localOffset_y):F2}] z[{bs.Min(b => b.localOffset_z):F2},{bs.Max(b => b.localOffset_z):F2}]";
            w.WriteLine($"  区域{a.index}: parent={a.parentIndex} seg={a.areaSegments} w={a.width} h={a.height} tropicAnchor={a.tropicAnchor} anchorOff=({a.anchorLocalOffsetX},{a.anchorLocalOffsetY}) 建筑{bs.Length} {range}");
        }
        foreach (float yaw in YawsOf(bp))
        {
            var cs = new List<int>();
            for (int c = -250; c <= 250; c++) if (IsLegal(bp, yaw, c)) cs.Add(c);
            w.WriteLine($"  yaw{yaw:0}: 合法光标纬度格 {cs.Count} 个 {Intervals(cs)}");
        }
    }

    internal static string Intervals(List<int> cs)
    {
        if (cs.Count == 0) return "（无）";
        var sb = new StringBuilder();
        int s = cs[0], prev = cs[0];
        for (int i = 1; i <= cs.Count; i++)
        {
            if (i < cs.Count && cs[i] == prev + 1) { prev = cs[i]; continue; }
            sb.Append($"[{s},{prev}]({s * 0.36:0.##}°~{prev * 0.36:0.##}°) ");
            if (i < cs.Count) { s = cs[i]; prev = cs[i]; }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// --pasteaudit &lt;产物文件或目录&gt; [原件目录] [--out report.md]
    /// 产物目录下 A_tag_名.txt / B_tag_名.txt 自动按"名"配对原件目录中的 名.txt。
    /// </summary>
    internal static int Run(string[] args)
    {
        string target = args[1];
        string origDir = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null;
        string outFile = null;
        for (int i = 2; i < args.Length - 1; i++) if (args[i] == "--out") outFile = args[i + 1];
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { target };
        var rows = new List<string>
        {
            "| 产物 | 带 | 链节 | 合法姿态 | 原件全净姿态 | 含越限姿态 | 含新增越限姿态 | 新增越限(链×姿态) | 原件同跨度越限(链×姿态) | 最坏新链节(m) | 最坏(m) | 原件净而产物脏 | 判定 |",
            "|---|---|---|---|---|---|---|---|---|---|---|---|---|"
        };
        int fail = 0;
        foreach (var f in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(f);
            var product = Load(f);
            if (product == null) { rows.Add($"| {fileName} | 加载失败 |||||||||||| FAIL |"); fail++; continue; }
            BlueprintData original = null;
            if (origDir != null)
            {
                string name = fileName;
                if ((name.StartsWith("A_") || name.StartsWith("B_")) && name.IndexOf('_', 2) > 0) name = name.Substring(name.IndexOf('_', 2) + 1);
                string op = Path.Combine(origDir, name + ".txt");
                if (File.Exists(op)) original = Load(op);
            }
            var r = Audit(fileName, product, original);
            if (!r.Ok) fail++;
            Console.WriteLine($"{(r.Ok ? "PASS" : "FAIL")} {fileName}: 合法姿态 {r.Placements}，含越限 {r.PlacementsWithAny}，含新增越限 {r.PlacementsWithNew}，新增(链×姿态) {r.NewLinkPlacementPairs}，原件同跨度 {r.KeptPairs}，最坏新 {r.WorstNew:F4}，最坏 {r.WorstAll:F4}" +
                (original != null ? $"，原件全净姿态 {r.OrigCleanPlacements}（其中产物脏 {r.CleanButProductDirty}）" : "（无原件对照）") +
                (r.FirstNew.Length > 0 ? $"；首个新增越限 {r.FirstNew}" : ""));
            rows.Add($"| {fileName} | {r.Belts} | {r.Links} | {r.Placements} | {(original != null ? r.OrigCleanPlacements.ToString() : "—")} | {r.PlacementsWithAny} | {r.PlacementsWithNew} | {r.NewLinkPlacementPairs} | {r.KeptPairs} | {r.WorstNew:F4} | {r.WorstAll:F4} | {(original != null ? r.CleanButProductDirty.ToString() : "—")} | {(r.Ok ? "PASS" : "**FAIL**")} |");
        }
        Console.WriteLine(fail == 0 ? "PASTE AUDIT ALL PASS" : $"PASTE AUDIT HAS FAILURES ({fail})");
        if (outFile != null) File.WriteAllLines(outFile, rows, new UTF8Encoding(false));
        return fail == 0 ? 0 : 1;
    }
}
