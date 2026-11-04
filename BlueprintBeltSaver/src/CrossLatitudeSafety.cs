using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// 跨纬度结构安全（004.16 引入，004.17 重写为"全合法粘贴姿态包络"）。
    ///
    /// 为什么需要：长带化产物若只按单一观测纬度建模，换纬度/旋转粘贴后长跨会超过游戏硬上限
    /// （BuildTool_BlueprintPaste.cs:2087 sqrMagnitude &gt; 5.3 →「距离太远」），粘贴期成批丢链接，
    /// 部分并带结构视觉断开（150绿糖 玩家实测）。
    ///
    /// 004.17：距离取 <see cref="PasteEnvelope"/>——游戏判定合法的全部粘贴姿态（偏航 × 光标纬度，
    /// 合法性直接调游戏 GenerateAreaGratBoxByBPData）中、原件能干净粘贴的那些姿态上的最坏值，
    /// 几何逐字复刻 RefreshBuildPreview。它同时修掉了 004.16 采样模型的两个漏洞（经度格宽按节点
    /// 纬度查表、未覆盖 90°/270° 旋转），又去掉了"采样到游戏不允许的高纬姿态"带来的过度保守，
    /// 因此不再需要 BandMargin 余量。
    /// </summary>
    internal static class CrossLatitudeSafety
    {
        /// <summary>
        /// 004.16 采样模型的附加余量；004.17 包络无采样缝隙，余量退役（恒 1.0，保留常量
        /// 供离线工具与旧调用点编译）。
        /// </summary>
        internal const double BandMargin = 1.0;

        /// <summary>按合并前蓝图建立粘贴包络（primaryAreaIdx &lt; 0 时取根区域）。</summary>
        internal static PasteEnvelope Envelope(IEnumerable<BlueprintArea> areas, int primaryAreaIdx,
            IList<BlueprintBuilding> buildings, Func<BlueprintBuilding, bool> isBelt)
            => PasteEnvelope.Build(areas?.ToArray(), primaryAreaIdx, buildings, isBelt);

        /// <summary>兼容旧签名（离线工具）：主区域由区域链推出。</summary>
        internal static Func<BlueprintBuilding, BlueprintBuilding, double> Wrap(
            IEnumerable<BlueprintArea> areas, BlueprintBuilding[] buildings,
            Func<BlueprintBuilding, bool> isBelt,
            BeltSpacingCalibration.Observation? observation,
            BeltSpacingCalibration.Result primary,
            bool enabled)
            => Wrap(Envelope(areas, -1, buildings, isBelt), observation, enabled);

        /// <summary>
        /// 由包络给出距离函数。<paramref name="enabled"/> = true（默认）：保护集内最坏值，产物在
        /// 任意合法纬度/朝向粘贴都不越限。false：单姿态精确模型——本次观测姿态（非法时吸附到
        /// 最近的保护集姿态），只保证在该位置粘贴（"同纬度工作流"省带优先）。
        /// 找不到任何合法姿态时返回恒 +∞（不做任何合并，fail-soft）。
        /// </summary>
        internal static Func<BlueprintBuilding, BlueprintBuilding, double> Wrap(PasteEnvelope envelope,
            BeltSpacingCalibration.Observation? observation, bool enabled)
        {
            if (envelope == null || envelope.Empty)
            {
                return (a, b) => double.PositiveInfinity;
            }
            if (!enabled)
            {
                var frame = envelope.Nearest(observation ?? BeltSpacingCalibration.Observation.FromYaw(0.0, 0f));
                if (frame == null)
                {
                    return (a, b) => double.PositiveInfinity;
                }
                return (a, b) => PasteEnvelope.DistanceAt(frame, a, b);
            }
            return envelope.Distance;
        }
    }
}
