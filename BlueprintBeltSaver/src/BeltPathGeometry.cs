using System;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// The belt path model the game uses. A belt piece (node -> next node) is not the straight chord
    /// between its nodes: the game draws a Bezier through the node and its neighbours, taking a factor
    /// between 0.552 and 0.667 (depending on the angles at the ends) as the basis for the front/middle/
    /// back key points, and rebuilds buffer positions from that curve.
    ///
    /// This matters for inserters: the connection is rebuilt from the PATH, so a piece that "looks"
    /// 1.4 m away when measured against the chord can still pass right by the inserter once the curve is
    /// taken into account - and vice versa. Measuring against the chord is what made an earlier analysis
    /// over-predict dead ends by more than an order of magnitude.
    /// </summary>
    internal static class BeltPathGeometry
    {
        private const int Samples = 16;
        private const double ControlFactor = 0.6;   // inside the game's 0.552 .. 0.667 range

        /// <summary>Shortest distance (in cells) from a point to the belt piece's path.</summary>
        internal static double DistanceToPiece(double px, double py, BlueprintBuilding head, BlueprintBuilding tail,
            Func<BlueprintBuilding, bool> isBelt)
        {
            double ax = head.localOffset_x, ay = head.localOffset_y;
            double bx = tail.localOffset_x, by = tail.localOffset_y;
            double dx = bx - ax, dy = by - ay;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6)
            {
                return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            }

            // Tangents from the neighbouring nodes, falling back to the chord for line ends.
            double tanAx = dx, tanAy = dy;
            if (head.inputObj != null && isBelt(head.inputObj))
            {
                tanAx = ax - head.inputObj.localOffset_x;
                tanAy = ay - head.inputObj.localOffset_y;
            }
            double tanBx = dx, tanBy = dy;
            if (tail.outputObj != null && isBelt(tail.outputObj))
            {
                tanBx = tail.outputObj.localOffset_x - bx;
                tanBy = tail.outputObj.localOffset_y - by;
            }
            Normalize(ref tanAx, ref tanAy, dx, dy);
            Normalize(ref tanBx, ref tanBy, dx, dy);

            // Cubic Bezier: P0 = head, P1 = head + tA * f * L, P2 = tail - tB * f * L, P3 = tail.
            double p1x = ax + tanAx * ControlFactor * length, p1y = ay + tanAy * ControlFactor * length;
            double p2x = bx - tanBx * ControlFactor * length, p2y = by - tanBy * ControlFactor * length;

            double best = double.MaxValue;
            double prevX = ax, prevY = ay;
            for (int i = 1; i <= Samples; i++)
            {
                double t = (double)i / Samples;
                double mt = 1.0 - t;
                double x = mt * mt * mt * ax + 3 * mt * mt * t * p1x + 3 * mt * t * t * p2x + t * t * t * bx;
                double y = mt * mt * mt * ay + 3 * mt * mt * t * p1y + 3 * mt * t * t * p2y + t * t * t * by;
                double d = PointToSegment(px, py, prevX, prevY, x, y);
                if (d < best)
                {
                    best = d;
                }
                prevX = x;
                prevY = y;
            }
            return best;
        }

        private static void Normalize(ref double tx, ref double ty, double fallbackX, double fallbackY)
        {
            double length = Math.Sqrt(tx * tx + ty * ty);
            if (length < 1e-6)
            {
                tx = fallbackX;
                ty = fallbackY;
                length = Math.Sqrt(tx * tx + ty * ty);
                if (length < 1e-6)
                {
                    tx = 1;
                    ty = 0;
                    return;
                }
            }
            tx /= length;
            ty /= length;
        }

        private static double PointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared < 1e-12 ? 0.0 : ((px - ax) * dx + (py - ay) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double qx = ax + dx * t, qy = ay + dy * t;
            return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }
    }
}
