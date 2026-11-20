using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlueprintBeltSaver
{
    internal static partial class BlueprintGridRestorer
    {
        /// <summary>
        /// Normalize ONLY existing serializer-scale noise. A full unbranched component is proposed
        /// at once; external equipment references, addon windows, seams, tiers and oblique routing
        /// block it. Longitudinal interpolants (e.g. 1/3 cells) are NOT rounded here. Signal/label
        /// metadata and reference identities are retained on the original owned nodes.
        /// </summary>
        private static HashSet<BlueprintBuilding> NormalizeGridNoise(BlueprintBuilding[] original,
            Dictionary<BlueprintBuilding, BlueprintBuilding> oldIn,
            Dictionary<BlueprintBuilding, BlueprintBuilding> oldOut,
            Dictionary<BlueprintBuilding, List<BlueprintBuilding>> incoming,
            PasteEnvelope envelope, bool quantizeHeight)
        {
            var changed = new HashSet<BlueprintBuilding>();
            var lines = Lines(original, oldIn, oldOut);
            var owners = original.Where(n => !Belt(n)).ToArray();
            var obstacles = owners.Where(n => !(n.itemId >= 2011 && n.itemId <= 2039 && n.itemId != 2030)).ToArray();
            var boxes = new List<BuildingCollision.WorldBox>();
            foreach (var o in obstacles) BuildingCollision.TryGetWorldBoxes(o, boxes);
            foreach (var group in original.Where(Belt).GroupBy(n => lines[n]))
            {
                var nodes = group.ToArray(); var set = new HashSet<BlueprintBuilding>(nodes);
                if (nodes.Length < 2 || owners.Any(o => set.Contains(o.inputObj) || set.Contains(o.outputObj))) continue;
                if (nodes.Any(n => !SameTier(n, nodes[0]) || incoming[n].Count > 1 ||
                    (n.inputObj != null && !Belt(n.inputObj)) || (n.outputObj != null && !Belt(n.outputObj)) ||
                    (Belt(n.inputObj) && oldOut[n.inputObj] != n) ||
                    (Belt(oldOut[n]) && (Math.Abs(n.localOffset_x - oldOut[n].localOffset_x) > 64 || Math.Abs(n.localOffset_y - oldOut[n].localOffset_y) > 64)))) continue;
                var proposed = new Dictionary<BlueprintBuilding, BlueprintBuilding>();
                foreach (var n in nodes)
                {
                    float x = Integer(n.localOffset_x) ? (float)Math.Round(n.localOffset_x) : n.localOffset_x;
                    float y = Integer(n.localOffset_y) ? (float)Math.Round(n.localOffset_y) : n.localOffset_y;
                    float z = quantizeHeight && Half(n.localOffset_z) ? (float)(Math.Round(n.localOffset_z * 2) / 2) : n.localOffset_z;
                    proposed[n] = new BlueprintBuilding { areaIndex = n.areaIndex, itemId = n.itemId, modelIndex = n.modelIndex,
                        localOffset_x = x, localOffset_y = y, localOffset_z = z };
                }
                var moved = nodes.Where(n => !SamePosition(n, proposed[n])).ToArray();
                if (moved.Length == 0) continue;
                bool valid = true;
                foreach (var n in nodes)
                {
                    var p = proposed[n]; var next = oldOut[n];
                    if (next != null && Belt(next) && LineAxis(p, proposed[next]) < 0) { valid = false; break; }
                    if (obstacles.Any(o => o.areaIndex == n.areaIndex && BeltPathOptimizer.IsBeltAddon(o.itemId) &&
                        (GridDistance(p, o) < 3.5 || (Belt(next) && SegmentDistance(o, p, proposed[next]) < 3.5)))) { valid = false; break; }
                    if (moved.Contains(n) && (BuildingCollision.Hits(p, boxes) || NearForeignBelt(p, original, lines, group.Key) ||
                        nodes.Any(other => other != n && oldOut[n] != other && oldOut[other] != n && oldIn[n] != other && oldIn[other] != n && GridDistance(p, proposed[other]) < 0.4)))
                    { valid = false; break; }
                    if (!moved.Contains(n) && (next == null || !moved.Contains(next))) continue;
                    if (Belt(next))
                    {
                        if (!SpanWithinWorkingPoses(p, proposed[next], envelope)) { valid = false; break; }
                    }
                }
                if (!valid) continue;
                // Coordinate application happens only after the entire component passed validation.
                foreach (var n in moved)
                {
                    var p = proposed[n];
                    n.localOffset_x = n.localOffset_x2 = p.localOffset_x;
                    n.localOffset_y = n.localOffset_y2 = p.localOffset_y;
                    if (quantizeHeight) n.localOffset_z = n.localOffset_z2 = p.localOffset_z;
                    changed.Add(n);
                }
            }
            return changed;
        }

        private static bool SpanWithinWorkingPoses(BlueprintBuilding a, BlueprintBuilding b, PasteEnvelope env)
        {
            float min = (float)(BeltPathOptimizer.MinimumMeters * BeltPathOptimizer.MinimumMeters);
            float max = (float)(BeltPathOptimizer.MaximumMeters * BeltPathOptimizer.MaximumMeters);
            foreach (var f in env.ProtectedFrames)
            {
                float d2 = (PasteEnvelope.LPos(b, f) - PasteEnvelope.LPos(a, f)).sqrMagnitude;
                if (float.IsNaN(d2) || float.IsInfinity(d2) || d2 < min || d2 > max) return false;
            }
            return true;
        }

        /// <summary>
        /// Game-native SnapLineNonAlloc generates the route on a genuine snapped longitude-zero
        /// reference frame. Using the envelope's arbitrary 0.3-radian longitude would manufacture a
        /// fractional blueprint-coordinate phase after inverse projection. No destructive source edit.
        /// Return null for non-grid endpoints or unsupported projections, then use the conservative
        /// existing path reconstruction instead. Attachments keep their owned anchors/window.
        /// </summary>
        private static List<(float x, float y)> NativeGridRoute(List<BlueprintBuilding> path, int axis,
            PasteEnvelope env, BlueprintArea[] areas, int primaryAreaIdx)
        {
            var first = path[0]; var last = path.Last();
            if (!Integer(first.localOffset_x) || !Integer(first.localOffset_y) || !Integer(last.localOffset_x) || !Integer(last.localOffset_y)) return null;
            var nearest = env.ProtectedFrames.Where(f => f.Quadrant == 0).OrderBy(f => Math.Abs(f.LatGrid)).FirstOrDefault();
            if (nearest == null) return null;
            var frame = PasteEnvelope.FrameForNativeGrid(areas, primaryAreaIdx, nearest.LatGrid, nearest.Yaw);
            if (frame == null) return null;
            double length = Math.Abs(Axis(last, axis) - Axis(first, axis));
            if (length > RunLimit) return null;
            var snaps = new Vector3[Math.Max(32, (int)Math.Ceiling(length) + 16)];
            var grid = new PlanetGrid(EGridType.LatitudeLongitude, 200, Quaternion.identity);
            int count = grid.SnapLineNonAlloc(PasteEnvelope.LPos(first, frame), PasteEnvelope.LPos(last, frame), axis == 0 ? 1 : 0, snaps, out _);
            if (count < 2 || count >= snaps.Length - 10) return null;
            var cells = new List<(float x, float y)>();
            int ai = frame.RefLat.Length == 1 ? 0 : first.areaIndex;
            double midLongitudeAxis = frame.Swap ? (first.localOffset_y + last.localOffset_y) * 0.5 : (first.localOffset_x + last.localOffset_x) * 0.5;
            double period = Math.PI * 2 / frame.LonPerGrid[ai];
            foreach (var v in snaps.Take(count))
            {
                float lon = 0, lat = 0;
                BlueprintUtils.GetLongitudeLatitudeRad(v.normalized, ref lon, ref lat);
                double lngAxis = Math.IEEERemainder(lon - frame.RefLon[ai], Math.PI * 2) / (frame.LonPerGrid[ai] * frame.LonSign);
                lngAxis += Math.Round((midLongitudeAxis - lngAxis) / period) * period;
                double latAxis = (lat - frame.RefLat[ai]) / (BlueprintUtils.GetLatitudeRadPerGrid(200) * frame.LatSign);
                double x = frame.Swap ? latAxis : lngAxis, y = frame.Swap ? lngAxis : latAxis;
                if (!Integer(x) || !Integer(y)) return null;
                var p = ((float)Math.Round(x), (float)Math.Round(y));
                if (cells.Count == 0 || cells[cells.Count - 1] != p) cells.Add(p);
            }
            if (cells.Count < 2 || Math.Abs(cells[0].x - first.localOffset_x) > Epsilon || Math.Abs(cells[0].y - first.localOffset_y) > Epsilon ||
                Math.Abs(cells.Last().x - last.localOffset_x) > Epsilon || Math.Abs(cells.Last().y - last.localOffset_y) > Epsilon) return null;
            double direction = Math.Sign(Axis(last, axis) - Axis(first, axis));
            for (int i = 1; i < cells.Count; i++)
            {
                double delta = axis == 0 ? cells[i].x - cells[i - 1].x : cells[i].y - cells[i - 1].y;
                if (delta * direction <= 0 || (axis == 0 ? Math.Abs(cells[i].y - first.localOffset_y) : Math.Abs(cells[i].x - first.localOffset_x)) > Epsilon) return null;
            }
            return cells;
        }
    }
}
