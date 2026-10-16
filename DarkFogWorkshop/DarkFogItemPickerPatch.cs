using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 让黑雾 6 种掉落物出现在「选择物品」界面（UIItemPicker）里，
    /// 且位置与正常获取时的物品目录格位一致。
    ///
    /// <para><b>原版为什么看不到</b>：<c>UIItemPicker.RefreshIcons</c> 有三道门槛 ——
    /// GridIndex ≥ 1101、<c>history.ItemUnlocked</c>、且物品所在网格页必须等于当前页签
    /// （选择窗只有第 1/2 两个页签）。六种黑雾材料的 GridIndex 是
    /// <b>5801–5806</b>（第 5 页·最后一行的前 6 格），第 5 页没有页签，
    /// 且在击杀黑雾前它们也不算已解锁，所以原版永远选不到。</para>
    ///
    /// <para><b>本补丁</b>：在 RefreshIcons 之后，从六种材料的<b>自然格位起点</b>
    /// （能量碎片 5801 → 网格槽位 98，即最后一行第 1 列；六种占 98–103）
    /// 开始、跳过已占用格顺次补写。这样：</para>
    /// <list type="bullet">
    /// <item>不与其它 MOD 冲突：若该排格位被占（例如 OrbitalRing 把矩阵移到了这一排），
    /// 就顺着往右找空格，与已有物品并排；</item>
    /// <item>只补「物品」页签（currentType == 1），不混进「建筑」页签；</item>
    /// <item>悬停提示与点击选择都读取 protoArray，无需其它改动。</item>
    /// </list>
    ///
    /// <para>采用 <see cref="Install"/> 手动注册并全程 try/catch：
    /// 这只是辅助性界面功能，游戏更新若导致目标缺失，只记警告跳过，
    /// 绝不能让整个插件加载失败。</para>
    /// </summary>
    internal static class DarkFogItemPickerPatch
    {
        /// <summary>黑雾 6 种掉落物（与 DarkFogWorkshopPatch 中的常量保持一致）。</summary>
        private static readonly int[] DarkFogItemIds =
        {
            5201, // 黑雾矩阵
            5202, // 硅基神经元
            5203, // 物质重组器
            5204, // 负熵奇点
            5205, // 核心素
            5206, // 能量碎片
        };

        /// <summary>选择窗网格：14 列 × 8 行 = 112 个可见格位。</summary>
        private const int GridCols = 14;
        private const int GridRows = 8;
        private const int GridSlots = GridCols * GridRows;

        private static bool _diagLogged;

        internal static void Install(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(UIItemPicker), "RefreshIcons");
                MethodInfo postfix = AccessTools.Method(typeof(DarkFogItemPickerPatch), nameof(RefreshIcons_Postfix));
                if (original == null || postfix == null)
                {
                    DarkFogWorkshop.Log?.LogWarning("[选物品] 未找到 UIItemPicker.RefreshIcons，黑雾材料不会出现在选择物品界面。");
                    return;
                }
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                DarkFogWorkshop.Log?.LogWarning($"[选物品] 补丁注册失败（不影响其它功能）：{e.Message}");
            }
        }

        /// <summary>按游戏规则把 GridIndex 换算成网格槽位；越界返回 -1。</summary>
        private static int SlotOfGridIndex(int gridIndex)
        {
            int page = gridIndex / 1000;
            int row = (gridIndex - page * 1000) / 100 - 1;
            int col = gridIndex % 100 - 1;
            if (row < 0 || col < 0 || row >= GridRows || col >= GridCols)
            {
                return -1;
            }
            return row * GridCols + col;
        }

        internal static void RefreshIcons_Postfix(UIItemPicker __instance)
        {
            try
            {
                AppendDarkFogItems(__instance);
            }
            catch (Exception e)
            {
                DarkFogWorkshop.Log?.LogWarning($"[选物品] 补充黑雾材料失败：{e.Message}");
            }
        }

        private static void AppendDarkFogItems(UIItemPicker picker)
        {
            if (picker == null || GameMain.history == null || GameMain.iconSet == null)
            {
                return;
            }

            // 只补「物品」页签（currentType == 1）；「建筑」页签是建筑清单，不混入。
            object currentType = AccessTools.Field(typeof(UIItemPicker), "currentType")?.GetValue(picker);
            if (currentType is int page && page != 1)
            {
                return;
            }

            ItemProto[] protoArray = (ItemProto[])AccessTools
                .Field(typeof(UIItemPicker), "protoArray")?.GetValue(picker);
            uint[] indexArray = (uint[])AccessTools
                .Field(typeof(UIItemPicker), "indexArray")?.GetValue(picker);
            if (protoArray == null || indexArray == null)
            {
                return;
            }

            // 只写可见的 112 个格位（数组实际分配了 120，尾部不显示）。
            int slots = Math.Min(Math.Min(protoArray.Length, indexArray.Length), GridSlots);

            // 自然格位起点：六种材料的 GridIndex 是 5801–5806，
            // 在原版物品目录里占「最后一行前 6 格」（槽位 98–103）。
            // 取其中最小的自然槽位作为摆放起点，之后跳过已占用格顺次摆放。
            int start = int.MaxValue;
            foreach (int id in DarkFogItemIds)
            {
                ItemProto p = LDB.items.Select(id);
                if (p == null)
                {
                    continue;
                }
                int slot = SlotOfGridIndex(p.GridIndex);
                if (slot >= 0 && slot < start)
                {
                    start = slot;
                }
            }
            if (start == int.MaxValue || start >= slots)
            {
                start = 0;
            }

            var placed = new List<string>();
            int cursor = start;
            foreach (int id in DarkFogItemIds)
            {
                // 已经出现在当前页（自然位置）的不再重复补。
                bool exists = false;
                for (int i = 0; i < slots; i++)
                {
                    if (protoArray[i] != null && protoArray[i].ID == id)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    continue;
                }

                ItemProto proto = LDB.items.Select(id);
                if (proto == null)
                {
                    continue;
                }

                while (cursor < slots && protoArray[cursor] != null)
                {
                    cursor++;
                }
                if (cursor >= slots)
                {
                    DarkFogWorkshop.Log?.LogWarning("[选物品] 「物品」页签网格已满，部分黑雾材料未能加入。");
                    break;
                }

                protoArray[cursor] = proto;
                indexArray[cursor] = GameMain.iconSet.itemIconIndex[id];
                placed.Add($"{id}@{cursor}");
                cursor++;
            }

            if (!_diagLogged)
            {
                _diagLogged = true;
                DarkFogWorkshop.Log?.LogInfo(
                    $"[选物品] currentType={currentType} 可见格位={slots} " +
                    $"起始槽位={start} 本次摆放=[{string.Join(", ", placed)}]");
            }
        }
    }
}
