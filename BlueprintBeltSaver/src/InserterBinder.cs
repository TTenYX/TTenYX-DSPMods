using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// Verifies every inserter end after belt merging and re-binds broken ones.
    ///
    /// Merging removes belt nodes; an inserter that pointed at a removed node loses its target (the
    /// game nulls the reference), which shows up in game as a dead sorter. The thresholds below are
    /// measured from a blueprint the game itself accepted (616 inserters, zero empty ends):
    ///   distance inserter centre to target belt node: median 0.66 m, p95 2.94 m, max 4.05 m
    ///   acute angle between the belt yaw and the inserter yaw: median 90.0 deg, p05 89.74 deg
    ///   projection of the inserter onto the target span: about -0.46 .. +0.60
    /// so a valid target sits within reach, runs ACROSS the inserter (about 90 deg, occasionally
    /// along it), and its span covers the inserter's position.
    /// </summary>
    internal static class InserterBinder
    {
        private const double MinReachMeters = 0.55;
        private const double MaxReachMeters = 5.2;
        /// <summary>The game's own belt-to-inserter limit; only clearly-broken links are re-bound.</summary>
        private const double ValidationReachMeters = 5.5;
        /// <summary>Permissive coverage limits used when validating an existing link.</summary>
        private const double ValidationStartTolerance = 1.5;
        private const double ValidationEndTolerance = 2.5;
        private const double PerpendicularYawTolerance = 15.0;
        private const double SpanStartTolerance = 0.60;
        private const double SpanEndTolerance = 0.75;

        internal sealed class Report
        {
            internal int Inserters, Ends, EmptyBefore, Rebond, Unresolved, Verified, MachineEnds;
            public override string ToString() =>
                $"分拣器 {Inserters} 个，端点 {Ends} 个：原有空端 {EmptyBefore}，机器端 {MachineEnds}（不改动），重新绑定 {Rebond}，无可绑定 {Unresolved}，原连接校验通过 {Verified}";
        }

        internal static bool IsBeltItem(int itemId) => itemId >= 2001 && itemId <= 2009;
        internal static bool IsInserterItem(int itemId) => itemId > 2010 && itemId < 2020;

        internal static Report Repair(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt,
            Func<BlueprintBuilding, BlueprintBuilding, double> meters)
        {
            var report = new Report();
            if (buildings == null)
            {
                return report;
            }
            var beltSet = new HashSet<BlueprintBuilding>(buildings.Where(isBelt));
            var all = new HashSet<BlueprintBuilding>(buildings);
            var belts = beltSet.ToArray();
            if (belts.Length == 0)
            {
                return report;
            }

            foreach (var inserter in buildings)
            {
                if (!IsInserterItem(inserter.itemId))
                {
                    continue;
                }
                report.Inserters++;
                bool wasEmptyInput = inserter.inputObj == null;
                bool wasEmptyOutput = inserter.outputObj == null;
                report.EmptyBefore += (wasEmptyInput ? 1 : 0) + (wasEmptyOutput ? 1 : 0);

                report.Ends += 2;
                inserter.inputObj = Resolve(inserter, inserter.inputObj, true, belts, all, isBelt, meters, report);
                inserter.outputObj = Resolve(inserter, inserter.outputObj, false, belts, all, isBelt, meters, report);
            }
            return report;
        }

        private static BlueprintBuilding Resolve(BlueprintBuilding inserter, BlueprintBuilding current, bool isInput,
            BlueprintBuilding[] belts, HashSet<BlueprintBuilding> all, Func<BlueprintBuilding, bool> isBelt,
            Func<BlueprintBuilding, BlueprintBuilding, double> meters, Report report)
        {
            if (current != null && all.Contains(current) && !isBelt(current))
            {
                // The other end of an inserter normally serves a machine; never re-point it at a belt.
                report.MachineEnds++;
                return current;
            }
            if (current != null && all.Contains(current) && isBelt(current) && IsCompatible(inserter, current, isBelt, meters))
            {
                report.Verified++;
                return current;
            }

            BlueprintBuilding best = null;
            double bestScore = double.MaxValue;
            foreach (var candidate in belts)
            {
                if (ReferenceEquals(candidate, current))
                {
                    continue;
                }
                double score = ScoreFor(inserter, candidate, isInput, isBelt);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (best == null)
            {
                // Relaxed second pass: keep the same line-shaped constraints but widen them, so an
                // end is repaired whenever a plausible belt exists rather than left dangling.
                foreach (var candidate in belts)
                {
                    if (ReferenceEquals(candidate, current)) continue;
                    double score = ScoreFor(inserter, candidate, isInput, isBelt, relaxed: true);
                    if (score < bestScore) { bestScore = score; best = candidate; }
                }
            }
            if (best != null)
            {
                report.Rebond++;
                return best;
            }
            report.Unresolved++;
            return current != null && all.Contains(current) ? current : null;
        }

        /// <summary>
        /// Candidate score in CELL space, which is what the game itself measures. The game rejects an
        /// inserter end whose object distance squared exceeds 6 (BlueprintUtils/BuildTool matching),
        /// i.e. about 2.449 cells; measured on a game-verified blueprint the attachment point of a
        /// valid link lies within that radius (pick ends sit on the belt line, place ends reach out
        /// sideways). The inset/output difference is a preference only: the target node may sit
        /// upstream or downstream of the inserter (both bearings occur in game-verified data).
        /// </summary>
        private static double ScoreFor(BlueprintBuilding inserter, BlueprintBuilding belt, bool isInput,
            Func<BlueprintBuilding, bool> isBelt, bool relaxed = false)
        {
            // Gate 1: the game rebuilds a missing target from the belt PATH - PlanetFactory connects an
            // inserter only when a path point lies within sqrt(2) ~ 1.41 m of the inserter body (pick)
            // or of the arm end (insert). A candidate whose path never enters that window can never be
            // re-connected in game, no matter how the stored link looks.
            double window = WindowDistance(inserter, belt, isInput, isBelt);
            if (window > (relaxed ? 2.4 : 1.7))
            {
                return double.MaxValue;
            }
            var span = SpanOf(belt, isBelt);
            if (span == null)
            {
                double endCells = CellDistance(inserter, belt);
                return endCells <= (relaxed ? 2.4 : 1.6) ? endCells + 5.0 + window : double.MaxValue;
            }
            double t = ProjectionT(inserter, span.Value.head, span.Value.tail);
            if (t < -0.75 || t > 1.75)
            {
                return double.MaxValue;
            }
            double perpendicular = PerpendicularCells(inserter, span.Value.head, span.Value.tail);
            double nodeCells = CellDistance(inserter, belt);
            double attachment = Math.Sqrt(perpendicular * perpendicular + Math.Pow(OutsideCells(t, span.Value), 2));
            double limit = relaxed ? 3.0 : 2.4;
            if (attachment > limit || nodeCells > limit + 1.0)
            {
                return double.MaxValue;
            }
            double beltRelative = Math.Abs(AngleDelta(belt.yaw, inserter.yaw));
            double perpendicularity = Math.Abs(beltRelative - 90.0);
            if (perpendicularity > (relaxed ? 55.0 : 40.0))
            {
                return double.MaxValue;
            }
            if (isInput)
            {
                // Prefer the line the inserter stands on (pick ends sit on the belt).
                return window + attachment * 2.0 + perpendicular * 3.0 + nodeCells;
            }
            // Prefer a sideways reach at a comfortable distance (place ends).
            return window + Math.Abs(attachment - 1.4) + perpendicularity * 0.05 + attachment * 0.2;
        }

        /// <summary>
        /// Metres from the inserter's pick point (its body) or place point (its arm end, which reaches
        /// sideways by about one grid) to the belt's local path. Double.MaxValue when the belt has no
        /// usable span at all.
        /// </summary>
        private static double WindowDistance(BlueprintBuilding inserter, BlueprintBuilding belt, bool isInput,
            Func<BlueprintBuilding, bool> isBelt)
        {
            var candidates = new List<(BlueprintBuilding head, BlueprintBuilding tail)>();
            if (belt.outputObj != null && isBelt(belt.outputObj) && !ReferenceEquals(belt.outputObj, belt))
            {
                candidates.Add((belt, belt.outputObj));
            }
            if (belt.inputObj != null && isBelt(belt.inputObj))
            {
                var prevTail = belt;
                candidates.Add((belt.inputObj, prevTail));
            }
            if (candidates.Count == 0)
            {
                return CellDistance(inserter, belt) * 1.2566;
            }
            var points = new List<(double x, double y)> { (inserter.localOffset_x, inserter.localOffset_y) };
            if (!isInput)
            {
                // The arm end sits roughly one grid to either side of the inserter.
                double yawRad = inserter.yaw * Math.PI / 180.0;
                double px = -Math.Sin(yawRad);
                double py = Math.Cos(yawRad);
                points.Add((inserter.localOffset_x + px * 1.3, inserter.localOffset_y + py * 1.3));
                points.Add((inserter.localOffset_x - px * 1.3, inserter.localOffset_y - py * 1.3));
            }
            double best = double.MaxValue;
            foreach (var span in candidates)
            {
                foreach (var point in points)
                {
                    double distance = PointToSegmentCells(point.x, point.y, span.head, span.tail) * 1.2566;
                    if (distance < best)
                    {
                        best = distance;
                    }
                }
            }
            return best;
        }

        private static double PointToSegmentCells(double px, double py, BlueprintBuilding head, BlueprintBuilding tail)
        {
            double dx = tail.localOffset_x - head.localOffset_x;
            double dy = tail.localOffset_y - head.localOffset_y;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared < 1e-9 ? 0.0 : ((px - head.localOffset_x) * dx + (py - head.localOffset_y) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double qx = head.localOffset_x + dx * t;
            double qy = head.localOffset_y + dy * t;
            return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }

        private static double OutsideCells(double t, (BlueprintBuilding head, BlueprintBuilding tail) span)
        {
            if (t < 0)
            {
                return -t * CellDistance(span.head, span.tail);
            }
            if (t > 1)
            {
                return (t - 1) * CellDistance(span.head, span.tail);
            }
            return 0.0;
        }

        private static double CellDistance(BlueprintBuilding a, BlueprintBuilding b)
        {
            double dx = b.localOffset_x - a.localOffset_x;
            double dy = b.localOffset_y - a.localOffset_y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static (BlueprintBuilding head, BlueprintBuilding tail)? SpanOf(BlueprintBuilding belt, Func<BlueprintBuilding, bool> isBelt)
        {
            var tail = belt.outputObj != null && isBelt(belt.outputObj) ? belt.outputObj : null;
            if (tail == null || ReferenceEquals(tail, belt))
            {
                return null;
            }
            return (belt, tail);
        }

        private static double PerpendicularCells(BlueprintBuilding node, BlueprintBuilding head, BlueprintBuilding tail)
        {
            double dx = tail.localOffset_x - head.localOffset_x;
            double dy = tail.localOffset_y - head.localOffset_y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6)
            {
                return 0.0;
            }
            double rx = node.localOffset_x - head.localOffset_x;
            double ry = node.localOffset_y - head.localOffset_y;
            return Math.Abs(rx * (dy / length) - ry * (dx / length));
        }

        private static double ProjectionT(BlueprintBuilding node, BlueprintBuilding head, BlueprintBuilding tail)
        {
            double dx = tail.localOffset_x - head.localOffset_x;
            double dy = tail.localOffset_y - head.localOffset_y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9)
            {
                return 0.0;
            }
            return ((node.localOffset_x - head.localOffset_x) * dx + (node.localOffset_y - head.localOffset_y) * dy) / lengthSquared;
        }

        private static double AngleDelta(double a, double b)
        {
            double delta = (a - b) % 360.0;
            if (delta > 180.0)
            {
                delta -= 360.0;
            }
            if (delta < -180.0)
            {
                delta += 360.0;
            }
            return delta;
        }

        /// <summary>
        /// Permissive validation of an existing link: only links the game itself would refuse are
        /// rejected (undefined target, reach beyond the game's limit, or a target span that does not
        /// pass the inserter at all). Being stricter here would re-point links the game accepts.
        /// </summary>
        private static bool IsCompatible(BlueprintBuilding inserter, BlueprintBuilding belt, Func<BlueprintBuilding, bool> isBelt,
            Func<BlueprintBuilding, BlueprintBuilding, double> meters)
        {
            double distance = meters(inserter, belt);
            if (double.IsNaN(distance) || distance > ValidationReachMeters)
            {
                return false;
            }
            return CoverageWithin(inserter, belt, isBelt, ValidationStartTolerance, ValidationEndTolerance) < double.MaxValue;
        }

        /// <summary>Preference only: the belt usually runs across the inserter (90 deg).</summary>
        private static double YawPenalty(float inserterYaw, float beltYaw)
        {
            double delta = Math.Abs(inserterYaw - beltYaw) % 360.0;
            if (delta > 180.0)
            {
                delta = 360.0 - delta;
            }
            double perpendicular = Math.Abs(delta - 90.0);
            if (perpendicular <= PerpendicularYawTolerance)
            {
                return perpendicular * 0.5;
            }
            return 10.0 + Math.Min(perpendicular, 90.0) * 0.1;
        }

        /// <summary>How well the belt's span covers the inserter; MaxValue when nothing covers it.</summary>
        private static double Coverage(BlueprintBuilding inserter, BlueprintBuilding belt, Func<BlueprintBuilding, bool> isBelt) =>
            CoverageWithin(inserter, belt, isBelt, SpanStartTolerance, SpanEndTolerance);

        private static double CoverageWithin(BlueprintBuilding inserter, BlueprintBuilding belt, Func<BlueprintBuilding, bool> isBelt,
            double startTolerance, double endTolerance)
        {
            double best = double.MaxValue;
            foreach (var head in new[] { belt, belt.inputObj })
            {
                if (head == null || !IsBeltItem(head.itemId))
                {
                    continue;
                }
                var tail = head.outputObj != null && IsBeltItem(head.outputObj.itemId) ? head.outputObj : null;
                if (tail == null || ReferenceEquals(head, tail))
                {
                    continue;
                }
                double dx = tail.localOffset_x - head.localOffset_x;
                double dy = tail.localOffset_y - head.localOffset_y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared < 1e-9)
                {
                    continue;
                }
                double t = ((inserter.localOffset_x - head.localOffset_x) * dx + (inserter.localOffset_y - head.localOffset_y) * dy) / lengthSquared;
                if (t < -startTolerance || t > 1.0 + endTolerance)
                {
                    continue;
                }
                double overshoot = t < 0 ? -t : (t > 1 ? t - 1 : 0);
                if (overshoot < best)
                {
                    best = overshoot;
                }
            }
            if (best < double.MaxValue)
            {
                return best * 100.0;
            }
            // A belt with no successor can still be the end of a line; accept it on distance alone.
            return belt.outputObj == null || !IsBeltItem(belt.outputObj.itemId) ? 50.0 : double.MaxValue;
        }
    }
}
