using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// F1: hand-drawn longer belts.
    ///
    /// Code base: the clean LongerBelts 1.5.1 source tree (the copy source named by the
    /// plan), because the 1.5.3 build only ships as an obfuscated-IL decompilation whose
    /// transcriber artefacts do not compile. Spacing constants come from 1.5.3, as the plan
    /// requires, and two 1.5.3 corrections were carried over:
    ///   * Archimedean mode now scales the first snap to the real start radius;
    ///   * grid-line mode has the 1.5.3 pole guard.
    /// The 1.5.3-only `ClampMaxDistance` / `DistanceUnits` options are deliberately absent,
    /// because the plan's configuration table does not list them (1.5.3 defaults them to
    /// off, so default behaviour is unchanged).
    /// </summary>
    internal static class LongerBeltPath
    {
        internal const float MinimumDistance = 0.400001f;
        internal const float MaximumDistance = 2.301172f;
        // 004.18：格数步长（MinimumGridDistance/MaximumGridDistance）随格数分段一并退役。

        internal static bool longerOnGrid;
        // 004.18：经纬线分段已改为按实际米制长度均匀分段（用户定稿），格数步长参数退役。
        internal static float currentDistance = 1.9f;
        internal static int isometricSegmentation;
        internal static bool shortestUnlimit;

        /// <summary>
        /// 托管版 slerp（与 Unity Vector3.Slerp 语义一致：方向球面插值、模长线性插值）。
        /// 不用 Vector3.Slerp 的原因：它是引擎 internal call，离线校验台（net8.0 进程）无法执行，
        /// 拉带生成就无法做米制托底的离线断言；共线/反共线边界这里显式走线性混合，比引擎实现更稳。
        /// </summary>
        private static Vector3 SlerpStable(Vector3 a, Vector3 b, float t)
        {
            float lengthA = a.magnitude, lengthB = b.magnitude;
            if (lengthA < 1e-8f || lengthB < 1e-8f)
            {
                return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
            }
            var na = new Vector3(a.x / lengthA, a.y / lengthA, a.z / lengthA);
            var nb = new Vector3(b.x / lengthB, b.y / lengthB, b.z / lengthB);
            float dot = Mathf.Clamp(Vector3.Dot(na, nb), -1f, 1f);
            float theta = Mathf.Acos(dot);
            float scaleA, scaleB;
            if (theta < 1e-4f)
            {
                scaleA = 1f - t;
                scaleB = t;
            }
            else
            {
                float sin = Mathf.Sin(theta);
                scaleA = Mathf.Sin((1f - t) * theta) / sin;
                scaleB = Mathf.Sin(t * theta) / sin;
            }
            var mixed = na * scaleA + nb * scaleB;
            float mixedMagnitude = mixed.magnitude;
            if (mixedMagnitude < 1e-8f)
            {
                mixed = na;
                mixedMagnitude = 1f;
            }
            float length = lengthA + (lengthB - lengthA) * t;
            return new Vector3(mixed.x / mixedMagnitude * length, mixed.y / mixedMagnitude * length, mixed.z / mixedMagnitude * length);
        }

        /// <summary>
        /// 米制弦长托底（用户 2026-11-18 定稿）：格数只是期望步长，拉带交付前把每段实际弦长
        /// 钳到面板「最远距离」与游戏硬上限常量中的较小者——超限段在球面上 slerp 加密，
        /// 确保拉出的带子保存成蓝图后在原版粘贴判据（位移平方 &gt; 5.3）下一定不报「距离太远」。
        /// 不加额外余量：托底量的是本星球当前高度的真实弦长（无省带的跨姿态包络问题），
        /// 而 2.301172 &lt; √5.3 ≈ 2.301776（平方 5.2954 &lt; 5.3）本身严格合法，顶格设置不应被加密
        /// （2026-11-18 用户实测：×0.98 会把顶格测地线拉带全部加密、节点翻倍，长带效果消失）。
        /// 弱约束间距（shortestUnlimit，慎用通道）开启时保持原行为不做钳制。
        /// </summary>
        private static float MetreCap()
            => Mathf.Clamp(Mathf.Min(currentDistance, MaximumDistance), MinimumDistance, MaximumDistance);

        private static Vector3[] _metreCapBuffer;

        /// <summary>radiusScale：snaps 存单位向量时传真实半径（含高度），已带半径时传 1。</summary>
        private static int EnforceMetreCap(Vector3[] snaps, int count, float radiusScale, int limit)
        {
            if (shortestUnlimit || count < 2)
            {
                return count;
            }
            float cap = MetreCap();
            int extra = 0;
            for (int i = 0; i < count - 1; i++)
            {
                float chord = Vector3.Distance(snaps[i], snaps[i + 1]) * radiusScale;
                if (chord > cap)
                {
                    extra += Mathf.CeilToInt(chord / cap) - 1;
                }
            }
            if (extra == 0)
            {
                return count;
            }
            if (_metreCapBuffer == null || _metreCapBuffer.Length < count + extra)
            {
                _metreCapBuffer = new Vector3[Math.Max(count + extra, limit + 16)];
            }
            int write = 0;
            _metreCapBuffer[write++] = snaps[0];
            for (int i = 1; i < count; i++)
            {
                float chord = Vector3.Distance(snaps[i - 1], snaps[i]) * radiusScale;
                int pieces = chord > cap ? Mathf.CeilToInt(chord / cap) : 1;
                for (int k = 1; k < pieces; k++)
                {
                    // Slerp keeps unit snaps unit and radius-carrying snaps on the same radial blend.
                    float t = (float)k / pieces;
                    _metreCapBuffer[write++] = SlerpStable(snaps[i - 1], snaps[i], t);
                }
                _metreCapBuffer[write++] = snaps[i];
            }
            int final = Mathf.Min(write, limit);
            Array.Copy(_metreCapBuffer, snaps, final);
            return final;
        }

        internal static int LongerSnapLineNonAlloc(PlanetGrid planetGrid, Vector3 begin, Vector3 end, int path, Vector3[] snaps, out int swerveIndex)
        {
            if (!longerOnGrid)
            {
                return planetGrid.SnapLineNonAlloc(begin, end, path, snaps, out swerveIndex);
            }

            swerveIndex = 0;
            int limit = snaps.Length - 10;
            if (limit <= 0)
            {
                return 0;
            }

            float radiusScale = begin.magnitude; // real radius incl. altitude, before Normalize
            begin.Normalize();
            end.Normalize();
            float beginLatitude = Mathf.Asin(begin.y);
            float beginLongitude = Mathf.Atan2(begin.x, 0f - begin.z);
            float endLatitude = Mathf.Asin(end.y);
            float endLongitude = Mathf.Atan2(end.x, 0f - end.z);

            // 1.5.3 pole guard: within one grid cell of a pole longitude is degenerate.
            float beginPoleRadius = (begin.x * begin.x + begin.z * begin.z) * planetGrid.segment * planetGrid.segment;
            float endPoleRadius = (end.x * end.x + end.z * end.z) * planetGrid.segment * planetGrid.segment;
            if (beginPoleRadius < 1f && endPoleRadius < 1f && begin.y * end.y >= 0f)
            {
                snaps[0] = new Vector3(0f, begin.y >= 0f ? 1f : -1f, 0f);
                return 1;
            }
            if (beginPoleRadius < 1f && endPoleRadius < 1f)
            {
                beginLongitude = endLongitude = 0f;
                beginLatitude = Mathf.Sign(beginLatitude) * (float)(Math.PI / 2.0);
                endLatitude = Mathf.Sign(endLatitude) * (float)(Math.PI / 2.0);
            }
            else if (beginPoleRadius < 1f)
            {
                beginLongitude = endLongitude;
                beginLatitude = Mathf.Sign(beginLatitude) * (float)(Math.PI / 2.0);
            }
            else if (endPoleRadius < 1f)
            {
                endLongitude = beginLongitude;
                endLatitude = Mathf.Sign(endLatitude) * (float)(Math.PI / 2.0);
            }

            float deltaLongitude = Mathf.Repeat(endLongitude - beginLongitude, 6.283185f);
            float higherLatitude = Mathf.Max(Mathf.Abs(beginLatitude), Mathf.Abs(endLatitude));
            Vector3 beginNormalized = begin.normalized;
            Vector3 endNormalized = end.normalized;
            int count = 0;
            snaps[count++] = beginNormalized;
            if (deltaLongitude > 3.1415927f)
            {
                deltaLongitude -= 6.283185f;
            }

            if (path == 1)
            {
                // Latitude leg first, then the longitude leg.
                // 004.18（用户定稿）：格数步长退役，两腿均按实际米制弦长均匀分段
                // （段长 ≤ MetreCap()，段数最少 = 最省带，节点不再吸附格点）。
                float segmentCount = PlanetGrid.DetermineLongitudeSegmentCount(
                    Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(beginLatitude / 6.283185f * planetGrid.segment) - 0.1f)),
                    planetGrid.segment);
                if ((Mathf.Abs(deltaLongitude) - 1.57079637f) * segmentCount > (1.57079637f - higherLatitude) * 200f)
                {
                    deltaLongitude += deltaLongitude > 0f ? -3.141593f : 3.141593f;
                    endLatitude = endLatitude < 0f ? -3.141593f - endLatitude : 3.141593f - endLatitude;
                }
                float targetLongitude = beginLongitude + deltaLongitude;
                float deltaLatitude = endLatitude - beginLatitude;
                float cap = MetreCap();
                float cosLatitude = Mathf.Cos(beginLatitude);
                var beginUnit = new Vector3(
                    cosLatitude * Mathf.Sin(beginLongitude),
                    Mathf.Sin(beginLatitude),
                    0f - cosLatitude * Mathf.Cos(beginLongitude));
                var corner = new Vector3(
                    cosLatitude * Mathf.Sin(targetLongitude),
                    Mathf.Sin(beginLatitude),
                    0f - cosLatitude * Mathf.Cos(targetLongitude));
                // Longitude leg: stay on the begin latitude (explicit parametrisation; a slerp would
                // drift onto the great circle). Steps from the leg ARC length (r·cosφ·Δλ) — the
                // chord under-counts by ~0.1%, enough to leave every segment marginally over cap.
                float longitudeArc = Mathf.Abs(deltaLongitude) * radiusScale * Mathf.Cos(beginLatitude);
                int longitudeSteps = longitudeArc > 1e-3f ? Mathf.Max(1, Mathf.CeilToInt(longitudeArc / cap)) : 0;
                for (int i = 1; i <= longitudeSteps; i++)
                {
                    if (count >= limit)
                    {
                        break;
                    }
                    float step = deltaLongitude * i / longitudeSteps;
                    snaps[count++] = new Vector3(
                        cosLatitude * Mathf.Sin(beginLongitude + step),
                        Mathf.Sin(beginLatitude),
                        0f - cosLatitude * Mathf.Cos(beginLongitude + step));
                }
                swerveIndex = count;
                if ((endNormalized - corner).magnitude < 0.00251f)
                {
                    return EnforceMetreCap(snaps, count, radiusScale, limit);
                }
                // Latitude leg: along the meridian (great circle), slerp is exact.
                float latitudeArc = Mathf.Abs(deltaLatitude) * radiusScale;
                int latitudeSteps = latitudeArc > 1e-3f ? Mathf.Max(1, Mathf.CeilToInt(latitudeArc / cap)) : 0;
                for (int j = 1; j <= latitudeSteps; j++)
                {
                    if (count >= limit)
                    {
                        break;
                    }
                    snaps[count++] = SlerpStable(corner, endNormalized, (float)j / latitudeSteps);
                }
                return EnforceMetreCap(snaps, count, radiusScale, limit);
            }
            else
            {
                // Longitude leg first, then the latitude leg.
                float segmentCount = PlanetGrid.DetermineLongitudeSegmentCount(
                    Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(endLatitude / 6.283185f * planetGrid.segment) - 0.1f)),
                    planetGrid.segment);
                if ((Mathf.Abs(deltaLongitude) - 1.57079637f) * segmentCount > (1.57079637f - higherLatitude) * 200f)
                {
                    deltaLongitude += deltaLongitude > 0f ? -3.141593f : 3.141593f;
                    endLatitude = endLatitude < 0f ? -3.141593f - endLatitude : 3.141593f - endLatitude;
                }
                float deltaLatitude = endLatitude - beginLatitude;
                float cap = MetreCap();
                float cosLatitude = Mathf.Cos(endLatitude);
                var corner = new Vector3(
                    cosLatitude * Mathf.Sin(beginLongitude),
                    Mathf.Sin(endLatitude),
                    0f - cosLatitude * Mathf.Cos(beginLongitude));
                // Latitude leg: along the end meridian (great circle), slerp is exact.
                float latitudeArc = Mathf.Abs(deltaLatitude) * radiusScale;
                int latitudeSteps = latitudeArc > 1e-3f ? Mathf.Max(1, Mathf.CeilToInt(latitudeArc / cap)) : 0;
                for (int k = 1; k <= latitudeSteps; k++)
                {
                    if (count >= limit)
                    {
                        break;
                    }
                    snaps[count++] = SlerpStable(beginNormalized, corner, (float)k / latitudeSteps);
                }
                swerveIndex = count;
                // Longitude leg: stay on the end latitude (arc length r·cosφ·Δλ, see above).
                float longitudeArc = Mathf.Abs(deltaLongitude) * radiusScale * Mathf.Cos(endLatitude);
                if (longitudeArc <= 1e-3f)
                {
                    return EnforceMetreCap(snaps, count, radiusScale, limit);
                }
                int longitudeSteps = Mathf.Max(1, Mathf.CeilToInt(longitudeArc / cap));
                for (int l = 1; l <= longitudeSteps; l++)
                {
                    if (count >= limit)
                    {
                        break;
                    }
                    float longitude = beginLongitude + (float)l / longitudeSteps * deltaLongitude;
                    snaps[count++] = new Vector3(
                        cosLatitude * Mathf.Sin(longitude),
                        Mathf.Sin(endLatitude),
                        0f - cosLatitude * Mathf.Cos(longitude));
                }
                return EnforceMetreCap(snaps, count, radiusScale, limit);
            }
        }

        internal static bool LongerGeodesic(Vector3 begin, Vector3 end, ref int counts, Vector3[] snaps)
        {
            int maxSnapsLength = snaps.Length - 10;
            float maxRadius = begin.magnitude > end.magnitude ? begin.magnitude : end.magnitude;
            Vector3 beginNormalized = begin.normalized;
            Vector3 endNormalized = end.normalized;
            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(beginNormalized, endNormalized), -1f, 1f));
            float geodesicDistance = angle * maxRadius;
            float radiusScale = begin.magnitude; // unit-snaps exits scale chords by the real radius
            float nodesCount;
            int snapCount;

            if (isometricSegmentation == 1)
            {
                // Archimedean spiral mode: equal-distance nodes including the height delta.
                float beginMagnitude = begin.magnitude;
                float endMagnitude = end.magnitude;
                float deltaHeight = Mathf.Abs(endMagnitude - beginMagnitude);
                float spatialDistance = Mathf.Sqrt(geodesicDistance * geodesicDistance + deltaHeight * deltaHeight);
                nodesCount = spatialDistance / currentDistance;
                if (!shortestUnlimit && spatialDistance / ((int)nodesCount + 1) < MinimumDistance)
                {
                    nodesCount = spatialDistance / MinimumDistance - 1f;
                }
                snapCount = nodesCount > 0.1f ? (int)nodesCount + 1 : 0;
                if (snapCount == 0)
                {
                    snaps[counts++] = beginNormalized;
                    if (Mathf.Abs(beginMagnitude - endMagnitude) > 0.001f)
                    {
                        // 1.5.3 correction: the start node must keep the real start radius.
                        snaps[0] = beginNormalized * beginMagnitude;
                        snaps[counts++] = beginNormalized * endMagnitude;
                        counts = EnforceMetreCap(snaps, counts, 1f, maxSnapsLength);
                        return false;
                    }
                }
                else
                {
                    for (int i = 0; i <= snapCount && counts < maxSnapsLength; i++)
                    {
                        snaps[counts++] = SlerpStable(beginNormalized, endNormalized, (float)i / snapCount);
                    }
                }
                if (counts < 2)
                {
                    snaps[0] *= beginMagnitude;
                    return false;
                }
                for (int j = 0; j < counts; j++)
                {
                    snaps[j] *= beginMagnitude * (counts - 1 - j) / (counts - 1) + endMagnitude * (float)j / (counts - 1);
                }
                counts = EnforceMetreCap(snaps, counts, 1f, maxSnapsLength);
                return false;
            }

            nodesCount = geodesicDistance / currentDistance;
            float minRadius = begin.magnitude < end.magnitude ? begin.magnitude : end.magnitude;
            if (!shortestUnlimit && angle * minRadius / ((int)nodesCount + 1) < MinimumDistance)
            {
                nodesCount = angle * minRadius / MinimumDistance - 1f;
            }
            snapCount = nodesCount > 0.1f ? (int)nodesCount + 1 : 0;
            if (snapCount == 0)
            {
                snaps[counts++] = beginNormalized;
            }
            else
            {
                for (int k = 0; k <= snapCount && counts < maxSnapsLength; k++)
                {
                    snaps[counts++] = SlerpStable(beginNormalized, endNormalized, (float)k / snapCount);
                }
            }
            counts = EnforceMetreCap(snaps, counts, radiusScale, maxSnapsLength);
            return true;
        }

        internal static void SwitchBeltsPath(BuildTool_Path path)
        {
            if (path.pathSuggest > 0)
            {
                path.geodesic = false;
            }
            if (path.pathAlternative == 1)
            {
                path.pathAlternative = 2;
            }
            else
            {
                path.pathAlternative = 1;
                path.geodesic = false;
            }
        }

        internal static void SlantVerticalBelts(Vector3 begin, ref Vector3 end)
        {
            if ((begin.normalized - end.normalized).magnitude < 0.0001f)
            {
                // A fully vertical belt would have an infinite slope; tilt it slightly so the
                // preview stays valid and visually sane (slope < 1000).
                Vector3 littleOffset = new Vector3(0f, 0.001f, 0f);
                if (Mathf.Abs(Vector3.Dot(begin.normalized, littleOffset.normalized)) > 0.99f)
                {
                    littleOffset = new Vector3(0.001f, 0f, 0f);
                }
                littleOffset = Vector3.Cross(end - begin, littleOffset);
                end += littleOffset;
            }
        }

        private static bool IsVector3LocalLoad(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Ldloc_S
                && instruction.opcode != OpCodes.Ldloc
                && instruction.opcode != OpCodes.Ldloca_S
                && instruction.opcode != OpCodes.Ldloca)
            {
                return false;
            }
            var builder = instruction.operand as LocalBuilder;
            if (builder != null)
            {
                return builder.LocalType == typeof(Vector3);
            }
            var info = instruction.operand as LocalVariableInfo;
            return info != null && info.LocalType == typeof(Vector3);
        }

        internal static IEnumerable<CodeInstruction> SnapLineNonAllocTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            try
            {
                var matcher = new CodeMatcher(instructions, generator);
                matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(PlanetGrid), nameof(PlanetGrid.SnapLineNonAlloc))));
                if (matcher.IsInvalid)
                {
                    Log.Warn("F1 transpiler: PlanetGrid.SnapLineNonAlloc call not found in PlanetAuxData.SnapLineNonAlloc; gridline long belts stay disabled.");
                    return instructions;
                }
                matcher.SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(LongerBeltPath), nameof(LongerSnapLineNonAlloc)));
                object snaps = matcher.InstructionAt(-3).operand;
                object jmpBeltGeneration = matcher.InstructionAt(1).operand;
                matcher.Advance(5);
                matcher.CreateLabelAt(matcher.Pos, out Label jmpShorterPath);
                matcher.InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldarg_3),
                    new CodeInstruction(OpCodes.Ldc_I4_2),
                    new CodeInstruction(OpCodes.Bne_Un_S, jmpShorterPath),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Ldloca_S, 0),
                    new CodeInstruction(OpCodes.Ldarg_S, snaps),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LongerBeltPath), nameof(LongerGeodesic))),
                    new CodeInstruction(OpCodes.Brtrue_S, jmpBeltGeneration),
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ret));
                return matcher.InstructionEnumeration();
            }
            catch (Exception ex)
            {
                Log.Warn("F1 transpiler (SnapLineNonAlloc) failed, keeping the original path code: " + ex.Message);
                return instructions;
            }
        }

        internal static IEnumerable<CodeInstruction> DeterminePreviewsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            try
            {
                var matcher = new CodeMatcher(instructions, generator);
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldc_I4_0),
                    new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(BuildTool_Path), nameof(BuildTool_Path.geodesic))));
                if (matcher.IsInvalid)
                {
                    Log.Warn("F1 transpiler: geodesic init pattern not found in BuildTool_Path.DeterminePreviews; path-mode switch is skipped.");
                    return instructions;
                }
                matcher.SetAndAdvance(OpCodes.Ldarg_0, null);
                matcher.SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(LongerBeltPath), nameof(SwitchBeltsPath)));
                matcher.SetAndAdvance(OpCodes.Nop, null);

                matcher.MatchForward(true, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(PlanetAuxData), nameof(PlanetAuxData.SnapLineNonAlloc))));
                if (matcher.IsInvalid)
                {
                    Log.Warn("F1 transpiler: PlanetAuxData.SnapLineNonAlloc call not found in DeterminePreviews; vertical slant is skipped.");
                    return matcher.InstructionEnumeration();
                }

                // Locate the (begin, end) argument pair by pattern instead of by fixed offsets.
                // LongerBelts hardcodes -22/-21, but on the current 0.10.35 assembly the pair sits
                // at -23/-22, so those offsets pick up `end` and the int `path` argument and would
                // emit `ref int` for a `ref Vector3` parameter (invalid IL).
                int endOffset = int.MinValue;
                int beginOffset = int.MinValue;
                for (int offset = -1; offset >= -40; offset--)
                {
                    CodeInstruction candidate = matcher.InstructionAt(offset);
                    if (candidate == null)
                    {
                        break;
                    }
                    if (!IsVector3LocalLoad(candidate))
                    {
                        continue;
                    }
                    if (endOffset == int.MinValue)
                    {
                        endOffset = offset;
                        continue;
                    }
                    beginOffset = offset;
                    break;
                }
                if (beginOffset == int.MinValue || endOffset == int.MinValue)
                {
                    Log.Warn("F1 transpiler: could not locate the begin/end Vector3 locals in DeterminePreviews; vertical slant is skipped.");
                    return matcher.InstructionEnumeration();
                }
                object begin = matcher.InstructionAt(beginOffset).operand;
                object end = matcher.InstructionAt(endOffset).operand;
                // Insert before the begin load so both locals are updated before the call reads them.
                matcher.Advance(beginOffset).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_S, begin),
                    new CodeInstruction(OpCodes.Ldloca_S, end),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LongerBeltPath), nameof(SlantVerticalBelts))));
                return matcher.InstructionEnumeration();
            }
            catch (Exception ex)
            {
                Log.Warn("F1 transpiler (DeterminePreviews) failed, keeping the original preview code: " + ex.Message);
                return instructions;
            }
        }
    }
}
