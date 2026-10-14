using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// Restores belt nodes where a merge moved the belt path out of an inserter's reach window.
    ///
    /// The game rebuilds an inserter connection from the belt PATH: a belt path point must lie within
    /// sqrt(2) ~ 1.414 m of the inserter body (pick end) or of its arm end (insert end). Merging two
    /// belt pieces into one longer piece keeps the line but changes the Bezier path, and a piece that
    /// used to pass right next to an inserter can end up 1.4-2 m away - the connection is then refused
    /// and the sorter shows "输入端失效"/"输出端失效".
    ///
    /// Instead of protecting inserters up front (which costs a lot of the savings), the merge runs at
    /// full strength first and this pass re-inserts ONLY the original node that the inserter needs, so
    /// the path returns exactly to its old shape at that spot. The cost is one belt per affected
    /// inserter.
    /// </summary>
    internal static class BeltWindowRepair
    {
        private const double WindowMeters = 1.41;
        private const double OnLineToleranceCells = 0.45;
        private const double ArmCells = 1.3;

        internal sealed class Report
        {
            internal int Ends, Violations, Restored, Unresolved;
            public override string ToString() =>
                $"分拣器取带窗口：检查 {Ends} 端，路径超窗 {Violations} 端，补回节点 {Restored} 处，无法补回 {Unresolved} 处";
        }

        internal static Report Repair(List<BlueprintBuilding> result, IEnumerable<BlueprintBuilding> preMerge,
            Func<BlueprintBuilding, bool> isBelt)
        {
            var report = new Report();
            if (result == null || preMerge == null)
            {
                return report;
            }
            var inResult = new HashSet<BlueprintBuilding>(result);
            var beltSet = new HashSet<BlueprintBuilding>(result.Where(isBelt));
            var deletedBelts = preMerge.Where(b => isBelt(b) && !inResult.Contains(b)).ToList();
            if (deletedBelts.Count == 0)
            {
                return report;
            }

            foreach (var inserter in result.Where(b => InserterBinder.IsInserterItem(b.itemId)).ToArray())
            {
                foreach (bool isInput in new[] { true, false })
                {
                    report.Ends++;
                    var target = isInput ? inserter.inputObj : inserter.outputObj;
                    if (target == null || !beltSet.Contains(target))
                    {
                        continue;
                    }
                    var referencePoints = ReferencePoints(inserter, isInput);
                    if (PathDistance(referencePoints, target, beltSet, isBelt) <= WindowMeters)
                    {
                        continue;
                    }
                    report.Violations++;
                    var restored = FindDeletedNode(referencePoints, target, beltSet, deletedBelts, isBelt);
                    if (restored == null)
                    {
                        report.Unresolved++;
                        continue;
                    }
                    // Splice the original node back between the piece head and its successor.
                    restored.inputObj = null;
                    restored.outputObj = target.outputObj;
                    target.outputObj = restored;
                    restored.outputFromSlot = 0;
                    restored.outputToSlot = 1;
                    restored.outputOffset = 0;
                    beltSet.Add(restored);
                    result.Add(restored);
                    deletedBelts.Remove(restored);
                    report.Restored++;
                }
            }
            return report;
        }

        private static List<(double x, double y)> ReferencePoints(BlueprintBuilding inserter, bool isInput)
        {
            var points = new List<(double x, double y)> { (inserter.localOffset_x, inserter.localOffset_y) };
            if (!isInput)
            {
                double yawRad = inserter.yaw * Math.PI / 180.0;
                double px = -Math.Sin(yawRad), py = Math.Cos(yawRad);
                points.Add((inserter.localOffset_x + px * ArmCells, inserter.localOffset_y + py * ArmCells));
                points.Add((inserter.localOffset_x - px * ArmCells, inserter.localOffset_y - py * ArmCells));
            }
            return points;
        }

        private static double PathDistance(List<(double x, double y)> points, BlueprintBuilding target,
            HashSet<BlueprintBuilding> beltSet, Func<BlueprintBuilding, bool> isBelt)
        {
            double best = double.MaxValue;
            foreach (var head in new[] { target, target.inputObj })
            {
                if (head == null || !beltSet.Contains(head))
                {
                    continue;
                }
                var tail = head.outputObj != null && beltSet.Contains(head.outputObj) ? head.outputObj : null;
                if (tail == null || ReferenceEquals(head, tail))
                {
                    continue;
                }
                foreach (var point in points)
                {
                    double d = BeltPathGeometry.DistanceToPiece(point.x, point.y, head, tail, isBelt) * 1.2566;
                    if (d < best)
                    {
                        best = d;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The original node that lies on the merged piece and sits closest to the inserter - putting it
        /// back makes the path pass right by the inserter again.
        /// </summary>
        private static BlueprintBuilding FindDeletedNode(List<(double x, double y)> points, BlueprintBuilding target,
            HashSet<BlueprintBuilding> beltSet, List<BlueprintBuilding> deletedBelts, Func<BlueprintBuilding, bool> isBelt)
        {
            var head = target.inputObj != null && beltSet.Contains(target.inputObj) ? target.inputObj : target;
            var tail = head.outputObj != null && beltSet.Contains(head.outputObj) ? head.outputObj : null;
            if (tail == null)
            {
                return null;
            }
            BlueprintBuilding best = null;
            double bestScore = double.MaxValue;
            foreach (var candidate in deletedBelts)
            {
                if (candidate.itemId != target.itemId || candidate.areaIndex != target.areaIndex)
                {
                    continue;
                }
                if (BeltPathGeometry.DistanceToPiece(candidate.localOffset_x, candidate.localOffset_y, head, tail, isBelt) > OnLineToleranceCells)
                {
                    continue;
                }
                foreach (var point in points)
                {
                    double dx = candidate.localOffset_x - point.x, dy = candidate.localOffset_y - point.y;
                    double score = Math.Sqrt(dx * dx + dy * dy);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        private static double PointToSegment(double px, double py, BlueprintBuilding head, BlueprintBuilding tail)
        {
            double dx = tail.localOffset_x - head.localOffset_x, dy = tail.localOffset_y - head.localOffset_y;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-9 ? 0 : ((px - head.localOffset_x) * dx + (py - head.localOffset_y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double qx = head.localOffset_x + dx * t, qy = head.localOffset_y + dy * t;
            return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }
    }
}
