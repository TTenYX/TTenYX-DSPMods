using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 让黑雾 6 种掉落物出现在「选择物品」界面（UIItemPicker）里，
    /// 位置与游戏本体数据中的原版格位完全一致。
    ///
    /// <para><b>原版为什么看不到</b>：游戏源码 <c>UIItemPicker.RefreshIcons</c>
    /// 要求 <c>GridIndex ≥ 1101</c>、<c>history.ItemUnlocked(ID)</c>、
    /// 且 <c>GridIndex/1000 == currentType</c>（只有第 1/2 两个页签）。
    /// 黑雾材料 UnlockKey = -2，只有「正常获取」（拾取黑雾掉落触发
    /// UnlockSpecialItem）后才算解锁 —— 用 MOD 产出的材料永远不触发解锁，
    /// 所以选择窗里看不到。</para>
    ///
    /// <para><b>原版格位</b>（直接解码游戏本体
    /// <c>DSPGAME_Data\resources.assets</c> 里 ItemProtoSet 的序列化数据，
    /// 经 UnlockKey/掉落等级/掉落率浮点逐位校验，不引用任何 MOD 的数据；
    /// 注意不能用运行时 LDB 值 —— OrbitalRing 等 MOD 会在运行时改写它）：</para>
    /// <list type="bullet">
    /// <item>5201 黑雾矩阵 = 1807（物品页 · 第8行 · 列6）</item>
    /// <item>5206 能量碎片 = 1808（列7）</item>
    /// <item>5202 硅基神经元 = 1809（列8）</item>
    /// <item>5204 负熵奇点 = 1810（列9）</item>
    /// <item>5203 物质重组器 = 1811（列10）</item>
    /// <item>5205 虚粒子 = 1112（第1行 · 列11）</item>
    /// </list>
    ///
    /// <para><b>本补丁</b>：RefreshIcons 之后，把六种材料补写到各自的
    /// <b>原版格位</b>（已自然显示的跳过；格位被占则顺延到之后最近的空格）。
    /// 只处理「物品」页签（currentType == 1），不混进「建筑」页签。
    /// 悬停提示与点击选择都读取 protoArray，无需其它改动。</para>
    ///
    /// <para><b>作用范围</b>：只对「物流塔槽位」触发的选择窗生效——
    /// UIItemPicker 是全局共用控件（分拣器/储物仓/监控/总控面板等都会弹），
    /// 这里按 <c>onReturn</c> 回调的宿主对象判断来源，
    /// 仅当宿主是 <c>UIStationStorage</c>（物流塔槽位选择）时才补写，
    /// 其它窗口一律不动。</para>
    ///
    /// <para>手动注册并全程 try/catch：游戏更新导致目标缺失时只记警告，
    /// 不影响插件其它功能。</para>
    /// </summary>
    internal static class DarkFogItemPickerPatch
    {
        /// <summary>黑雾 6 种掉落物 ID（与 DarkFogWorkshopPatch 保持一致）。</summary>
        private static readonly int[] DarkFogItemIds =
        {
            5201, // 黑雾矩阵
            5202, // 硅基神经元
            5203, // 物质重组器
            5204, // 负熵奇点
            5205, // 虚粒子
            5206, // 能量碎片
        };

        /// <summary>
        /// 各自的原版网格槽位（14 列网格的行*14+列，来自游戏本体数据解码）：
        /// 1807→104, 1809→106, 1811→108, 1810→107, 1112→11, 1808→105。
        /// </summary>
        private static readonly int[] VanillaSlots = { 104, 106, 108, 107, 11, 105 };

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

            // 只处理「物流塔槽位」触发的选择窗：UIItemPicker 是全局共用控件
            // （分拣器/储物仓/监控/总控面板等 17 处都会弹），
            // 这里通过 onReturn 回调的宿主对象判断来源——
            // 只有 UIStationStorage.OnItemPickerReturn（物流塔槽位选择）才补写，
            // 其它窗口一律不动。
            if (!(picker.onReturn != null && picker.onReturn.Target is UIStationStorage))
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

            int slots = Math.Min(Math.Min(protoArray.Length, indexArray.Length), GridSlots);

            var placed = new List<string>();
            for (int n = 0; n < DarkFogItemIds.Length; n++)
            {
                int id = DarkFogItemIds[n];

                // 已自然显示（正常获取过、已解锁）的不再重复补。
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

                // 原版格位；被占则从该处向后找最近空格。
                int slot = VanillaSlots[n];
                while (slot < slots && protoArray[slot] != null)
                {
                    slot++;
                }
                if (slot >= slots)
                {
                    continue;
                }

                protoArray[slot] = proto;
                indexArray[slot] = GameMain.iconSet.itemIconIndex[id];
                placed.Add($"{id}@{slot}");
            }

            if (!_diagLogged)
            {
                _diagLogged = true;
                DarkFogWorkshop.Log?.LogInfo(
                    $"[选物品] currentType={currentType} 本次摆放=[{string.Join(", ", placed)}] " +
                    $"（原版格位 1807-1811→末行列6-10，1112→首行列11）");
            }
        }
    }
}
