using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ControlPanelPlus
{
    /// <summary>
    /// F6 (new): item filter shortcuts.
    ///  - opening the control panel automatically filters the item under the mouse cursor
    ///    (same idea as UXAssist's "Logistics Control Panel Improvement");
    ///  - right-clicking an item icon of a station / advanced miner entry replaces the item filter
    ///    with that item;
    ///  - Shift + right-click appends the item to the existing filter instead of replacing it.
    ///
    /// UXAssist (when installed) also replaces the filter on a plain right click. To stay compatible
    /// with it, the append case is applied in LateUpdate - after every right-click handler of the
    /// frame ran - using the filter snapshot of the previous frame.
    /// </summary>
    public static class ItemFilterShortcuts
    {
        private static readonly Dictionary<UIControlPanelObjectEntry, Action<int>[]> Handlers =
            new Dictionary<UIControlPanelObjectEntry, Action<int>[]>();

        private static readonly FieldInfo GridStorageField = AccessTools.Field(typeof(UIStorageGrid), "storage");
        private static readonly FieldInfo GridMouseXField = AccessTools.Field(typeof(UIStorageGrid), "mouseOnX");
        private static readonly FieldInfo GridMouseYField = AccessTools.Field(typeof(UIStorageGrid), "mouseOnY");
        private static readonly FieldInfo ReplicatorRecipeArrayField = AccessTools.Field(typeof(UIReplicatorWindow), "recipeProtoArray");
        private static readonly FieldInfo ReplicatorMouseIndexField = AccessTools.Field(typeof(UIReplicatorWindow), "mouseRecipeIndex");

        private static int[] _lastSeenItems;
        private static bool _pendingAppend;
        private static int _pendingItem;
        private static int[] _pendingBase;

        // ------------------------------------------------------------ attach / detach

        public static void AttachStationEntry(UIControlPanelStationEntry entry)
        {
            if (entry == null || Handlers.ContainsKey(entry)) return;
            UIButton[] buttons =
            {
                ButtonOf(entry.storageItem0), ButtonOf(entry.storageItem1), ButtonOf(entry.storageItem2),
                ButtonOf(entry.storageItem3), ButtonOf(entry.storageItem4)
            };
            Register(entry, buttons, 5);
        }

        public static void AttachMinerEntry(UIControlPanelAdvancedMinerEntry entry)
        {
            if (entry == null || Handlers.ContainsKey(entry)) return;
            Register(entry, new[] { ButtonOf(entry.storageItem) }, 1);
        }

        private static UIButton ButtonOf(UIControlPanelStorageItem item)
        {
            return item != null ? item.itemButton : null;
        }

        private static void Register(UIControlPanelObjectEntry entry, UIButton[] buttons, int slots)
        {
            Action<int>[] actions = new Action<int>[slots];
            for (int i = 0; i < slots; i++)
            {
                int slot = i;
                actions[i] = _ => OnIconRightClick(entry, slot);
                if (buttons[i] != null) buttons[i].onRightClick += actions[i];
            }
            Handlers[entry] = actions;
        }

        public static void Detach(UIControlPanelObjectEntry entry)
        {
            if (entry == null || !Handlers.TryGetValue(entry, out Action<int>[] actions)) return;
            UIButton[] buttons = null;
            if (entry is UIControlPanelStationEntry stationEntry)
            {
                buttons = new[]
                {
                    ButtonOf(stationEntry.storageItem0), ButtonOf(stationEntry.storageItem1), ButtonOf(stationEntry.storageItem2),
                    ButtonOf(stationEntry.storageItem3), ButtonOf(stationEntry.storageItem4)
                };
            }
            else if (entry is UIControlPanelAdvancedMinerEntry minerEntry)
            {
                buttons = new[] { ButtonOf(minerEntry.storageItem) };
            }
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Length && i < actions.Length; i++)
                {
                    if (buttons[i] != null) buttons[i].onRightClick -= actions[i];
                }
            }
            Handlers.Remove(entry);
        }

        // ------------------------------------------------------------ click handling

        private static void OnIconRightClick(UIControlPanelObjectEntry entry, int slot)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                int itemId = ItemIdOf(entry, slot);
                if (itemId <= 0) return;
                UIControlPanelWindow window = NativeUi.CurrentWindow();
                if (window == null || !window.active) return;
                if (VFInput.shift)
                {
                    ScheduleAppend(itemId);
                }
                else
                {
                    SetSingleItem(window, itemId, refresh: true);
                }
            }
            catch (Exception e)
            {
                CppLog.Error("right-click item filter failed: " + e);
            }
        }

        private static int ItemIdOf(UIControlPanelObjectEntry entry, int slot)
        {
            StationComponent station = null;
            if (entry is UIControlPanelStationEntry stationEntry) station = stationEntry.station;
            else if (entry is UIControlPanelAdvancedMinerEntry minerEntry) station = minerEntry.station;
            if (station == null || station.storage == null) return 0;
            if (slot < 0 || slot >= station.storage.Length) return 0;
            return station.storage[slot].itemId;
        }

        private static void ScheduleAppend(int itemId)
        {
            _pendingBase = CopyItems(_lastSeenItems);
            _pendingItem = itemId;
            _pendingAppend = true;
        }

        /// <summary>Called every frame from HotkeyDriver.LateUpdate.</summary>
        public static void Tick()
        {
            UIControlPanelWindow window = NativeUi.CurrentWindow();
            if (_pendingAppend)
            {
                _pendingAppend = false;
                if (window != null && window.active) ApplyUnion(window, _pendingBase, _pendingItem);
                _pendingBase = null;
            }
            _lastSeenItems = window != null && window.active ? CopyItems(window.filter.itemsFilter) : null;
        }

        public static void Reset()
        {
            _pendingAppend = false;
            _pendingBase = null;
            _lastSeenItems = null;
        }

        private static int[] CopyItems(int[] items)
        {
            if (items == null || items.Length == 0) return null;
            int[] copy = new int[items.Length];
            Array.Copy(items, copy, items.Length);
            return copy;
        }

        private static void ApplyUnion(UIControlPanelWindow window, int[] baseItems, int itemId)
        {
            List<int> items = baseItems != null ? new List<int>(baseItems) : new List<int>();
            if (!items.Contains(itemId)) items.Add(itemId);
            while (items.Count > ControlPanelFilter.ITEM_FILTER_LIMIT) items.RemoveAt(0);

            ControlPanelFilter filter = window.filter;
            filter.itemsFilter = items.Count > 0 ? items.ToArray() : null;
            window.filter = filter;
            Refresh(window);
            _lastSeenItems = CopyItems(window.filter.itemsFilter);
            CppLog.Info("item filter appended: " + string.Join(",", items.ConvertAll(i => i.ToString()).ToArray()));
        }

        public static void SetSingleItem(UIControlPanelWindow window, int itemId, bool refresh)
        {
            if (window == null || itemId <= 0) return;
            int[] current = window.filter.itemsFilter;
            if (current != null && current.Length == 1 && current[0] == itemId) return;
            ControlPanelFilter filter = window.filter;
            filter.itemsFilter = new[] { itemId };
            window.filter = filter;
            if (refresh && window.active)
            {
                Refresh(window);
                CppLog.Info("item filter set by right click: " + itemId);
            }
            _lastSeenItems = CopyItems(window.filter.itemsFilter);
        }

        /// <summary>Set the filter before the panel opens; the panel's own open path refreshes the UI.</summary>
        public static void SetSingleItemForOpen(UIControlPanelWindow window, int itemId)
        {
            SetSingleItem(window, itemId, refresh: false);
        }

        private static void Refresh(UIControlPanelWindow window)
        {
            try
            {
                UIControlPanelFilterPanel panel = window.filterPanel;
                if (panel != null) panel.RefreshFilterUI();
                window.DetermineFilterResults();
                window.DetermineEntryVisible();
            }
            catch (Exception e)
            {
                CppLog.Error("filter refresh failed: " + e);
            }
        }

        // ------------------------------------------------------------ open-panel auto filter

        public static int ItemIdHintUnderMouse()
        {
            try
            {
                Player player = GameMain.data != null ? GameMain.data.mainPlayer : null;
                if (player != null && player.inhandItemId > 0) return player.inhandItemId;

                EventSystem eventSystem = EventSystem.current;
                if (eventSystem == null) return 0;
                PointerEventData pointer = new PointerEventData(eventSystem);
                pointer.position = Input.mousePosition;
                List<RaycastResult> hits = new List<RaycastResult>();
                eventSystem.RaycastAll(pointer, hits);

                for (int i = 0; i < hits.Count; i++)
                {
                    GameObject go = hits[i].gameObject;
                    if (go == null) continue;
                    UIButton button = go.GetComponentInParent<UIButton>();
                    if (button != null && button.tips.itemId > 0) return button.tips.itemId;

                    UIReplicatorWindow replicator = go.GetComponentInParent<UIReplicatorWindow>();
                    if (replicator != null) return ReplicatorItem(replicator);

                    UIStorageGrid grid = go.GetComponentInParent<UIStorageGrid>();
                    if (grid != null) return StorageGridItem(grid);

                    UIProductEntry product = go.GetComponentInParent<UIProductEntry>();
                    if (product != null) return ProductItem(product);
                }
            }
            catch (Exception e)
            {
                CppLog.Debug("hover item lookup failed: " + e.Message);
            }
            return 0;
        }

        private static int ReplicatorItem(UIReplicatorWindow replicator)
        {
            if (ReplicatorMouseIndexField == null || ReplicatorRecipeArrayField == null) return 0;
            int index = (int)ReplicatorMouseIndexField.GetValue(replicator);
            if (index < 0) return 0;
            RecipeProto[] recipes = ReplicatorRecipeArrayField.GetValue(replicator) as RecipeProto[];
            if (recipes == null || index >= recipes.Length) return 0;
            RecipeProto recipe = recipes[index];
            if (recipe == null || recipe.Results == null || recipe.Results.Length == 0) return 0;
            return recipe.Results[0];
        }

        private static int StorageGridItem(UIStorageGrid grid)
        {
            if (GridStorageField == null || GridMouseXField == null || GridMouseYField == null) return 0;
            StorageComponent storage = GridStorageField.GetValue(grid) as StorageComponent;
            if (storage == null || storage.grids == null) return 0;
            int x = (int)GridMouseXField.GetValue(grid);
            int y = (int)GridMouseYField.GetValue(grid);
            if (x < 0 || y < 0) return 0;
            int index = x + y * grid.colCount;
            if (index < 0 || index >= storage.grids.Length) return 0;
            return storage.grids[index].itemId;
        }

        private static int ProductItem(UIProductEntry product)
        {
            if (product.productionStatWindow == null || !product.productionStatWindow.isProductionTab) return 0;
            return product.entryData != null ? product.entryData.itemId : 0;
        }
    }

    [HarmonyPatch(typeof(UIGame), "OpenControlPanelWindow")]
    internal static class AutoFilterOnOpenPatch
    {
        [HarmonyPrefix]
        public static void Prefix(UIGame __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                UIControlPanelWindow window = __instance.controlPanelWindow;
                if (window == null || window.active) return;
                int itemId = ItemFilterShortcuts.ItemIdHintUnderMouse();
                if (itemId > 0) ItemFilterShortcuts.SetSingleItemForOpen(window, itemId);
            }
            catch (Exception e)
            {
                CppLog.Error("auto item filter on open failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationEntry))]
    internal static class StationEntryFilterPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("_OnRegEvent")]
        public static void OnRegEvent(UIControlPanelStationEntry __instance)
        {
            try
            {
                ItemFilterShortcuts.AttachStationEntry(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("attach station right-click filter failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnUnregEvent")]
        public static void OnUnregEvent(UIControlPanelStationEntry __instance)
        {
            ItemFilterShortcuts.Detach(__instance);
        }
    }

    [HarmonyPatch(typeof(UIControlPanelAdvancedMinerEntry))]
    internal static class MinerEntryFilterPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("_OnRegEvent")]
        public static void OnRegEvent(UIControlPanelAdvancedMinerEntry __instance)
        {
            try
            {
                ItemFilterShortcuts.AttachMinerEntry(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("attach miner right-click filter failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnUnregEvent")]
        public static void OnUnregEvent(UIControlPanelAdvancedMinerEntry __instance)
        {
            ItemFilterShortcuts.Detach(__instance);
        }
    }
}
