using System;
using HarmonyLib;

namespace ControlPanelPlus
{
    /// <summary>
    /// Round 3: allow taking / placing items on the logistics control panel for stations that are not
    /// on the current planet.
    ///
    /// The vanilla code guards the item icon interactions with
    /// <see cref="UIControlPanelStationStorage.isLocal"/> and
    /// <see cref="UIControlPanelVeinCollectorPanel.isLocal"/>. Instead of re-implementing the
    /// transfer logic, the getter result is forced to true for the duration of the guarded methods,
    /// so the vanilla (local) path runs unchanged.
    ///
    /// Priority: if CheatEnabler is installed this feature stays inert - its Architect Mode owns the
    /// behaviour (requirement: "以 CheatEnabler 为准").
    /// </summary>
    public static class RemoteItemAccess
    {
        private static int _scope;

        public static bool FeatureEnabled
        {
            get
            {
                return Cfg.Enabled.Value
                       && Cfg.AllowRemoteItemTransfer.Value
                       && !PluginPresence.CheatEnablerInstalled;
            }
        }

        public static bool ForceLocal => _scope > 0;

        public static void BeginScope()
        {
            if (FeatureEnabled) _scope++;
        }

        public static void EndScope()
        {
            if (_scope > 0) _scope--;
        }

        public static void ResetScope()
        {
            _scope = 0;
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationStorage), "OnItemIconMouseDown")]
    internal static class StationStorageMouseDownScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            RemoteItemAccess.BeginScope();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            RemoteItemAccess.EndScope();
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationStorage), "OnTakeBackButtonClick")]
    internal static class StationStorageTakeBackScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            RemoteItemAccess.BeginScope();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            RemoteItemAccess.EndScope();
        }
    }

    [HarmonyPatch(typeof(UIControlPanelVeinCollectorPanel), "OnProductIconClick")]
    internal static class VeinCollectorProductScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            RemoteItemAccess.BeginScope();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            RemoteItemAccess.EndScope();
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationStorage), "get_isLocal")]
    internal static class StationStorageIsLocalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RemoteItemAccess.ForceLocal) __result = true;
        }
    }

    [HarmonyPatch(typeof(UIControlPanelVeinCollectorPanel), "get_isLocal")]
    internal static class VeinCollectorIsLocalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RemoteItemAccess.ForceLocal) __result = true;
        }
    }
}
