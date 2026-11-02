using System;
using System.Collections.Generic;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// 004.16 跨纬度结构安全：按纬度带采样建一组模型，链节距离取带内最坏值。
    ///
    /// 为什么需要：单一观测纬度的模型只保证"贴在该纬度"不断链。网格经度缩放随纬度变
    /// （1/cos），赤道下合法的长跨（如 C1.150绿糖 的 1.778 格 ≈ 2.25 m）贴到 40° 会膨胀到
    /// 2.50 m，超过游戏硬上限 → 粘贴期成批丢链接 → 部分并带结构视觉断开
    /// （150绿糖 实测：A_lib 导出在 20°~72° 有 53~129 条越限链节，原件全纬度 0 越限）。
    ///
    /// 带内最坏判定从根上保证"拿到手的长带化蓝图在任何纬度粘贴都不断链"，代价是
    /// 放弃单纬度最优的一部分省带量（有效上限约收缩到全纬度包络）。
    /// </summary>
    internal static class CrossLatitudeSafety
    {
        /// <summary>纬度带采样范围（北半球；网格缩放关于赤道对称，南半球由对称性覆盖）。
        /// 步长 1°：链接长度随锚点纬度是分段函数（节点跨越 5° 网格分段边界时阶跃），
        /// 采样过疏会漏掉阶跃峰（1080 实测 8° 步长在 45° 漏出 12 条越限）。</summary>
        internal const double BandLowDeg = 0.0;
        internal const double BandHighDeg = 80.0;
        internal const double BandStepDeg = 1.0;

        /// <summary>带模式下的附加安全余量：cap × SafetyMargin × BandMargin。
        /// 吸收"锚点采样点之间"的阶梯跳变（分段切换一步可差 ~5%）。</summary>
        internal const double BandMargin = 0.95;

        /// <summary>
        /// 把单一模型的距离函数包装成"纬度带内最坏距离"。<paramref name="primary"/> 为
        /// 按当前观测纬度 Resolve 出的主模型（保持原有语义/日志口径）；
        /// <paramref name="enabled"/> = false 时原样返回单模型距离（旧行为）。
        /// </summary>
        internal static Func<BlueprintBuilding, BlueprintBuilding, double> Wrap(
            IEnumerable<BlueprintArea> areas, BlueprintBuilding[] buildings,
            Func<BlueprintBuilding, bool> isBelt,
            BeltSpacingCalibration.Observation? observation,
            BeltSpacingCalibration.Result primary,
            bool enabled)
        {
            if (!enabled)
            {
                return (a, b) => BeltSpacingCalibration.Distance(a, b, primary);
            }
            var obs = observation ?? BeltSpacingCalibration.Observation.FromYaw(0.0, 0f);
            var models = new List<BeltSpacingCalibration.Result> { primary };
            for (double latDeg = BandLowDeg + BandStepDeg; latDeg <= BandHighDeg + 1e-9; latDeg += BandStepDeg)
            {
                models.Add(BeltSpacingCalibration.Resolve(areas, buildings, isBelt,
                    BeltSpacingCalibration.Observation.FromYaw(latDeg * Math.PI / 180.0, 0f)));
            }
            var arr = models.ToArray();
            return (a, b) =>
            {
                double worst = 0.0;
                for (int i = 0; i < arr.Length; i++)
                {
                    double d = BeltSpacingCalibration.Distance(a, b, arr[i]);
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        continue;
                    }
                    if (d > worst)
                    {
                        worst = d;
                    }
                }
                return worst;
            };
        }
    }
}
