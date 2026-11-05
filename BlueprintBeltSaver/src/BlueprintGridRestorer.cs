using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// Reconstruct one-cell grid steps on the CURRENT flat, axis-aligned belt path (not undo).
    /// Turns, slopes, endpoints, tiers, labels and attachment windows stay intact. No benefit test,
    /// no nearest-neighbour rewiring and no configuration switch bypasses safety checks.
    /// </summary>
    internal static class BlueprintGridRestorer
    {
        private const double Epsilon = 0.0002; // serializer noise, not a routing tolerance
        private const int BuildingLimit = 15000;
        private const int RunLimit = 8192;
        internal sealed class Report
        {
            internal int Before, After, Added, Removed, RestoredRuns, ProtectedRuns, UnaffectedRuns, CheckedPoses, ProtectedNodes;
            internal string Reason = "", ProtectionDetails = "";
            internal string[] ProtectedPoints = new string[0];
        }
        private static bool Belt(BlueprintBuilding n) => n != null && n.itemId >= 2001 && n.itemId <= 2009;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Integer(double v) => Math.Abs(v - Math.Round(v)) <= Epsilon;
        private static bool Half(double v) => Math.Abs(v - Math.Round(v * 2) / 2) <= Epsilon;
        private static double Axis(BlueprintBuilding n, int axis) => axis == 0 ? n.localOffset_x : n.localOffset_y;
        private static bool SameTier(BlueprintBuilding a, BlueprintBuilding b)
            => a.areaIndex == b.areaIndex && a.itemId == b.itemId && a.modelIndex == b.modelIndex;
        private static bool Metadata(BlueprintBuilding n)
            => n.filterId != 0 || n.recipeId != 0 || !string.IsNullOrEmpty(n.content) || (n.parameters != null && n.parameters.Length != 0);
        private static int LineAxis(BlueprintBuilding a, BlueprintBuilding b)
        {
            if (!SameTier(a, b) || Math.Abs(a.localOffset_z - b.localOffset_z) > Epsilon) return -1;
            double dx = Math.Abs(a.localOffset_x - b.localOffset_x), dy = Math.Abs(a.localOffset_y - b.localOffset_y);
            // A short physical seam link can have a huge raw coordinate delta. It must not be
            // interpreted as a many-cell straight run through the whole blueprint.
            if (Math.Max(dx, dy) > 64) return -1;
            if (dx > Epsilon && dy <= Epsilon && Integer(a.localOffset_y) && Integer(b.localOffset_y)) return 0;
            if (dy > Epsilon && dx <= Epsilon && Integer(a.localOffset_x) && Integer(b.localOffset_x)) return 1;
            return -1;
        }

        internal static bool TryRestore(BlueprintData source, out BlueprintData restored, out Report report,
            bool quantizeHeight = true, bool strictGrid = false)
        {
            restored = null;
            report = new Report();
            try
            {
                if (source?.buildings == null || source.buildings.Length == 0 || source.areas == null || source.areas.Length == 0)
                    return Fail(report, "invalid-blueprint");
                if (source.buildings.Length > BuildingLimit) return Fail(report, "building-limit");
                var srcSet = new HashSet<BlueprintBuilding>(source.buildings);
                if (srcSet.Count != source.buildings.Length || srcSet.Contains(null)) return Fail(report, "invalid-buildings");
                foreach (var n in source.buildings)
                {
                    if (!Finite(n.localOffset_x) || !Finite(n.localOffset_y) || !Finite(n.localOffset_z)) return Fail(report, "nonfinite-coordinate");
                    if ((n.inputObj != null && !srcSet.Contains(n.inputObj)) || (n.outputObj != null && !srcSet.Contains(n.outputObj)))
                        return Fail(report, "dangling-reference");
                }
                var bp = CloneOwned(source); // serializer mutates Version/Patch; own the graph before exporting
                if (bp?.buildings == null || bp.buildings.Length != source.buildings.Length) return Fail(report, "clone-failed");
                var original = bp.buildings;
                var belts = new HashSet<BlueprintBuilding>(original.Where(Belt));
                report.Before = report.After = belts.Count;
                var oldOut = original.ToDictionary(n => n, n => n.outputObj);
                var oldIn = original.ToDictionary(n => n, n => n.inputObj);
                var incoming = belts.ToDictionary(n => n, n => new List<BlueprintBuilding>());
                foreach (var n in original) if (Belt(n) && Belt(n.outputObj)) incoming[n.outputObj].Add(n);
                var anchors = new HashSet<BlueprintBuilding>();
                var frozen = new HashSet<BlueprintBuilding>();
                foreach (var n in original.Where(Belt))
                {
                    var pred = incoming[n]; var next = n.outputObj;
                    if (pred.Count != 1 || !Belt(next) || Metadata(n) || (n.inputObj != null && (pred.Count != 1 || n.inputObj != pred[0]))) anchors.Add(n);
                    if (pred.Count == 1 && Belt(next))
                    {
                        int left = LineAxis(pred[0], n), right = LineAxis(n, next);
                        if (left < 0 || left != right || (Axis(n, right) - Axis(pred[0], right)) * (Axis(next, right) - Axis(n, right)) <= 0)
                        {
                            anchors.Add(n);
                            Freeze(n, 1, frozen, oldIn, oldOut, incoming);
                        }
                    }
                    if (Belt(n.inputObj) && oldOut[n.inputObj] != n)
                    {
                        Freeze(n, 2, frozen, oldIn, oldOut, incoming);
                        Freeze(n.inputObj, 2, frozen, oldIn, oldOut, incoming);
                    }
                }
                foreach (var n in original.Where(n => !Belt(n)))
                {
                    if (Belt(n.inputObj)) Freeze(n.inputObj, 2, frozen, oldIn, oldOut, incoming);
                    if (Belt(n.outputObj)) Freeze(n.outputObj, 2, frozen, oldIn, oldOut, incoming);
                }
                var obstacles = original.Where(n => !Belt(n) && !(n.itemId >= 2011 && n.itemId <= 2039 && n.itemId != 2030)).ToArray();
                var addons = obstacles.Where(n => BeltPathOptimizer.IsBeltAddon(n.itemId)).ToArray();
                var boxes = new List<BuildingCollision.WorldBox>();
                foreach (var obstacle in obstacles) BuildingCollision.TryGetWorldBoxes(obstacle, boxes);
                // Automatic addon connections are rebuilt by proximity. Adding a closer node could
                // steal an existing cargo/inc/monitor/turret connection, so freeze the whole window.
                foreach (var addon in addons)
                    foreach (var n in original.Where(Belt))
                    {
                        if (n.areaIndex == addon.areaIndex && GridDistance(n, addon) < 3.5) Freeze(n, 2, frozen, oldIn, oldOut, incoming);
                        if (Belt(n.outputObj) && n.areaIndex == addon.areaIndex && n.outputObj.areaIndex == n.areaIndex && SegmentDistance(addon, n, n.outputObj) < 3.5)
                        { Freeze(n, 2, frozen, oldIn, oldOut, incoming); Freeze(n.outputObj, 2, frozen, oldIn, oldOut, incoming); }
                    }
                anchors.UnionWith(frozen);
                var visited = new HashSet<BlueprintBuilding>();
                foreach (var n in original.Where(n => Belt(n) && anchors.Contains(n))) Walk(n, visited, oldOut);
                foreach (var n in original.Where(Belt)) if (!visited.Contains(n)) { anchors.Add(n); Walk(n, visited, oldOut); }
                var envelope = PasteEnvelope.Build(bp.areas, bp.primaryAreaIdx, original, Belt);
                if (envelope.Empty) return Fail(report, "no-legal-placement");
                report.CheckedPoses = envelope.ProtectedCount;
                var lineOf = Lines(original, oldIn, oldOut);
                var ancestry = new Dictionary<BlueprintBuilding, int>(lineOf);
                var removed = new HashSet<BlueprintBuilding>();
                var insertAfter = new Dictionary<BlueprintBuilding, List<BlueprintBuilding>>();
                foreach (var start in original.Where(n => Belt(n) && anchors.Contains(n)))
                {
                    var path = new List<BlueprintBuilding> { start };
                    var seen = new HashSet<BlueprintBuilding> { start };
                    var next = oldOut[start];
                    while (Belt(next) && !seen.Contains(next))
                    {
                        path.Add(next);
                        if (anchors.Contains(next)) break;
                        seen.Add(next); next = oldOut[next];
                    }
                    if (path.Count < 2) continue;
                    int axis = LineAxis(path[0], path[1]);
                    if (axis < 0 || (path.Count == 2 && path.All(frozen.Contains)) || path.Skip(1).Take(path.Count - 2).Any(frozen.Contains) ||
                        (quantizeHeight && path.Any(n => !Half(n.localOffset_z))))
                    { report.ProtectedRuns++; continue; }
                    bool compatible = true;
                    for (int k = 1; k < path.Count; k++)
                        if (LineAxis(path[k - 1], path[k]) != axis ||
                            (Axis(path[k], axis) - Axis(path[k - 1], axis)) * (Axis(path[1], axis) - Axis(start, axis)) <= 0) compatible = false;
                    if (!compatible || Math.Abs(Axis(path.Last(), axis) - Axis(start, axis)) > RunLimit)
                    { report.ProtectedRuns++; continue; }
                    var replacement = GridSequence(path, axis, envelope, quantizeHeight);
                    if (replacement == null || replacement.Any(n => !NativeGridScaleMatches(n, envelope)))
                    { report.ProtectedRuns++; continue; }
                    if (SameSequence(path, replacement)) { report.UnaffectedRuns++; continue; }
                    bool collision = false;
                    foreach (var n in replacement)
                    {
                        if (path.Contains(n)) continue;
                        if (BuildingCollision.Hits(n, boxes) || NearForeignBelt(n, original, lineOf, lineOf[start]) ||
                            addons.Any(a => a.areaIndex == n.areaIndex && GridDistance(n, a) < 3.5))
                        { collision = true; break; }
                    }
                    if (collision) { report.ProtectedRuns++; continue; }
                    var repSet = new HashSet<BlueprintBuilding>(replacement);
                    var generated = replacement.Where(n => !oldOut.ContainsKey(n)).ToList();
                    if (original.Length - removed.Count + report.Added + generated.Count > BuildingLimit) return Fail(report, "building-limit");
                    for (int k = 1; k < path.Count - 1; k++) if (!repSet.Contains(path[k])) removed.Add(path[k]);
                    int toSlot = path[path.Count - 2].outputToSlot;
                    for (int k = 0; k < replacement.Count - 1; k++)
                    {
                        replacement[k].outputObj = replacement[k + 1];
                        replacement[k].outputToSlot = k == replacement.Count - 2 ? toSlot : 1;
                        if (k > 0) replacement[k].inputObj = replacement[k - 1];
                    }
                    var tail = replacement.Last();
                    if (oldIn[tail] == path[path.Count - 2]) tail.inputObj = replacement[replacement.Count - 2];
                    insertAfter[start] = generated;
                    foreach (var n in generated) ancestry[n] = lineOf[start];
                    report.Added += generated.Count; report.RestoredRuns++;
                }
                if (report.RestoredRuns > 0)
                {
                    var result = new List<BlueprintBuilding>();
                    foreach (var n in original)
                    {
                        if (removed.Contains(n)) continue;
                        result.Add(n);
                        if (insertAfter.TryGetValue(n, out var nodes)) result.AddRange(nodes);
                    }
                    bp.buildings = result.Where(Belt).Concat(result.Where(n => !Belt(n))).ToArray();
                }
                report.Removed = removed.Count; report.After = bp.buildings.Count(Belt);
                var exceptions = bp.buildings.Where(n => Belt(n) && (!Integer(n.localOffset_x) || !Integer(n.localOffset_y) || (quantizeHeight && !Half(n.localOffset_z)))).ToArray();
                report.ProtectedNodes = exceptions.Length;
                report.ProtectedPoints = exceptions.Select(n => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2:R},{3:R},{4:R}", n.index, n.areaIndex, n.localOffset_x, n.localOffset_y, n.localOffset_z)).ToArray();
                report.ProtectionDetails = string.Join("\n", exceptions.Take(12).Select(n => $"#{n.index} area={n.areaIndex} ({n.localOffset_x:0.####}, {n.localOffset_y:0.####}, {n.localOffset_z:0.####})"));
                if (strictGrid && exceptions.Length > 0) return Fail(report, "strict-grid-conflict");
                if (strictGrid)
                {
                    // Only sub-mm serializer noise remains. Snap it exactly; no geometric routing.
                    foreach (var n in bp.buildings.Where(Belt))
                    {
                        n.localOffset_x = n.localOffset_x2 = (float)Math.Round(n.localOffset_x);
                        n.localOffset_y = n.localOffset_y2 = (float)Math.Round(n.localOffset_y);
                        if (quantizeHeight) n.localOffset_z = n.localOffset_z2 = (float)(Math.Round(n.localOffset_z * 2) / 2);
                    }
                }
                if (!VerifyTopology(original, bp.buildings, oldIn, oldOut, ancestry, removed)) return Fail(report, "topology-failed");
                var spans = new HashSet<(BlueprintBuilding, BlueprintBuilding)>();
                foreach (var n in original.Where(Belt)) if (Belt(oldOut[n])) spans.Add((n, oldOut[n]));
                if (envelope.Gate(bp.buildings, Belt, (a, b) => spans.Contains((a, b))).Over != 0) return Fail(report, "distance-failed");
                if (report.RestoredRuns > 0 || strictGrid)
                {
                    // The game filters index != array position as invalid data; it does NOT simply
                    // reindex. New nodes must have their final IDs before CheckBuildingData reads them.
                    for (int i = 0; i < bp.buildings.Length; i++) bp.buildings[i].index = i;
                    int count = bp.buildings.Length;
                    bp.CheckBuildingData();
                    if (bp.buildings.Length != count || !VerifyTopology(original, bp.buildings, oldIn, oldOut, ancestry, removed))
                        return Fail(report, "topology-failed");
                }
                report.After = bp.buildings.Count(Belt);
                var roundtrip = bp.Clone();
                if (roundtrip?.buildings == null || roundtrip.buildings.Length != bp.buildings.Length) return Fail(report, "serialize-failed");
                for (int i = 0; i < bp.buildings.Length; i++)
                {
                    var before = bp.buildings[i]; var after = roundtrip.buildings[i];
                    if (before.itemId != after.itemId || before.modelIndex != after.modelIndex ||
                        (before.inputObj?.index ?? -1) != (after.inputObj?.index ?? -1) ||
                        (before.outputObj?.index ?? -1) != (after.outputObj?.index ?? -1))
                        return Fail(report, "topology-failed");
                }
                if (envelope.Gate(roundtrip.buildings, Belt, (a, b) => a.index >= 0 && b.index >= 0 &&
                    a.index < bp.buildings.Length && b.index < bp.buildings.Length &&
                    spans.Contains((bp.buildings[a.index], bp.buildings[b.index]))).Over != 0)
                    return Fail(report, "distance-failed");
                restored = bp;
                return true;
            }
            catch (Exception ex) { report.Reason = "restore-exception:" + ex.GetType().Name; return false; }
        }
        /// <summary>Copy every field without running Export (which writes private Version/Patch).
        /// Mutable arrays and reference graphs are owned by the result; header/private version survive.</summary>
        internal static BlueprintData CloneOwned(BlueprintData source)
        {
            var memberwise = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            T CopyObject<T>(T o) where T : class => o == null ? null : (T)memberwise.Invoke(o, null);
            var bp = CopyObject(source);
            if (bp == null) return null;
            bp.areas = source.areas?.Select(a => CopyObject(a)).ToArray();
            var map = new Dictionary<BlueprintBuilding, BlueprintBuilding>();
            foreach (var n in source.buildings ?? new BlueprintBuilding[0])
            {
                var clone = CopyObject(n);
                clone.parameters = n.parameters == null ? null : (int[])n.parameters.Clone();
                map.Add(n, clone);
            }
            bp.buildings = source.buildings?.Select(n => map[n]).ToArray();
            foreach (var pair in map)
            {
                pair.Value.inputObj = pair.Key.inputObj == null ? null : map[pair.Key.inputObj];
                pair.Value.outputObj = pair.Key.outputObj == null ? null : map[pair.Key.outputObj];
            }
            bp.reformData = CopyObject(source.reformData);
            if (bp.reformData != null)
            {
                bp.reformData.rects = source.reformData.rects == null ? null : (BPReformRect[])source.reformData.rects.Clone();
                bp.reformData.customReformColors = source.reformData.customReformColors == null ? null : (uint[])source.reformData.customReformColors.Clone();
            }
            foreach (var field in typeof(BlueprintData).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                if (field.FieldType == typeof(List<BlueprintBuilding>))
                    field.SetValue(bp, new List<BlueprintBuilding>());
            return bp;
        }
        private static bool Fail(Report r, string reason) { r.Reason = reason; return false; }
        private static void Walk(BlueprintBuilding n, HashSet<BlueprintBuilding> seen, Dictionary<BlueprintBuilding, BlueprintBuilding> next)
        { while (Belt(n) && seen.Add(n)) n = next[n]; }
        private static void Freeze(BlueprintBuilding n, int depth, HashSet<BlueprintBuilding> frozen,
            Dictionary<BlueprintBuilding, BlueprintBuilding> ins, Dictionary<BlueprintBuilding, BlueprintBuilding> outs,
            Dictionary<BlueprintBuilding, List<BlueprintBuilding>> incoming)
        {
            if (!Belt(n)) return;
            frozen.Add(n);
            if (depth == 0) return;
            Freeze(outs[n], depth - 1, frozen, ins, outs, incoming);
            Freeze(ins[n], depth - 1, frozen, ins, outs, incoming);
            foreach (var p in incoming[n]) Freeze(p, depth - 1, frozen, ins, outs, incoming);
        }
        private static List<BlueprintBuilding> GridSequence(List<BlueprintBuilding> path, int axis, PasteEnvelope env, bool height)
        {
            var first = path[0]; var last = path.Last();
            double from = Axis(first, axis), to = Axis(last, axis), dir = Math.Sign(to - from);
            var candidates = new List<BlueprintBuilding> { first, last };
            candidates.AddRange(path.Skip(1).Take(path.Count - 2).Where(n => Integer(Axis(n, axis))));
            int min = (int)Math.Ceiling(Math.Min(from, to)), max = (int)Math.Floor(Math.Max(from, to));
            for (int cell = min; cell <= max; cell++)
            {
                if ((cell - from) * dir <= Epsilon || (to - cell) * dir <= Epsilon || candidates.Any(n => Math.Abs(Axis(n, axis) - cell) <= Epsilon)) continue;
                int donor = 0;
                for (int j = 1; j < path.Count; j++)
                    if ((cell - Axis(path[j - 1], axis)) * dir >= -Epsilon && (Axis(path[j], axis) - cell) * dir >= -Epsilon)
                    { donor = j; break; }
                // Inherit from the source piece covering this cell, not a remote run endpoint with
                // a potentially different orientation. Do not duplicate signals/labels on new nodes.
                var template = path[Math.Min(donor, path.Count - 1)];
                var n = Copy(template, axis == 0 ? cell : (float)Math.Round(first.localOffset_x), axis == 1 ? cell : (float)Math.Round(first.localOffset_y), height);
                candidates.Add(n);
            }
            candidates.Sort((a, b) => (dir * Axis(a, axis)).CompareTo(dir * Axis(b, axis)));
            for (int i = candidates.Count - 2; i >= 1; i--)
            {
                var n = candidates[i];
                if (path.Contains(n)) continue;
                bool tooShort = false;
                foreach (var f in env.ProtectedFrames)
                {
                    var p = PasteEnvelope.LPos(n, f);
                    if ((p - PasteEnvelope.LPos(candidates[i - 1], f)).sqrMagnitude < 0.400001f * 0.400001f ||
                        (PasteEnvelope.LPos(candidates[i + 1], f) - p).sqrMagnitude < 0.400001f * 0.400001f) { tooShort = true; break; }
                }
                if (tooShort) candidates.RemoveAt(i);
            }
            var oldSpans = new HashSet<(BlueprintBuilding, BlueprintBuilding)>();
            for (int i = 1; i < path.Count; i++) oldSpans.Add((path[i - 1], path[i]));
            for (int i = 1; i < candidates.Count; i++)
            {
                if (oldSpans.Contains((candidates[i - 1], candidates[i]))) continue;
                double d = env.Distance(candidates[i - 1], candidates[i]);
                if (double.IsNaN(d) || double.IsInfinity(d) || d > BeltPathOptimizer.MaximumMeters * 0.98) return null;
                foreach (var f in env.ProtectedFrames)
                    if ((PasteEnvelope.LPos(candidates[i], f) - PasteEnvelope.LPos(candidates[i - 1], f)).sqrMagnitude < 0.400001f * 0.400001f) return null;
            }
            return candidates;
        }
        private static bool NativeGridScaleMatches(BlueprintBuilding n, PasteEnvelope envelope)
        {
            foreach (var f in envelope.ProtectedFrames)
            {
                int area = f.RefLat.Length == 1 ? 0 : n.areaIndex;
                if (area < 0 || area >= f.RefLat.Length) return false;
                float lat = f.RefLat[area] + (f.Swap ? n.localOffset_x : n.localOffset_y) *
                    BlueprintUtils.GetLatitudeRadPerGrid(200) * f.LatSign;
                if (Math.Abs(BlueprintUtils.GetLongitudeRadPerGrid(lat, 200) - f.LonPerGrid[area]) > 0.00000005f) return false;
            }
            return true;
        }
        private static bool SameSequence(List<BlueprintBuilding> a, List<BlueprintBuilding> b)
            => a.Count == b.Count && a.Zip(b, (x, y) => x == y).All(x => x);
        private static BlueprintBuilding Copy(BlueprintBuilding b, float x, float y, bool height)
            => new BlueprintBuilding
            {
                areaIndex = b.areaIndex, itemId = b.itemId, modelIndex = b.modelIndex,
                yaw = b.yaw, yaw2 = b.yaw2, pitch = b.pitch, pitch2 = b.pitch2, tilt = b.tilt, tilt2 = b.tilt2,
                localOffset_x = x, localOffset_x2 = x, localOffset_y = y, localOffset_y2 = y,
                localOffset_z = height ? (float)(Math.Round(b.localOffset_z * 2) / 2) : b.localOffset_z,
                localOffset_z2 = height ? (float)(Math.Round(b.localOffset_z * 2) / 2) : b.localOffset_z,
                outputFromSlot = 0, outputToSlot = 1, inputFromSlot = 0, inputToSlot = 1,
                parameters = new int[0], content = "", tempInputObjIdx = -1, tempOutputObjIdx = -1
            };
        private static bool NearForeignBelt(BlueprintBuilding n, BlueprintBuilding[] original, Dictionary<BlueprintBuilding, int> lineOf, int ownLine)
        {
            foreach (var b in original.Where(Belt))
            {
                if (b.areaIndex != n.areaIndex || lineOf[b] == ownLine) continue;
                if (GridDistance(n, b) < 0.4) return true;
                if (Belt(b.outputObj) && b.outputObj.areaIndex == n.areaIndex && SegmentDistance(n, b, b.outputObj) < 0.4) return true;
            }
            return false;
        }
        private static double GridDistance(BlueprintBuilding a, BlueprintBuilding b)
        { double x = a.localOffset_x - b.localOffset_x, y = a.localOffset_y - b.localOffset_y, z = (a.localOffset_z - b.localOffset_z) * 1.06; return Math.Sqrt(x * x + y * y + z * z); }
        private static double SegmentDistance(BlueprintBuilding n, BlueprintBuilding a, BlueprintBuilding b)
        {
            double dx = b.localOffset_x - a.localOffset_x, dy = b.localOffset_y - a.localOffset_y, dz = (b.localOffset_z - a.localOffset_z) * 1.06;
            double len = dx * dx + dy * dy + dz * dz;
            double t = len < 1e-12 ? 0 : ((n.localOffset_x - a.localOffset_x) * dx + (n.localOffset_y - a.localOffset_y) * dy + (n.localOffset_z - a.localOffset_z) * 1.06 * dz) / len;
            t = Math.Max(0, Math.Min(1, t));
            double x = n.localOffset_x - a.localOffset_x - t * dx, y = n.localOffset_y - a.localOffset_y - t * dy, z = (n.localOffset_z - a.localOffset_z) * 1.06 - t * dz;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        private static Dictionary<BlueprintBuilding, int> Lines(BlueprintBuilding[] all,
            Dictionary<BlueprintBuilding, BlueprintBuilding> ins, Dictionary<BlueprintBuilding, BlueprintBuilding> outs)
        {
            var parent = all.Where(Belt).ToDictionary(n => n, n => n);
            BlueprintBuilding Root(BlueprintBuilding n) { while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; } return n; }
            foreach (var n in parent.Keys.ToArray()) foreach (var next in new[] { ins[n], outs[n] })
                if (Belt(next) && parent.ContainsKey(next)) parent[Root(n)] = Root(next);
            var ids = new Dictionary<BlueprintBuilding, int>(); var result = new Dictionary<BlueprintBuilding, int>();
            foreach (var n in all.Where(Belt))
            { var root = Root(n); if (!ids.TryGetValue(root, out int id)) { id = ids.Count; ids.Add(root, id); } result.Add(n, id); }
            return result;
        }
        private static bool VerifyTopology(BlueprintBuilding[] original, BlueprintBuilding[] result,
            Dictionary<BlueprintBuilding, BlueprintBuilding> ins, Dictionary<BlueprintBuilding, BlueprintBuilding> outs,
            Dictionary<BlueprintBuilding, int> ancestry, HashSet<BlueprintBuilding> removed)
        {
            var set = new HashSet<BlueprintBuilding>(result);
            if (set.Count != result.Length) return false;
            foreach (var n in result)
            {
                if ((n.inputObj != null && !set.Contains(n.inputObj)) || (n.outputObj != null && !set.Contains(n.outputObj))) return false;
                if (!Belt(n) && (n.inputObj != ins[n] || n.outputObj != outs[n])) return false;
            }
            var now = Lines(result, result.ToDictionary(n => n, n => n.inputObj), result.ToDictionary(n => n, n => n.outputObj));
            var o2r = new Dictionary<int, int>(); var r2o = new Dictionary<int, int>();
            foreach (var n in result.Where(Belt))
            {
                int o = ancestry[n], r = now[n];
                if ((o2r.TryGetValue(o, out int rr) && rr != r) || (r2o.TryGetValue(r, out int oo) && oo != o)) return false;
                o2r[o] = r; r2o[r] = o;
            }
            if (o2r.Count != ancestry.Values.Distinct().Count()) return false;
            foreach (var n in original.Where(n => Belt(n) && !removed.Contains(n)))
            {
                var wanted = outs[n]; var seenOld = new HashSet<BlueprintBuilding>();
                while (wanted != null && removed.Contains(wanted) && seenOld.Add(wanted)) wanted = outs[wanted];
                var actual = n.outputObj; var seenNew = new HashSet<BlueprintBuilding>();
                while (actual != null && !outs.ContainsKey(actual) && seenNew.Add(actual)) actual = actual.outputObj;
                if (actual != wanted) return false;
            }
            return true;
        }
        private static bool SamePosition(BlueprintBuilding a, BlueprintBuilding b)
            => a.areaIndex == b.areaIndex && a.localOffset_x == b.localOffset_x && a.localOffset_y == b.localOffset_y && a.localOffset_z == b.localOffset_z;
    }
}
