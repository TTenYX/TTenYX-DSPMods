using System;
using System.Reflection;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 让黑雾 6 种掉落物出现在「选择物品」界面（UIItemPicker）里。
    ///
    /// 原版 <c>UIItemPicker.RefreshIcons</c> 的显示条件有三道：
    /// <list type="number">
    /// <item><c>GridIndex &gt;= 1101</c>；</item>
    /// <item><c>history.ItemUnlocked(物品ID)</c>（除非 showAll 沙盒模式）；</item>
    /// <item>物品所在网格页 == 当前页签（<c>num == currentType</c>）。</item>
    /// </list>
    ///
    /// 这个选择窗只有第 1、2 两个页签，而黑雾掉落物（52xx）的 GridIndex
    /// 落在第 5 页 —— 没有页签可切，且在击杀黑雾之前它们也不算「已解锁」。
    /// 所以原版永远选不到它们，玩家也就无法把黑雾材料设为物流塔的供应物品。
    ///
    /// 做法：<c>RefreshIcons</c> 执行完之后，把这 6 种物品补写进「物品」页签
    /// 网格里最后一个已占用位置之后（protoArray / indexArray）；
    /// 「建筑」页签不补，避免材料混进建筑清单。
    /// 悬停提示与点击选择都读取 protoArray，因此无需改动其它逻辑。
    ///
    /// 该选择窗为全局共用（物流塔、分拣器、储物仓、过滤器等），
    /// 修正后这些界面同样可以直接选中黑雾材料。
    ///
    /// 采用 <see cref="Install"/> 手动打补丁而非特性标注：
    /// 这只是个锦上添花的界面功能，游戏更新若改名导致目标缺失，
    /// 只记警告跳过，绝不能让整个插件加载失败。
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
        private const int GridSlots = 112;

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

            // 只补进「物品」页签（currentType == 1）。
            // 「建筑」页签（currentType == 2）是建筑清单，黑雾材料混进去是错的。
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

            // 追加到最后一个已占用格位之后，让黑雾材料排在页面末尾，不打乱原版排布。
            int cursor = 0;
            for (int i = 0; i < slots; i++)
            {
                if (protoArray[i] != null)
                {
                    cursor = i + 1;
                }
            }

            foreach (int id in DarkFogItemIds)
            {
                // 该物品已经出现在当前页（自然位置）就不重复补。
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

                if (cursor >= slots)
                {
                    break;
                }

                protoArray[cursor] = proto;
                indexArray[cursor] = GameMain.iconSet.itemIconIndex[id];
                cursor++;
            }
        }
    }
}
