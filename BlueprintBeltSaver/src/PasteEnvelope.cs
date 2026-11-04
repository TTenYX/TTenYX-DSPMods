using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// 004.17 全合法粘贴姿态包络：准则⑧（任意纬度粘贴都不断链）的精确实现，取代 004.16 的
    /// "锚点 0~80° 每 1° 采样 + BandMargin 0.95"。
    ///
    /// ── 004.16 采样模型的两个实证缺陷（游戏同式审计 tools/abprobe --pasteaudit 查出）──────────
    /// ① 经度格宽按"节点纬度"查表。游戏 RefreshBuildPreview（BlueprintUtils.cs:1948）用的是**区域
    ///    参考纬度**：<c>GetLongitudeRadPerGrid(num33)</c>，num33 = 区域锚点纬度，同一区域所有节点共用
    ///    一个格宽。节点落在区域格框外（6k太阳帆 区域0 框高 3 行、节点却铺满 131 行）时两者差 1.67 倍：
    ///    004.16 的 6k 产物在它唯一的两个合法姿态上有 206 条新增越限（最长 2.81 m）。
    /// ② 只采样偏航 0°。单区域蓝图可旋转 90°/270°（DeterminRotate 每次 +270°），换轴后 y 向链节
    ///    变成经向、吃到高纬带的格宽：004.16 的 80环带 / 长带测试图01 旋转后在 -63°~-64° 越限。
    /// 同时它又**过度保守**：锚点采到 80°、节点再往北铺——这些姿态游戏根本不允许（区域不得跨
    /// 回归线 BlueprintAreaCrossTropic、空间不足 BlueprintAreaNotEnoughSpace），却拉高了经向最坏值
    /// （最坏取到 86.58° 带的 1.876 m/格），白白砍掉大量省带。
    ///
    /// ── 本模型 ─────────────────────────────────────────────────────────────────────────
    /// 1. 合法姿态 = 游戏自己的判定：对每个偏航（单区域 0/90/180/270，多区域 0/180）与每个整数
    ///    光标纬度格 c ∈ [-250, 250]（粘贴光标经 planetAux.Snap 吸附到格点），调用
    ///    <c>BlueprintUtils.GenerateAreaGratBoxByBPData</c>（粘贴预检 CheckBuildConditionsPrestage 同一
    ///    函数），x/y/w 任一非 0（跨回归线 / 空间不足 / 分段比例）即非法。z（经度对齐）只取决于光标
    ///    经度、SnapLongitude 会自动对齐且不影响距离，忽略 = 取超集（更保守）。
    /// 2. 保护集 = 原件（合并前）带-带链节在该姿态下**全部不越限**的合法姿态——"原件能干净粘贴的
    ///    地方，产物也必须能"；原件处处都有越限时退回全部合法姿态（最严）。
    /// 3. 每个姿态的区域参考系（参考纬度/经度、经度格宽、符号、换轴）逐字复刻 RefreshBuildPreview
    ///    1703-1778 行（float，能调游戏原函数的全部调原函数）。
    /// 4. 距离 = 保护集内最坏值。同一偏航下连续合法、各区域格宽不变的一段 c 构成一个"组"：组内
    ///    改变 c 只是全体节点纬度平移、经度差不变，弦长² = r_a²+r_b²−2r_ar_b[cosΔφ(1+cosΔλ)/2
    ///    − cos2m(1−cosΔλ)/2] 只随中点纬度 m 的 cos2m 单调增 → 组内最坏值在"链节中点最贴近赤道"
    ///    的 c 处取得（连续松弛 = 上界）。于是每条链节只需每组算一次（≤ 约 30 次），比 004.16 的
    ///    81 个模型更快，且没有采样缝隙 → 不再需要 BandMargin。
    /// 5. 交付前另有独立的精确闸门 <see cref="Gate"/>：保护集全部姿态逐个展开，按游戏 float 公式
    ///    算 lpos 并用 sqrMagnitude 判定（不走第 4 步的解析化简），任何新造越限即放弃本次省带。
    /// </summary>
    internal sealed class PasteEnvelope
    {
        internal const int Segment = 200;
        internal const float RealRadius = 200f;

        /// <summary>游戏 GetLatitudeRadPerGrid(200)（float）。</summary>
        private static readonly float LatRadF = MathF.PI * 2f / (Segment * 5);

        /// <summary>一个合法粘贴姿态下各区域的参考系（RefreshBuildPreview 同式）。</summary>
        internal sealed class Frame
        {
            internal float Yaw;
            internal int Quadrant;
            internal int LatGrid;
            internal bool Swap;
            internal float LatSign, LonSign;
            internal float[] RefLat;      // num33
            internal float[] RefLon;      // num32
            internal float[] LonPerGrid;  // GetLongitudeRadPerGrid(num33)
            public override string ToString() => $"yaw{Yaw:0} c{LatGrid}({LatGrid * 0.36:0.00}°)";
        }

        /// <summary>同偏航、连续合法、各区域格宽不变的一段光标纬度（解析最坏值的单位）。</summary>
        private sealed class Group
        {
            internal Frame Lo;
            internal double SpanRad;
        }

        private readonly List<Frame> protectedFrames = new List<Frame>();
        private readonly List<Group> groups = new List<Group>();
        private int areaCount = 1;

        /// <summary>游戏判定合法的姿态总数。</summary>
        internal int LegalCount { get; private set; }
        /// <summary>原件全部带-带链节不越限的合法姿态数。</summary>
        internal int CleanCount { get; private set; }
        /// <summary>true = 保护集取原件全净姿态；false = 原件处处有越限（或无链节），保护集 = 全部合法姿态。</summary>
        internal bool CleanSubset { get; private set; }
        internal int GroupCount => groups.Count;
        internal int ProtectedCount => protectedFrames.Count;
        internal IReadOnlyList<Frame> ProtectedFrames => protectedFrames;
        /// <summary>保护集为空（找不到任何合法姿态）：距离恒为 +∞，不做任何合并。</summary>
        internal bool Empty => groups.Count == 0;

        public override string ToString()
            => Empty
                ? "★无合法粘贴姿态（区域数据异常或非标准星球），不做省带"
                : $"合法粘贴姿态 {LegalCount} 个（偏航×光标纬度），" +
                  (!filterApplied ? "保护集 = 全部合法姿态（审计口径）"
                   : CleanSubset ? $"原件全净 {CleanCount} 个作为保护集"
                   : "原件无全净姿态，按全部合法姿态保护") +
                  $"，解析分组 {groups.Count} 个";

        private bool filterApplied;

        // ------------------------------------------------------------------ 构建 ------------------

        /// <summary>
        /// 按合并前的蓝图建立包络。<paramref name="primaryAreaIdx"/> &lt; 0 时取根区域
        /// （parentIndex &lt; 0；游戏的区域链以主区域为根向两侧展开）。
        /// <paramref name="cleanFilter"/> = false 时保护集取全部合法姿态（审计用：不让被审蓝图
        /// 自己的越限姿态把自己豁免掉）。
        /// </summary>
        internal static PasteEnvelope Build(BlueprintArea[] areas, int primaryAreaIdx, IList<BlueprintBuilding> source,
            Func<BlueprintBuilding, bool> isBelt, bool cleanFilter = true)
        {
            var env = new PasteEnvelope();
            if (areas == null || areas.Length == 0)
            {
                return env;
            }
            env.areaCount = areas.Length;
            primaryAreaIdx = ResolvePrimary(areas, primaryAreaIdx);
            var bp = new BlueprintData();
            bp.areas = areas;
            bp.primaryAreaIdx = primaryAreaIdx;

            var legal = new List<Frame>();
            var boxes = new BPGratBox[areas.Length];
            var conds = new IntVector4[areas.Length];
            var dots = new Vector3[1];
            float[] yaws = areas.Length > 1 ? new[] { 0f, 180f } : new[] { 0f, 90f, 180f, 270f };
            foreach (float yaw in yaws)
            {
                for (int c = -250; c <= 250; c++)
                {
                    dots[0] = CursorAt(c);
                    try
                    {
                        BlueprintUtils.GenerateAreaGratBoxByBPData(bp, boxes, conds, dots, 1, yaw, Segment);
                    }
                    catch
                    {
                        continue;
                    }
                    bool ok = true;
                    for (int i = 0; i < conds.Length; i++)
                    {
                        if (conds[i].x > 0 || conds[i].y > 0 || conds[i].w > 0)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                    {
                        legal.Add(MakeFrame(bp, dots[0], yaw, c));
                    }
                }
            }
            env.LegalCount = legal.Count;

            // 保护集：原件全部带-带链节在该姿态都不越限（游戏 float 同式判定）。
            var links = new List<(BlueprintBuilding a, BlueprintBuilding b)>();
            if (source != null)
            {
                foreach (var n in source)
                {
                    if (n != null && isBelt(n) && n.outputObj != null && isBelt(n.outputObj))
                    {
                        links.Add((n, n.outputObj));
                    }
                }
            }
            var clean = new List<Frame>();
            if (cleanFilter && links.Count > 0)
            {
                float limitSq = (float)(BeltPathOptimizer.MaximumMeters * BeltPathOptimizer.MaximumMeters);
                foreach (var f in legal)
                {
                    bool fits = true;
                    foreach (var link in links)
                    {
                        if ((LPos(link.b, f) - LPos(link.a, f)).sqrMagnitude > limitSq)
                        {
                            fits = false;
                            break;
                        }
                    }
                    if (fits)
                    {
                        clean.Add(f);
                    }
                }
            }
            env.CleanCount = clean.Count;
            env.filterApplied = cleanFilter;
            env.CleanSubset = clean.Count > 0;
            env.protectedFrames.AddRange(env.CleanSubset ? clean : legal);

            // 解析分组：同偏航、c 连续、各区域格宽逐一相同。
            Group current = null;
            Frame previous = null;
            foreach (var f in env.protectedFrames)
            {
                bool extend = current != null && previous != null && previous.Yaw == f.Yaw
                    && f.LatGrid == previous.LatGrid + 1 && SameWidths(previous, f);
                if (extend)
                {
                    current.SpanRad = (f.LatGrid - current.Lo.LatGrid) * (double)LatRadF;
                }
                else
                {
                    current = new Group { Lo = f, SpanRad = 0.0 };
                    env.groups.Add(current);
                }
                previous = f;
            }
            return env;
        }

        private static bool SameWidths(Frame a, Frame b)
        {
            if (a.LonPerGrid.Length != b.LonPerGrid.Length)
            {
                return false;
            }
            for (int i = 0; i < a.LonPerGrid.Length; i++)
            {
                if (a.LonPerGrid[i] != b.LonPerGrid[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>粘贴光标：吸附到纬度格 c 的格点（经度任取，距离与之无关）。</summary>
        internal static Vector3 CursorAt(int latGrid)
            => BlueprintUtils.GetDir(0.3f, latGrid * LatRadF) * (RealRadius + 0.2f);

        /// <summary>主区域：调用方给定且有效就用它，否则取根区域（parentIndex &lt; 0）。</summary>
        private static int ResolvePrimary(BlueprintArea[] areas, int primaryAreaIdx)
        {
            if (primaryAreaIdx >= 0 && primaryAreaIdx < areas.Length)
            {
                return primaryAreaIdx;
            }
            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i] != null && areas[i].parentIndex < 0)
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>
        /// 给定粘贴观测（光标纬度 + 偏航象限）的单姿态参考系，<paramref name="legal"/> 报告游戏
        /// 预检是否允许该姿态（跨回归线/空间不足/分段比例）。粘贴期增量复核用：按**真实**粘贴
        /// 姿态逐点同式复核现有结果。
        /// </summary>
        internal static Frame FrameFor(BlueprintArea[] areas, int primaryAreaIdx,
            BeltSpacingCalibration.Observation observation, out bool legal)
        {
            legal = false;
            if (areas == null || areas.Length == 0)
            {
                return null;
            }
            var bp = new BlueprintData();
            bp.areas = areas;
            bp.primaryAreaIdx = ResolvePrimary(areas, primaryAreaIdx);
            int c = (int)Math.Round(observation.AnchorLatitudeRad / LatRadF, MidpointRounding.AwayFromZero);
            c = Math.Max(-250, Math.Min(250, c));
            float yaw = observation.Quadrant * 90f;
            var dots = new[] { CursorAt(c) };
            var conds = new IntVector4[areas.Length];
            try
            {
                BlueprintUtils.GenerateAreaGratBoxByBPData(bp, new BPGratBox[areas.Length], conds, dots, 1, yaw, Segment);
                legal = true;
                for (int i = 0; i < conds.Length; i++)
                {
                    if (conds[i].x > 0 || conds[i].y > 0 || conds[i].w > 0)
                    {
                        legal = false;
                        break;
                    }
                }
                return MakeFrame(bp, dots[0], yaw, c);
            }
            catch
            {
                legal = false;
                return null;
            }
        }

        /// <summary>RefreshBuildPreview 1703-1778 行逐字复刻（_dotsCursor = 1），并拆成逐区域参考系。</summary>
        private static Frame MakeFrame(BlueprintData bp, Vector3 cursor, float yaw, int latGrid)
        {
            int count = bp.areas.Length;
            var array = new Vector4[count > 1 ? count : 1];
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
                int num5 = count > 1 ? primaryAreaIdx : 0;
                if ((float)(curWidth - 1) * longitudeRadPerGrid > MathF.PI * 2f - longitudeRadPerGrid + 1E-05f)
                {
                    num4 = _longitude + MathF.PI * 2f - longitudeRadPerGrid;
                }
                array[num5].x = _longitude;
                array[num5].y = _latitude;
                array[num5].z = Mathf.Repeat(num4 + MathF.PI, MathF.PI * 2f) - MathF.PI;
                array[num5].w = w;
            }
            for (int j = 0; j < count; j++)
            {
                if (j == primaryAreaIdx)
                {
                    continue;
                }
                int num6 = j < primaryAreaIdx ? primaryAreaIdx - j - 1 : j;
                BlueprintArea blueprintArea2 = bp.areas[num6];
                int parent = blueprintArea2.parentIndex;
                if (parent < 0 || parent >= count)
                {
                    continue;
                }
                float num7 = (num2 < 0f) ? array[parent].z : array[parent].x;
                float num8 = (num3 < 0f) ? array[parent].w : array[parent].y;
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
            bool flag = num2 < 0f;
            bool flag2 = num3 < 0f;
            var frame = new Frame
            {
                Yaw = yaw,
                Quadrant = num,
                LatGrid = latGrid,
                Swap = num == 1 || num == 3,
                LatSign = num3,
                LonSign = num2,
                RefLat = new float[array.Length],
                RefLon = new float[array.Length],
                LonPerGrid = new float[array.Length]
            };
            for (int i = 0; i < array.Length; i++)
            {
                frame.RefLon[i] = flag ? array[i].z : array[i].x;
                frame.RefLat[i] = flag2 ? array[i].w : array[i].y;
                frame.LonPerGrid[i] = BlueprintUtils.GetLongitudeRadPerGrid(frame.RefLat[i], Segment);
            }
            return frame;
        }

        private static int AreaSlot(Frame f, BlueprintBuilding b)
        {
            int slots = f.RefLat.Length;
            if (slots == 1)
            {
                return 0;
            }
            int a = b.areaIndex;
            return a >= 0 && a < slots ? a : 0;
        }

        /// <summary>RefreshBuildPreview 1945-1967 行逐字复刻（float）：建筑 lpos。</summary>
        internal static Vector3 LPos(BlueprintBuilding b, Frame f)
        {
            int ai = AreaSlot(f, b);
            float num32 = f.RefLon[ai];
            float num33 = f.RefLat[ai];
            float longitudeRadPerGrid4 = f.LonPerGrid[ai];
            Vector2 vector4 = BlueprintUtils.TransitionWidthAndHeight(f.Yaw, b.localOffset_x, b.localOffset_y);
            float longitudeRad = num32 + vector4.x * longitudeRadPerGrid4 * f.LonSign;
            float num34 = num33 + vector4.y * LatRadF * f.LatSign;
            num34 = (Math.Abs(num34) > MathF.PI / 2f) ? (MathF.PI / 2f * (float)Math.Sign(num34)) : num34;
            Vector3 dir = BlueprintUtils.GetDir(longitudeRad, num34);
            return dir * (b.localOffset_z * 1.3333333f + 0.2f + RealRadius);
        }

        // ------------------------------------------------------------------ 距离 ------------------

        /// <summary>节点在组基准姿态（组内最低 c）下的纬度（弧度，未夹极）。</summary>
        private static double LatAt(Frame f, BlueprintBuilding b)
        {
            int ai = AreaSlot(f, b);
            double yt = f.Swap ? b.localOffset_x : b.localOffset_y;
            return f.RefLat[ai] + yt * LatRadF * f.LatSign;
        }

        /// <summary>双精度弦长：组基准姿态上全体纬度再平移 shift（组内换 c 的效果）。</summary>
        private static double Chord(Frame f, BlueprintBuilding a, BlueprintBuilding b, double shift)
        {
            Pos(f, a, shift, out double ax, out double ay, out double az);
            Pos(f, b, shift, out double bx, out double by, out double bz);
            double dx = ax - bx, dy = ay - by, dz = az - bz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void Pos(Frame f, BlueprintBuilding b, double shift, out double x, out double y, out double z)
        {
            int ai = AreaSlot(f, b);
            double xt = f.Swap ? b.localOffset_y : b.localOffset_x;
            double yt = f.Swap ? b.localOffset_x : b.localOffset_y;
            double lon = f.RefLon[ai] + xt * f.LonPerGrid[ai] * f.LonSign;
            double lat = f.RefLat[ai] + shift + yt * LatRadF * f.LatSign;
            if (lat > Math.PI / 2) lat = Math.PI / 2;
            if (lat < -Math.PI / 2) lat = -Math.PI / 2;
            double r = b.localOffset_z * 1.3333333 + 0.2 + RealRadius;
            double cl = Math.Cos(lat);
            x = cl * Math.Sin(lon) * r;
            y = Math.Sin(lat) * r;
            z = -cl * Math.Cos(lon) * r;
        }

        /// <summary>
        /// 保护集内的最坏距离（米）。每组取"链节中点最贴近赤道"的光标纬度（连续松弛，上界）；
        /// 节点可能被夹极的组额外核两端。保护集为空时返回 +∞（不做任何合并）。
        /// </summary>
        internal double Distance(BlueprintBuilding a, BlueprintBuilding b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }
            if (groups.Count == 0)
            {
                return double.PositiveInfinity;
            }
            double worst = 0.0;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var f = group.Lo;
                double la = LatAt(f, a), lb = LatAt(f, b);
                double mid = 0.5 * (la + lb);
                double shift = -mid;
                if (shift < 0.0) shift = 0.0;
                if (shift > group.SpanRad) shift = group.SpanRad;
                double d = Chord(f, a, b, shift);
                double lo = Math.Min(la, lb), hi = Math.Max(la, lb);
                if (hi + group.SpanRad > 1.48 || lo < -1.48)
                {
                    // 可能夹极（|纬度| 逼近 90°）：单调性前提不再成立，两端补算。
                    d = Math.Max(d, Math.Max(Chord(f, a, b, 0.0), Chord(f, a, b, group.SpanRad)));
                }
                if (d > worst)
                {
                    worst = d;
                }
            }
            return worst;
        }

        // ------------------------------------------------------------------ 闸门 ------------------

        internal readonly struct GateResult
        {
            internal readonly int Over, KeptOver, Frames;
            internal readonly double Worst;
            internal readonly string First;
            internal GateResult(int over, int keptOver, int frames, double worst, string first)
            {
                Over = over; KeptOver = keptOver; Frames = frames; Worst = worst; First = first;
            }
        }

        /// <summary>
        /// 独立精确闸门：保护集**逐个姿态展开**，按游戏 float 公式算 lpos、用 sqrMagnitude 判定，
        /// 统计"新造跨度"在任一姿态越限的链节数（<paramref name="isPreexisting"/> 判为原件同跨度的
        /// 只记 KeptOver）。不经过 <see cref="Distance"/> 的解析化简，作为交付前的最终复核。
        /// </summary>
        internal GateResult Gate(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
            Func<BlueprintBuilding, BlueprintBuilding, bool> isPreexisting)
        {
            if (buildings == null)
            {
                return new GateResult(0, 0, 0, 0.0, "");
            }
            if (protectedFrames.Count == 0)
            {
                return new GateResult(int.MaxValue, 0, 0, double.PositiveInfinity, "无合法粘贴姿态");
            }
            var set = new HashSet<BlueprintBuilding>(buildings);
            var links = new List<(BlueprintBuilding a, BlueprintBuilding b, bool pre)>();
            foreach (var n in buildings)
            {
                if (n == null || !isBelt(n) || n.outputObj == null || !isBelt(n.outputObj) || !set.Contains(n.outputObj))
                {
                    continue;
                }
                links.Add((n, n.outputObj, isPreexisting != null && isPreexisting(n, n.outputObj)));
            }
            float limitSq = (float)(BeltPathOptimizer.MaximumMeters * BeltPathOptimizer.MaximumMeters);
            var overNew = new bool[links.Count];
            var overKept = new bool[links.Count];
            double worst = 0.0;
            string first = "";
            foreach (var f in protectedFrames)
            {
                for (int i = 0; i < links.Count; i++)
                {
                    var l = links[i];
                    float d2 = (LPos(l.b, f) - LPos(l.a, f)).sqrMagnitude;
                    if (!l.pre && d2 > worst * worst)
                    {
                        worst = Math.Sqrt(d2);
                    }
                    if (d2 <= limitSq)
                    {
                        continue;
                    }
                    if (l.pre)
                    {
                        overKept[i] = true;
                    }
                    else
                    {
                        if (!overNew[i] && first.Length == 0)
                        {
                            first = $"{f}: {Math.Sqrt(d2):F4} m ({l.a.localOffset_x:F3},{l.a.localOffset_y:F3})→({l.b.localOffset_x:F3},{l.b.localOffset_y:F3}) 区域{l.a.areaIndex}";
                        }
                        overNew[i] = true;
                    }
                }
            }
            int over = 0, kept = 0;
            for (int i = 0; i < links.Count; i++)
            {
                if (overNew[i]) over++;
                if (overKept[i]) kept++;
            }
            return new GateResult(over, kept, protectedFrames.Count, worst, first);
        }

        // ------------------------------------------------------------------ 单姿态 ----------------

        /// <summary>
        /// 与给定粘贴观测最接近的保护集姿态（同偏航象限优先，再按光标纬度格差最小）。
        /// 供"跨纬度安全关闭"时的单姿态精确模型用：观测纬度本身非法（如多区域蓝图的库内
        /// 赤道建模）时自动吸附到最近的合法位置。保护集为空时返回 null。
        /// </summary>
        internal Frame Nearest(BeltSpacingCalibration.Observation observation)
        {
            Frame best = null;
            int bestScore = int.MaxValue;
            int c = (int)Math.Round(observation.AnchorLatitudeRad / LatRadF, MidpointRounding.AwayFromZero);
            foreach (var f in protectedFrames)
            {
                int score = Math.Abs(f.LatGrid - c) + (f.Quadrant == observation.Quadrant ? 0 : 100000);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = f;
                }
            }
            return best;
        }

        /// <summary>单姿态双精度距离（与 <see cref="LPos"/> 同式，供关闭跨纬度安全时使用）。</summary>
        internal static double DistanceAt(Frame f, BlueprintBuilding a, BlueprintBuilding b)
            => f == null || a == null || b == null ? 0.0 : Chord(f, a, b, 0.0);

        internal int AreaCount => areaCount;
    }
}
