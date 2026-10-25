using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ControlPanelPlus
{
    /// <summary>
    /// F4: turn the item filter from union into intersection.
    ///
    /// <see cref="ControlPanelFilter.ItemFilterPass"/> alone cannot express "contains all items" - it is
    /// called per slot - so the intersection is applied afterwards by pruning the freshly built result
    /// list. Result positions are cumulative heights, so removing an entry rewinds every later position
    /// by that entry's height (see <see cref="WindowAccess.RemoveResultAt"/>).
    /// </summary>
    public static class ItemFilterIntersect
    {
        private const string ToggleButtonName = "cpt-filter-mode";
        private static UIButton _toggleButton;

        public static void Apply(UIControlPanelWindow w)
        {
            if (!Cfg.Enabled.Value || !Cfg.ItemFilterIntersect.Value) return;
            ControlPanelFilter filter = w.filter;
            int filterCount = filter.itemsFilterCount;
            if (filterCount < 2) return; // union == intersection for a single item

            List<ControlPanelTarget> results = WindowAccess.Results(w);
            if (results == null || results.Count == 0) return;

            HashSet<int> remove = new HashSet<int>();
            int inspected = 0;
            for (int i = 0; i < results.Count; i++)
            {
                ControlPanelTarget target = results[i];
                if (target.entryType == EControlPanelEntryType.None) continue;
                bool keep = true;
                switch (target.entryType)
                {
                    case EControlPanelEntryType.InterstellarStation:
                    case EControlPanelEntryType.OrbitCollector:
                    case EControlPanelEntryType.LocalStation:
                    case EControlPanelEntryType.VeinCollector:
                        inspected++;
                        keep = StationContainsAll(target, filter.itemsFilter);
                        break;
                    case EControlPanelEntryType.Dispenser:
                        inspected++;
                        keep = DispenserContainsAll(target, filter.itemsFilter);
                        break;
                    case EControlPanelEntryType.Marker:
                        keep = true; // the vanilla filter never applies item conditions to markers
                        break;
                }
                if (!keep) remove.Add(i);
            }

            // drop planet headers whose every child was pruned (the vanilla code only keeps a planet
            // row when it contributed at least one entry)
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].entryType != EControlPanelEntryType.None) continue;
                bool hasChild = false;
                for (int j = i + 1; j < results.Count && results[j].entryType != EControlPanelEntryType.None; j++)
                {
                    if (!remove.Contains(j))
                    {
                        hasChild = true;
                        break;
                    }
                }
                if (!hasChild) remove.Add(i);
            }

            if (remove.Count == 0) return;

            List<int> indices = new List<int>(remove);
            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                WindowAccess.RemoveResultAt(w, indices[i]);
            }
            WindowAccess.MarkEntryVisibleDirty(w);
            w.ReconnSelectionOnDetermineResults();
            CppLog.Debug($"item filter intersect: inspected={inspected} removed={remove.Count} remaining={results.Count}");
        }

        private static bool StationContainsAll(ControlPanelTarget target, int[] items)
        {
            StationComponent station = ResolveStation(target);
            if (station == null || station.storage == null) return false;
            for (int f = 0; f < items.Length; f++)
            {
                int itemId = items[f];
                bool found = false;
                for (int s = 0; s < station.storage.Length; s++)
                {
                    if (station.storage[s].itemId == itemId)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool DispenserContainsAll(ControlPanelTarget target, int[] items)
        {
            PlanetFactory factory = ResolveFactory(target.astroId);
            if (factory == null) return false;
            int entityId = target.objId;
            if (entityId <= 0 || entityId >= factory.entityPool.Length) return false;
            int dispenserId = factory.entityPool[entityId].dispenserId;
            if (dispenserId <= 0 || dispenserId >= factory.transport.dispenserPool.Length) return false;
            DispenserComponent dispenser = factory.transport.dispenserPool[dispenserId];
            if (dispenser == null || dispenser.id != dispenserId) return false;
            // a dispenser handles a single item type, so it can only satisfy a one-item intersection
            return items.Length == 1 && dispenser.filter == items[0];
        }

        private static StationComponent ResolveStation(ControlPanelTarget target)
        {
            PlanetFactory factory = ResolveFactory(target.astroId);
            if (factory == null) return null;
            int entityId = target.objId;
            if (entityId <= 0 || entityId >= factory.entityPool.Length) return null;
            int stationId = factory.entityPool[entityId].stationId;
            if (stationId <= 0 || stationId >= factory.transport.stationPool.Length) return null;
            StationComponent station = factory.transport.stationPool[stationId];
            return station != null && station.id == stationId ? station : null;
        }

        private static PlanetFactory ResolveFactory(int astroId)
        {
            GameData data = GameMain.data;
            if (data == null || data.galaxy == null) return null;
            PlanetData planet = data.galaxy.PlanetById(astroId);
            return planet != null ? planet.factory : null;
        }

        // ---------------------------------------------------------------- quick toggle button

        /// <summary>
        /// Small switch above the filter rows (title bar, left of the expand/collapse buttons) that
        /// flips the item filter between union and intersection without opening the config manager.
        /// </summary>
        public static void EnsureToggleButton(UIControlPanelWindow window)
        {
            if (window == null) return;
            Transform existing = window.transform.Find(ToggleButtonName);
            if (existing != null)
            {
                _toggleButton = existing.GetComponent<UIButton>();
                UpdateToggleLabel();
                return;
            }

            UIButton button = NativeUi.MakeSmallTextButton(Label(), new Vector2(86f, 22f), 13, ToggleButtonName);
            if (button == null) return;
            button.transform.SetParent(window.transform, worldPositionStays: false);
            NativeUi.AnchorTopRight((RectTransform)button.transform, -121f, -25f, new Vector2(86f, 22f));
            button.onClick += OnToggleClick;
            _toggleButton = button;
            UpdateToggleLabel();
        }

        private static string Label()
        {
            return Cfg.ItemFilterIntersect.Value ? Lang.T("筛选：交集", "Filter: Intersect") : Lang.T("筛选：并集", "Filter: Union");
        }

        private static void UpdateToggleLabel()
        {
            if (_toggleButton == null) return;
            Text text = NativeUi.TextOf(_toggleButton);
            if (text != null) text.text = Label();
        }

        private static void OnToggleClick(int obj)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                Cfg.ItemFilterIntersect.Value = !Cfg.ItemFilterIntersect.Value;
                UIControlPanelWindow window = NativeUi.CurrentWindow();
                if (window != null && window.active)
                {
                    window.DetermineFilterResults();
                    window.DetermineEntryVisible();
                }
                UpdateToggleLabel();
                // keep the runtime config watcher in sync (we already refreshed the list here)
                ConfigWatcher.Reset();
                if (window != null) ConfigWatcher.Poll(window);
                CppLog.Info("item filter mode switched: " + Label());
            }
            catch (Exception e)
            {
                CppLog.Error("filter mode toggle failed: " + e);
            }
        }
    }
}
