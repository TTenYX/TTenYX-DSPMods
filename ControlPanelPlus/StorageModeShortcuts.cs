using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ControlPanelPlus
{
    /// <summary>
    /// Round 3: mouse shortcuts on the logistic storage rows of the control panel to switch the
    /// storage mode quickly (same feature as starfi5h's UITweaks):
    ///   right click  -> switch between Demand and Supply
    ///   middle click -> switch between None and Demand
    ///
    /// The buttons of the local / remote supply-demand popup are used as the hit areas, exactly like
    /// UITweaks does. When UITweaks is installed this feature stays inert ("以 UITweaks 为准").
    /// </summary>
    public static class StorageModeShortcuts
    {
        public static bool FeatureEnabled
        {
            get { return Cfg.Enabled.Value && !PluginPresence.UiTweaksInstalled; }
        }

        /// <summary>Attaches the right/middle click handlers to the two supply-demand buttons of a row.</summary>
        public static void EnsureHandlers(UIControlPanelStationStorage row)
        {
            if (row == null || !FeatureEnabled) return;
            Attach(row.localSdButton, row, false);
            Attach(row.remoteSdButton, row, true);
        }

        /// <summary>True when both supply/demand buttons of the row carry our click handler (self test).</summary>
        public static bool HandlersAttached(UIControlPanelStationStorage row)
        {
            if (row == null) return false;
            return HasHandler(row.localSdButton) && HasHandler(row.remoteSdButton);
        }

        public static void DetachHandlers(UIControlPanelStationStorage row)
        {
            if (row == null) return;
            Detach(row.localSdButton);
            Detach(row.remoteSdButton);
        }

        private static void Attach(Button button, UIControlPanelStationStorage row, bool remote)
        {
            if (button == null) return;
            StorageModeClickHandler handler = button.gameObject.GetComponent<StorageModeClickHandler>();
            if (handler == null) handler = button.gameObject.AddComponent<StorageModeClickHandler>();
            handler.row = row;
            handler.remote = remote;
        }

        private static void Detach(Button button)
        {
            if (button == null) return;
            StorageModeClickHandler handler = button.gameObject.GetComponent<StorageModeClickHandler>();
            if (handler != null) UnityEngine.Object.DestroyImmediate(handler);
        }

        private static bool HasHandler(Button button)
        {
            return button != null && button.gameObject.GetComponent<StorageModeClickHandler>() != null;
        }

        /// <summary>option 1 = Supply&lt;-&gt;Demand (None-&gt;Supply), option 0 = None&lt;-&gt;Demand (Supply-&gt;None).</summary>
        private static ELogisticStorage Cycle(ELogisticStorage current, int option)
        {
            if (option == 0)
            {
                return current == ELogisticStorage.None ? ELogisticStorage.Demand : ELogisticStorage.None;
            }
            return current == ELogisticStorage.Supply ? ELogisticStorage.Demand : ELogisticStorage.Supply;
        }

        /// <summary>Applies the mode change through the native SetStationStorage path.</summary>
        public static void Apply(UIControlPanelStationStorage row, bool remote, int option)
        {
            if (row == null || !FeatureEnabled) return;
            if (!row.isStationDataValid) return;
            try
            {
                StationComponent station = row.station;
                int index = row.index;
                if (station == null || station.storage == null) return;
                if (index < 0 || index >= station.storage.Length) return;
                StationStore store = station.storage[index];
                if (store.itemId <= 0) return;
                if (LDB.items.Select(store.itemId) == null) return;

                PlanetTransport transport = null;
                if (row.factory != null) transport = row.factory.transport;
                if (transport == null && row.masterInspector != null) transport = row.masterInspector.transport;
                if (transport == null) return;

                ELogisticStorage local = store.localLogic;
                ELogisticStorage remoteLogic = store.remoteLogic;
                if (remote) remoteLogic = Cycle(remoteLogic, option);
                else local = Cycle(local, option);

                transport.SetStationStorage(station.id, index, store.itemId, store.max, local, remoteLogic, GameMain.mainPlayer);
                CppLog.Debug($"storage mode shortcut remote={remote} option={option} -> local={local} remote={remoteLogic}");
            }
            catch (Exception e)
            {
                CppLog.Error("storage mode shortcut failed: " + e);
            }
        }
    }

    internal class StorageModeClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public UIControlPanelStationStorage row;
        public bool remote;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || row == null) return;
            if (!StorageModeShortcuts.FeatureEnabled) return;
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                StorageModeShortcuts.Apply(row, remote, 1);
            }
            else if (eventData.button == PointerEventData.InputButton.Middle)
            {
                StorageModeShortcuts.Apply(row, remote, 0);
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationStorage), "_OnOpen")]
    internal static class StationStorageModeShortcutPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIControlPanelStationStorage __instance)
        {
            try
            {
                StorageModeShortcuts.EnsureHandlers(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("attach storage mode shortcut failed: " + e);
            }
        }
    }
}
