using System;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 兼容层：OrbitalRing（轨道环）。
    ///
    /// <para><b>现象</b>：同时安装 OrbitalRing 后，打开其新增的
    /// 太空物流港 / 深空物流港 / 太空电梯 的窗口会抛
    /// <c>System.IndexOutOfRangeException</c>，整个物流塔窗口变成错误报告。</para>
    ///
    /// <para><b>根因（OrbitalRing 源码）</b>：
    /// <c>Patches/UI/OrbitalRingStorage/UIStationWindowPatch.cs</c> 里
    /// <c>_uiShareStorage = new UIShareStorage[5]</c> 固定 5 个元素，
    /// 其 <c>StationStoreIsShares</c> 索引器也只支持 0–4；
    /// 但后缀却用 <c>for (int i = 0; i &lt; stationComponent.storage.Length; i++)</c>
    /// 做上界遍历。槽位数一旦超过 5 就越界，且
    /// <c>event_lock</c> 停留在 true，窗口交互被锁死。</para>
    ///
    /// <para><b>与黑雾工坊无关</b>：黑雾工坊从不改变 <c>storage</c> 数组的长度
    /// （只读写元素与 count/inc），也不新增槽位。OrbitalRing 单独安装时
    /// 打开这几种建筑同样会崩。</para>
    ///
    /// <para><b>本兜底</b>：只在「异常确实来自 OrbitalRing 代码」时，
    /// 复位 <c>event_lock</c> 并放行，让窗口继续工作
    /// （代价是它的共享仓储勾选只更新前 5 格，属 OrbitalRing 自身的功能损失）。
    /// 其它异常照常抛出，不掩盖黑雾工坊自身的问题。</para>
    /// </summary>
    internal static class OrbitalRingCompat
    {
        private static Exception Finalizer(Exception __exception, UIStationWindow __instance)
        {
            if (__exception == null)
            {
                return null;
            }
            if (!(__exception is IndexOutOfRangeException) && !(__exception is ArgumentOutOfRangeException))
            {
                return __exception;
            }
            string stack = __exception.StackTrace;
            if (string.IsNullOrEmpty(stack) ||
                stack.IndexOf("ProjectOrbitalRing", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return __exception;
            }
            if (__instance != null)
            {
                // OrbitalRing 的后缀在入口处把 event_lock 置 true，抛异常后没机会复位。
                __instance.event_lock = false;
            }
            if (DarkFogWorkshop.Log != null)
            {
                DarkFogWorkshop.Log.LogWarning(
                    "[兼容] 已拦截 OrbitalRing 共享仓储 UI 的数组越界（其 UI 固定 5 槽），窗口继续运行。");
            }
            return null;
        }

        [HarmonyPatch(typeof(UIStationWindow), "OnStationIdChange")]
        internal static class OnStationIdChangeGuard
        {
            internal static Exception Finalizer(Exception __exception, UIStationWindow __instance) =>
                OrbitalRingCompat.Finalizer(__exception, __instance);
        }

        [HarmonyPatch(typeof(UIStationWindow), "_OnUpdate")]
        internal static class OnUpdateGuard
        {
            internal static Exception Finalizer(Exception __exception, UIStationWindow __instance) =>
                OrbitalRingCompat.Finalizer(__exception, __instance);
        }

        [HarmonyPatch(typeof(UIStationWindow), "_OnOpen")]
        internal static class OnOpenGuard
        {
            internal static Exception Finalizer(Exception __exception, UIStationWindow __instance) =>
                OrbitalRingCompat.Finalizer(__exception, __instance);
        }
    }
}
