using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ControlPanelPlus
{
    /// <summary>All patches on the control panel window itself; the feature code lives next to them.</summary>
    [HarmonyPatch(typeof(UIControlPanelWindow))]
    internal static class ControlPanelWindowPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("_OnOpen")]
        public static void OnOpenPrefix(UIControlPanelWindow __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                // must happen before _OnOpen rebuilds the results, otherwise the default expand
                // policy would run before the "re-open keeps state" flag is set
                ExpandCollapse.OnWindowOpening();
            }
            catch (Exception e)
            {
                CppLog.Error("window open prefix hook failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnOpen")]
        public static void OnOpen(UIControlPanelWindow __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                ExpandCollapse.OnWindowOpened(__instance);
                StationCopyPaste.OnWindowOpened();
                ItemFilterIntersect.EnsureToggleButton(__instance);
                ItemFilterShortcuts.Reset();
                ConfigWatcher.Reset();
                StationCopyPaste.ClearMultiSelect();
            }
            catch (Exception e)
            {
                CppLog.Error("window open hook failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnClose")]
        public static void OnClose(UIControlPanelWindow __instance)
        {
            try
            {
                ExpandCollapse.OnWindowClosed();
                StationCopyPaste.OnWindowClosed();
                ItemFilterShortcuts.Reset();
                ConfigWatcher.Reset();
            }
            catch (Exception e)
            {
                CppLog.Error("window close hook failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("DetermineFilterResults")]
        public static void AfterDetermineFilterResults(UIControlPanelWindow __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                // Order matters: prune first, then decide the default expand state, then let the
                // station clipboard notice a filter switch (it clears the multi selection then).
                ItemFilterIntersect.Apply(__instance);
                ExpandCollapse.AfterResults(__instance);
                StationCopyPaste.OnFilterResultsDetermined(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("result hook failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelWindow))]
    internal static class ControlPanelWindowInitPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("_OnInit")]
        public static void OnInit(UIControlPanelWindow __instance)
        {
            try
            {
                ExpandCollapse.OnGameInit();
                StationCopyPaste.OnWindowClosed();
                ItemFilterShortcuts.Reset();
                ConfigWatcher.Reset();
            }
            catch (Exception e)
            {
                CppLog.Error("window init hook failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelFilterPanel))]
    internal static class ControlPanelFilterPanelPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("_OnOpen")]
        public static void OnOpen(UIControlPanelFilterPanel __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                NativeUi.EnableRichText(__instance.astroFilterBox);
            }
            catch (Exception e)
            {
                CppLog.Error("filter panel open hook failed: " + e);
            }
        }
    }
}
