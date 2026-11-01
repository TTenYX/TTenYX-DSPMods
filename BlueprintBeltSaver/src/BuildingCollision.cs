using System;
using System.Collections.Generic;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// 建筑"Build 碰撞盒"与传送带节点的碰撞判定（004.11）。
    ///
    /// 数据来源：游戏 prefabDesc.colliders 的 Build 组（F9 导出 CSV，254 个建筑 / 527 个盒），
    /// 由 BuildingColliders.cs 内嵌。坐标换算：
    ///   · x/z：1 蓝图格 = 1.2566370614 m（与传送带赤道格长一致）；
    ///   · y：1 层 = 1.3333333 m。
    /// 判定对象与游戏粘贴一致：带节点球（半径 0.23 m，球心沿法线上浮 0.2 m）。
    ///
    /// 为规避建筑 yaw 方向约定不明，盒子的水平范围取"绕 yaw 旋转后的 AABB"再保守处理：
    ///   ex' = |cx| + ex·|cos| + ez·|sin|，ez' = |cz| + ex·|sin| + ez·|cos|，盒心归到建筑原点。
    /// 对中置对称盒（制造台/熔炉等）是精确的；对偏置盒（喷涂机/物流站）只会偏保守。
    /// </summary>
    internal static class BuildingCollision
    {
        private const double MetresPerGrid = 1.2566370614359173;
        private const double MetresPerLevel = 1.3333333333333333;
        /// <summary>带节点球半径（米）与球心上浮（米），来自 BuildTool_BlueprintPaste 的反编译。</summary>
        private const double NodeRadiusMetres = 0.23;
        private const double NodeLiftMetres = 0.20;

        private static readonly float NodeRadiusXZ = (float)(NodeRadiusMetres / MetresPerGrid);
        private static readonly float NodeRadiusZ = (float)(NodeRadiusMetres / MetresPerLevel);
        private static readonly float NodeLiftZ = (float)(NodeLiftMetres / MetresPerLevel);

        /// <summary>世界（蓝图局部）坐标下的一个 AABB 碰撞盒。</summary>
        internal struct WorldBox
        {
            internal float cx, cy, cz, ex, ey, ez;
        }

        /// <summary>把建筑的内嵌 Build 碰撞盒转换为蓝图局部 AABB；没有数据时返回 false。</summary>
        internal static bool TryGetWorldBoxes(BlueprintBuilding building, List<WorldBox> output)
        {
            if (building == null || !BuildingColliders.TryGet(building.itemId, out var boxes) || boxes == null)
            {
                return false;
            }
            double yaw = building.yaw * Math.PI / 180.0;
            double c = Math.Abs(Math.Cos(yaw));
            double s = Math.Abs(Math.Sin(yaw));
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                var world = new WorldBox
                {
                    cx = building.localOffset_x,
                    cz = building.localOffset_y,
                    cy = building.localOffset_z + box.cy,
                    ex = (float)(Math.Abs(box.cx) + box.ex * c + box.ez * s),
                    ez = (float)(Math.Abs(box.cz) + box.ex * s + box.ez * c),
                    ey = box.ey
                };
                output.Add(world);
            }
            return true;
        }

        /// <summary>带节点球是否与任一碰撞盒相交。</summary>
        internal static bool Hits(BlueprintBuilding node, List<WorldBox> boxes)
        {
            if (node == null || boxes == null || boxes.Count == 0)
            {
                return false;
            }
            float x = node.localOffset_x;
            float y = node.localOffset_y;
            float z = node.localOffset_z + NodeLiftZ;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (Math.Abs(x - b.cx) > b.ex + NodeRadiusXZ) continue;
                if (Math.Abs(y - b.cz) > b.ez + NodeRadiusXZ) continue;
                if (z + NodeRadiusZ < b.cy - b.ey || z - NodeRadiusZ > b.cy + b.ey) continue;
                return true;
            }
            return false;
        }
    }
}
