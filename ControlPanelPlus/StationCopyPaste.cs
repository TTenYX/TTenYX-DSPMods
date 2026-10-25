using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ControlPanelPlus
{
    public sealed class SlotSnapshot
    {
        public int itemId;
        public int max;
        public ELogisticStorage localLogic;
        public ELogisticStorage remoteLogic;
    }

    public sealed class StationSnapshot
    {
        public bool isStellar;
        public bool isCollector;
        public bool isVeinCollector;
        public int planetId;
        public string name = "";

        // block 1 - slots
        public SlotSnapshot[] slots;

        // block 1 - auto replenish flags
        public bool droneAutoReplenish;
        public bool shipAutoReplenish;

        // block 2 - station settings
        public long workEnergyPerTick;
        public double tripRangeDrones;
        public double tripRangeShips;
        public double warpEnableDist;
        public int deliveryDrones;
        public int deliveryShips;
        public int pilerCount;
        public bool includeOrbitCollector;
        public bool warperNecessary;

        // block 3 - priority / naming
        public byte[] priorityIndex;
        public byte[] priorityLockTick;
        public ERemoteRoutePriority routePriority;
        public long remoteGroupMask;
        public int[] p2pPartners;

        public bool IsMiner => isCollector || isVeinCollector;

        public string Summary()
        {
            int filled = 0;
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].itemId > 0) filled++;
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(isCollector
                ? Lang.T("轨道采集器", "Orbital Collector")
                : (isVeinCollector
                    ? Lang.T("大型采矿机", "Vein Collector")
                    : (isStellar ? Lang.T("星际物流站", "Interstellar Station") : Lang.T("行星内物流站", "Planetary Station"))));
            sb.Append(Lang.T(" 槽位=", " slots=")).Append(filled)
              .Append(Lang.T(" 名称=\"", " name=\"")).Append(name).Append('"')
              .Append(Lang.T(" 航路优先级=", " routePriority=")).Append(routePriority)
              .Append(Lang.T(" 分组=", " group=")).Append(remoteGroupMask);
            if (p2pPartners != null && p2pPartners.Length > 0)
            {
                sb.Append(Lang.T(" P2P伙伴=", " p2pPartners=")).Append(p2pPartners.Length);
            }
            return sb.ToString();
        }
    }

    internal sealed class PasteResult
    {
        public int slotsApplied;
        public int slotsSkipped;
        public bool settings;
        public bool priority;
        public int routesAdded;
        public string skippedReason = "";
    }

    /// <summary>
    /// F3: copy / paste station settings while the control panel has a station selected.
    ///
    /// Only settings are touched - never inventory, never drones/ships/logistic entities. The native
    /// <see cref="PlanetTransport.SetStationStorage"/> is used for slots so the game performs its own
    /// validation (item existence, max count clamp, traffic refresh).
    /// </summary>
    public static class StationCopyPaste
    {
        private static StationSnapshot _clipboard;
        private static readonly HashSet<long> Multi = new HashSet<long>();
        private static string _filterSignature;

        public static bool HasClipboard => _clipboard != null;

        /// <summary>Current clipboard content (null when empty). Read-only, used by the self test.</summary>
        public static StationSnapshot Clipboard => _clipboard;

        /// <summary>Clears the clipboard. Used by the self test to check the empty-clipboard path.</summary>
        public static void ClearClipboard()
        {
            _clipboard = null;
        }

        /// <summary>Toggle multi-select by target instead of by entry instance (self test entry point).</summary>
        public static bool ToggleMultiSelect(ControlPanelTarget target)
        {
            if (target.astroId == 0 || target.objId == 0) return false;
            StationComponent station = ResolveStation(target.astroId, target.objId);
            if (station == null || station.id <= 0) return false;

            long key = Key(target.astroId, target.objId);
            if (!Multi.Remove(key))
            {
                Multi.Add(key);
                return true;
            }
            return true;
        }

        private static long Key(int astroId, int objId)
        {
            return ((long)astroId << 32) | (uint)objId;
        }

        private static int KeyAstro(long key) => (int)(key >> 32);

        private static int KeyObj(long key) => (int)(key & 0xFFFFFFFFu);

        public static bool IsMultiSelected(ControlPanelTarget target)
        {
            if (Multi.Count == 0 || target.astroId == 0 || target.objId == 0) return false;
            return Multi.Contains(Key(target.astroId, target.objId));
        }

        public static int MultiCount => Multi.Count;

        private static void Tip(string message)
        {
            UIRealtimeTip.Popup(message, false);
        }

        // ------------------------------------------------------------------ lifecycle

        public static void OnWindowOpened()
        {
            Multi.Clear();
            _filterSignature = null;
        }

        public static void OnWindowClosed()
        {
            Multi.Clear();
            _filterSignature = null;
        }

        public static void OnFilterResultsDetermined(UIControlPanelWindow w)
        {
            string signature = FilterSignature(w);
            if (signature != _filterSignature)
            {
                if (_filterSignature != null && Multi.Count > 0)
                {
                    CppLog.Debug("filter changed, multi-select cleared");
                }
                _filterSignature = signature;
                Multi.Clear();
            }
        }

        private static string FilterSignature(UIControlPanelWindow w)
        {
            ControlPanelFilter filter = w.filter;
            StringBuilder sb = new StringBuilder(64);
            sb.Append((int)filter.typeFilter).Append('|').Append(filter.astroFilter).Append('|')
              .Append((int)filter.sortMethod).Append('|').Append(filter.stateFilter).Append('|')
              .Append(filter.tagFilter ?? "").Append('|').Append(filter.searchFilter ?? "");
            if (filter.itemsFilter != null)
            {
                for (int i = 0; i < filter.itemsFilter.Length; i++) sb.Append(filter.itemsFilter[i]).Append(',');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ multi select

        public static bool ToggleMultiSelect(UIControlPanelObjectEntry entry)
        {
            if (entry == null || !entry.inUse) return false;
            if (!IsStationType(entry.target.entryType)) return false;
            ControlPanelTarget target = entry.target;
            if (target.astroId == 0 || target.objId == 0) return false;
            StationComponent station = ResolveStation(target.astroId, target.objId);
            if (station == null || station.id <= 0) return false;

            long key = Key(target.astroId, target.objId);

            // Ctrl+click counts the tower that is already selected as the first member of the
            // multi selection, so the very first Ctrl+click already selects two towers.
            UIControlPanelWindow window = NativeUi.CurrentWindow();
            if (window != null && !window.selection.isNull)
            {
                ControlPanelTarget selected = window.selection;
                long selectedKey = Key(selected.astroId, selected.objId);
                if (IsStationType(selected.entryType) && selectedKey != key && !Multi.Contains(selectedKey)
                    && ResolveStation(selected.astroId, selected.objId) != null)
                {
                    Multi.Add(selectedKey);
                }
            }

            if (!Multi.Remove(key))
            {
                Multi.Add(key);
                Tip(Lang.T($"已加入多选：{Multi.Count} 座塔", $"Added {Multi.Count} station(s) to the multi-selection"));
            }
            else
            {
                Tip(Multi.Count > 0 ? Lang.T($"已移出多选：剩余 {Multi.Count} 座", $"Removed from the multi-selection: {Multi.Count} left") : Lang.T("已清空多选", "Multi-selection cleared"));
            }
            CppLog.Debug($"multi-select toggled astro={target.astroId} obj={target.objId} count={Multi.Count}");
            return true;
        }

        /// <summary>Shift+click: select every station entry between the current selection and the clicked entry.</summary>
        public static bool SelectRangeTo(UIControlPanelObjectEntry entry)
        {
            if (entry == null || !entry.inUse) return false;
            UIControlPanelWindow window = NativeUi.CurrentWindow();
            if (window == null) return false;
            List<ControlPanelTarget> results = WindowAccess.Results(window);
            if (results == null) return false;

            int clickedIndex = IndexOf(results, entry.target);
            int anchorIndex = IndexOf(results, window.selection);
            if (clickedIndex < 0 || anchorIndex < 0) return false;

            int from = Math.Min(clickedIndex, anchorIndex);
            int to = Math.Max(clickedIndex, anchorIndex);
            Multi.Clear();
            for (int i = from; i <= to; i++)
            {
                ControlPanelTarget candidate = results[i];
                if (!IsStationType(candidate.entryType)) continue;
                if (ResolveStation(candidate.astroId, candidate.objId) == null) continue;
                Multi.Add(Key(candidate.astroId, candidate.objId));
            }
            Tip(Lang.T($"已批量选择 {Multi.Count} 座塔", $"Selected {Multi.Count} station(s) in range"));
            CppLog.Debug($"range select {from}..{to} -> {Multi.Count} stations");
            return Multi.Count > 0;
        }

        public static void ClearMultiSelect()
        {
            if (Multi.Count > 0)
            {
                Multi.Clear();
                CppLog.Debug("multi-select cleared");
            }
        }

        public static bool IsStationType(EControlPanelEntryType type)
        {
            return type == EControlPanelEntryType.InterstellarStation
                   || type == EControlPanelEntryType.OrbitCollector
                   || type == EControlPanelEntryType.LocalStation
                   || type == EControlPanelEntryType.VeinCollector;
        }

        private static int IndexOf(List<ControlPanelTarget> results, ControlPanelTarget target)
        {
            if (target.isNull) return -1;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] == target) return i;
            }
            return -1;
        }

        public static void ApplyHighlight(UIControlPanelObjectEntry entry)
        {
            if (entry == null || entry.selectButton == null || !entry.inUse) return;
            if (!IsMultiSelected(entry.target)) return;
            // use the game's own selection highlight so multi-selected towers look exactly like the
            // normally selected one
            entry.selectButton.highlighted = true;
        }

        // ------------------------------------------------------------------ copy

        public static void CopyFromSelection(UIControlPanelWindow w)
        {
            UIControlPanelStationInspector inspector = w != null ? w.currentInspector as UIControlPanelStationInspector : null;
            if (inspector == null || inspector.station == null || inspector.factory == null || inspector.station.id <= 0)
            {
                Tip(Lang.T("请先在左侧选中一个物流塔再复制", "Select a station on the left before copying"));
                return;
            }

            _clipboard = Capture(inspector.station, inspector.factory);
            CppLog.Info("copied station settings: " + _clipboard.Summary());
            Tip(Lang.T("已复制：", "Copied: ") + _clipboard.Summary());
        }

        private static StationSnapshot Capture(StationComponent station, PlanetFactory factory)
        {
            StationSnapshot snap = new StationSnapshot();
            snap.isStellar = station.isStellar;
            snap.isCollector = station.isCollector;
            snap.isVeinCollector = station.isVeinCollector;
            snap.planetId = station.planetId;
            snap.name = SafeExtraInfo(factory, station.entityId);

            snap.slots = new SlotSnapshot[station.storage.Length];
            for (int i = 0; i < station.storage.Length; i++)
            {
                StationStore store = station.storage[i];
                snap.slots[i] = new SlotSnapshot
                {
                    itemId = store.itemId,
                    max = store.max,
                    localLogic = store.localLogic,
                    remoteLogic = store.remoteLogic
                };
            }
            snap.droneAutoReplenish = station.droneAutoReplenish;
            snap.shipAutoReplenish = station.shipAutoReplenish;

            if (station.pcId > 0 && station.pcId < factory.powerSystem.consumerPool.Length)
            {
                snap.workEnergyPerTick = factory.powerSystem.consumerPool[station.pcId].workEnergyPerTick;
            }
            snap.tripRangeDrones = station.tripRangeDrones;
            snap.tripRangeShips = station.tripRangeShips;
            snap.warpEnableDist = station.warpEnableDist;
            snap.deliveryDrones = station.deliveryDrones;
            snap.deliveryShips = station.deliveryShips;
            snap.pilerCount = station.pilerCount;
            snap.includeOrbitCollector = station.includeOrbitCollector;
            snap.warperNecessary = station.warperNecessary;

            if (station.priorityLocks != null)
            {
                snap.priorityIndex = new byte[station.priorityLocks.Length];
                snap.priorityLockTick = new byte[station.priorityLocks.Length];
                for (int i = 0; i < station.priorityLocks.Length; i++)
                {
                    snap.priorityIndex[i] = station.priorityLocks[i].priorityIndex;
                    snap.priorityLockTick[i] = station.priorityLocks[i].lockTick;
                }
            }
            snap.routePriority = station.routePriority;
            snap.remoteGroupMask = station.remoteGroupMask;
            snap.p2pPartners = CollectP2pPartners(station);
            return snap;
        }

        private static string SafeExtraInfo(PlanetFactory factory, int entityId)
        {
            try
            {
                return factory.ReadExtraInfoOnEntity(entityId) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static int[] CollectP2pPartners(StationComponent station)
        {
            if (!station.isStellar || station.gid <= 0) return null;
            GameData data = GameMain.data;
            if (data == null || data.galacticTransport == null) return null;
            HashSet<long> routes = data.galacticTransport.station2stationRoutes;
            if (routes == null || routes.Count == 0) return null;

            List<int> partners = new List<int>();
            foreach (long key in routes)
            {
                int gid0 = (int)(key & 0xFFFFFFFFu);
                int gid1 = (int)(key >> 32);
                if (gid0 == station.gid) partners.Add(gid1);
                else if (gid1 == station.gid) partners.Add(gid0);
            }
            return partners.Count > 0 ? partners.ToArray() : null;
        }

        // ------------------------------------------------------------------ paste

        public static void Paste(UIControlPanelWindow w, bool batch)
        {
            if (_clipboard == null)
            {
                Tip(Lang.T("剪贴板为空：先选中物流塔按复制键", "Clipboard is empty: select a station and press the copy key first"));
                return;
            }

            try
            {
                if (batch)
                {
                    PasteBatch();
                    return;
                }

                List<StationComponent> targets = new List<StationComponent>();
                List<PlanetFactory> factories = new List<PlanetFactory>();
                if (Multi.Count > 0)
                {
                    foreach (long key in Multi)
                    {
                        StationComponent station = ResolveStation(KeyAstro(key), KeyObj(key));
                        PlanetFactory factory = ResolveFactory(KeyAstro(key));
                        if (station != null && factory != null)
                        {
                            targets.Add(station);
                            factories.Add(factory);
                        }
                    }
                }
                else
                {
                    UIControlPanelStationInspector inspector = w != null ? w.currentInspector as UIControlPanelStationInspector : null;
                    if (inspector != null && inspector.station != null && inspector.station.id > 0 && inspector.factory != null)
                    {
                        targets.Add(inspector.station);
                        factories.Add(inspector.factory);
                    }
                }

                if (targets.Count == 0)
                {
                    Tip(Lang.T("请先在左侧选中一个物流塔再粘贴", "Select a station on the left before pasting"));
                    return;
                }

                int ok = 0;
                int slotsApplied = 0;
                int slotsSkipped = 0;
                bool trafficTouched = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    PasteResult result = ApplyTo(targets[i], factories[i]);
                    trafficTouched |= result.priority;
                    CppLog.Debug($"pasted to [{Describe(targets[i], factories[i])}] slots={result.slotsApplied}/{result.slotsApplied + result.slotsSkipped}" +
                                (result.slotsSkipped > 0 ? $" skipped={result.slotsSkipped}" : "") +
                                $" settings={result.settings} priority={result.priority} routes+={result.routesAdded}");
                    if (result.slotsSkipped > 0 && !string.IsNullOrEmpty(result.skippedReason))
                    {
                        CppLog.Debug("  skipped: " + result.skippedReason);
                    }
                    ok++;
                    slotsApplied += result.slotsApplied;
                    slotsSkipped += result.slotsSkipped;
                }

                if (trafficTouched && GameMain.data != null && GameMain.data.galacticTransport != null)
                {
                    GameMain.data.galacticTransport.RefreshTraffic();
                }

                string message = Lang.T($"已粘贴到 {ok} 座物流塔（槽位 {slotsApplied} 项", $"Pasted to {ok} station(s) ({slotsApplied} slot updates");
                if (slotsSkipped > 0) message += Lang.T($"，跳过 {slotsSkipped} 项", $", {slotsSkipped} skipped");
                message += Lang.T("）", ")");
                Tip(message);
            }
            catch (Exception e)
            {
                CppLog.Error("paste failed: " + e);
                Tip(Lang.T("粘贴失败，详见 BepInEx 日志", "Paste failed, see the BepInEx log"));
            }
        }

        private static void PasteBatch()
        {
            PlanetData localPlanet = GameMain.data != null ? GameMain.data.localPlanet : null;
            PlanetFactory factory = localPlanet != null ? localPlanet.factory : null;
            if (factory == null)
            {
                Tip(Lang.T("当前没有所在星球，无法批量粘贴", "No current planet, batch paste is unavailable"));
                return;
            }

            StationComponent[] pool = factory.transport.stationPool;
            int cursor = factory.transport.stationCursor;
            int matched = 0;
            int ok = 0;
            int slotsSkipped = 0;
            bool trafficTouched = false;
            for (int i = 1; i < cursor; i++)
            {
                StationComponent station = pool[i];
                if (station == null || station.id != i) continue;
                if (!SlotLayoutMatches(station, _clipboard)) continue;
                matched++;
                PasteResult result = ApplyTo(station, factory);
                trafficTouched |= result.priority;
                ok++;
                slotsSkipped += result.slotsSkipped;
                CppLog.Debug($"batch paste -> [{Describe(station, factory)}] slots={result.slotsApplied}/{result.slotsApplied + result.slotsSkipped} skipped={result.slotsSkipped}");
            }
            if (trafficTouched && GameMain.data != null && GameMain.data.galacticTransport != null)
            {
                GameMain.data.galacticTransport.RefreshTraffic();
            }

            if (matched == 0)
            {
                Tip(Lang.T("当前星球没有槽位设置相同的物流塔", "No station on the current planet shares the same slot layout"));
                CppLog.Info("batch paste: 0 matching stations on current planet");
                return;
            }
            CppLog.Info($"batch paste: planet={localPlanet.displayName} matched={ok} slotsSkipped={slotsSkipped}");
            Tip(Lang.T($"批量粘贴：命中 {matched} 座槽位相同的物流塔", $"Batch paste: {matched} station(s) with the same slot layout"));
        }

        private static bool SlotLayoutMatches(StationComponent station, StationSnapshot snap)
        {
            if (station.storage == null || snap.slots == null) return false;
            int count = Math.Min(station.storage.Length, snap.slots.Length);
            for (int i = 0; i < count; i++)
            {
                StationStore store = station.storage[i];
                SlotSnapshot slot = snap.slots[i];
                if (store.itemId != slot.itemId) return false;
                if (store.localLogic != slot.localLogic) return false;
                if (store.remoteLogic != slot.remoteLogic) return false;
            }
            // any extra slot beyond the snapshot must be empty
            int longer = Math.Max(station.storage.Length, snap.slots.Length);
            for (int i = count; i < longer; i++)
            {
                if (i < station.storage.Length && station.storage[i].itemId != 0) return false;
                if (i < snap.slots.Length && snap.slots[i].itemId != 0) return false;
            }
            return true;
        }

        private static PasteResult ApplyTo(StationComponent target, PlanetFactory factory)
        {
            PasteResult result = new PasteResult();
            StringBuilder skipped = new StringBuilder();

            if (Cfg.EnableSlots.Value)
            {
                ApplySlots(target, factory, result, skipped);
                target.droneAutoReplenish = _clipboard.droneAutoReplenish;
                target.shipAutoReplenish = _clipboard.shipAutoReplenish;
            }
            if (Cfg.EnableSettings.Value)
            {
                ApplySettings(target, factory);
                result.settings = true;
            }
            if (Cfg.EnablePriority.Value)
            {
                ApplyPriority(target, factory, result);
                result.priority = true;
            }

            result.skippedReason = skipped.ToString();
            return result;
        }

        private static void ApplySlots(StationComponent target, PlanetFactory factory, PasteResult result, StringBuilder skipped)
        {
            bool reduced = _clipboard.IsMiner || target.isCollector || target.isVeinCollector;
            Player player = GameMain.mainPlayer;
            int count = Math.Min(target.storage.Length, _clipboard.slots.Length);

            for (int i = 0; i < count; i++)
            {
                if (!Cfg.SlotOn(i)) continue;
                SlotSnapshot source = _clipboard.slots[i];
                StationStore targetSlot = target.storage[i];

                if (reduced)
                {
                    // Miner / orbit collector: only max + supply/demand, never the item type.
                    if (targetSlot.itemId <= 0) continue;
                    factory.transport.SetStationStorage(target.id, i, targetSlot.itemId, source.max,
                        source.localLogic, source.remoteLogic, player);
                    if (Cfg.AllowMinerOverflow.Value && source.max > 0)
                    {
                        // Keep the original max even when it is above the target station's capacity.
                        target.storage[i].max = source.max;
                    }
                    else
                    {
                        // The default behaviour is the reduced copy: never exceed the target station's
                        // own capacity, even when another mod removes the engine-side clamp.
                        int capacity = StationCapacity(target, factory);
                        if (capacity > 0 && target.storage[i].max > capacity) target.storage[i].max = capacity;
                    }
                    result.slotsApplied++;
                    continue;
                }

                if (source.itemId <= 0) continue; // never clear existing target slots

                bool forceReplace = false;
                if (targetSlot.itemId > 0 && targetSlot.itemId != source.itemId && targetSlot.count > 0)
                {
                    if (!Cfg.ForcePaste.Value)
                    {
                        // Keep the inventory of the target tower intact.
                        result.slotsSkipped++;
                        skipped.Append($"slot{i}({targetSlot.itemId}->{source.itemId},count={targetSlot.count}) ");
                        continue;
                    }
                    forceReplace = true;
                }

                // Force paste: replaced items go to the player unless the destroy switch is on; the
                // station's input/output slot order (port binding) is preserved.
                int localOrder = targetSlot.localOrder;
                int remoteOrder = targetSlot.remoteOrder;
                Player receiver = forceReplace && Cfg.DestroyReplacedItems.Value ? null : player;
                factory.transport.SetStationStorage(target.id, i, source.itemId, source.max,
                    source.localLogic, source.remoteLogic, receiver);
                if (forceReplace)
                {
                    target.storage[i].localOrder = localOrder;
                    target.storage[i].remoteOrder = remoteOrder;
                }
                result.slotsApplied++;
            }

            if (target.storage.Length < _clipboard.slots.Length)
            {
                int lost = 0;
                for (int i = target.storage.Length; i < _clipboard.slots.Length; i++)
                {
                    if (_clipboard.slots[i].itemId > 0) lost++;
                }
                if (lost > 0)
                {
                    result.slotsSkipped += lost;
                    skipped.Append($"targetSlots={target.storage.Length} clipboardSlots={_clipboard.slots.Length} ");
                }
            }
        }

        private static void ApplySettings(StationComponent target, PlanetFactory factory)
        {
            if (Cfg.SetChargePower.Value && target.pcId > 0 && target.pcId < factory.powerSystem.consumerPool.Length)
            {
                long chargePower = _clipboard.workEnergyPerTick;
                long prefab = WorkEnergyPrefab(factory, target);
                if (prefab > 0)
                {
                    long min = prefab / 2;
                    long max = prefab * 5;
                    if (chargePower < min) chargePower = min;
                    if (chargePower > max) chargePower = max;
                }
                factory.powerSystem.consumerPool[target.pcId].workEnergyPerTick = chargePower;
            }

            if (target.isStellar)
            {
                if (Cfg.SetTripDrones.Value) target.tripRangeDrones = _clipboard.tripRangeDrones;
                if (Cfg.SetTripShips.Value) target.tripRangeShips = _clipboard.tripRangeShips;
                if (Cfg.SetWarpDistance.Value) target.warpEnableDist = _clipboard.warpEnableDist;
                if (Cfg.SetWarperNecessary.Value) target.warperNecessary = _clipboard.warperNecessary;
                if (Cfg.SetOrbitCollector.Value) target.includeOrbitCollector = _clipboard.includeOrbitCollector;
                if (Cfg.SetDeliveryDrones.Value) target.deliveryDrones = _clipboard.deliveryDrones;
                if (Cfg.SetDeliveryShips.Value) target.deliveryShips = _clipboard.deliveryShips;
                if (!target.isCollector && Cfg.SetPilerCount.Value)
                {
                    target.pilerCount = _clipboard.pilerCount;
                }
            }
            else if (!target.isVeinCollector)
            {
                if (Cfg.SetTripDrones.Value) target.tripRangeDrones = _clipboard.tripRangeDrones;
                if (Cfg.SetDeliveryDrones.Value) target.deliveryDrones = _clipboard.deliveryDrones;
                if (Cfg.SetPilerCount.Value) target.pilerCount = _clipboard.pilerCount;
            }
            else if (Cfg.SetPilerCount.Value)
            {
                target.pilerCount = _clipboard.pilerCount;
            }
        }

        private static int StationCapacity(StationComponent station, PlanetFactory factory)
        {
            try
            {
                int modelIndex = factory.entityPool[station.entityId].modelIndex;
                ModelProto model = LDB.models.Select(modelIndex);
                int capacity = model != null && model.prefabDesc != null ? model.prefabDesc.stationMaxItemCount : 0;
                int extra = station.isStellar && !station.isCollector && !station.isVeinCollector
                    ? GameMain.history.remoteStationExtraStorage
                    : GameMain.history.localStationExtraStorage;
                return capacity + extra;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static long WorkEnergyPrefab(PlanetFactory factory, StationComponent station)
        {
            try
            {
                EntityData entity = factory.entityPool[station.entityId];
                ItemProto proto = LDB.items.Select(entity.protoId);
                if (proto != null && proto.prefabDesc != null)
                {
                    return proto.prefabDesc.workEnergyPerTick;
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return 0;
        }

        private static void ApplyPriority(StationComponent target, PlanetFactory factory, PasteResult result)
        {
            string targetName = SafeExtraInfo(factory, target.entityId);
            bool renamed = false;
            if (!string.IsNullOrEmpty(_clipboard.name) && _clipboard.name != targetName)
            {
                factory.WriteExtraInfoOnEntity(target.entityId, _clipboard.name);
                renamed = true;
                CppLog.Debug($"  named target station as \"{_clipboard.name}\"");
            }

            if (_clipboard.priorityIndex != null && target.priorityLocks != null)
            {
                int count = Math.Min(_clipboard.priorityIndex.Length, target.priorityLocks.Length);
                for (int i = 0; i < count; i++)
                {
                    if (target.storage[i].itemId <= 0) continue;
                    target.priorityLocks[i].priorityIndex = _clipboard.priorityIndex[i];
                    target.priorityLocks[i].lockTick = _clipboard.priorityLockTick[i];
                }
            }

            if (target.isStellar)
            {
                target.routePriority = _clipboard.routePriority;
                target.remoteGroupMask = _clipboard.remoteGroupMask;

                if (renamed && target.gid > 0 && _clipboard.p2pPartners != null)
                {
                    GalacticTransport transport = GameMain.data != null ? GameMain.data.galacticTransport : null;
                    if (transport != null)
                    {
                        for (int i = 0; i < _clipboard.p2pPartners.Length; i++)
                        {
                            int partner = _clipboard.p2pPartners[i];
                            if (partner <= 0 || partner == target.gid) continue;
                            if (partner >= transport.stationPool.Length || transport.stationPool[partner] == null) continue;
                            // add straight into the route set; the caller refreshes traffic once for the whole paste
                            long key = transport.CalculateStation2StationKey(target.gid, partner);
                            if (transport.station2stationRoutes.Add(key))
                            {
                                result.routesAdded++;
                            }
                        }
                    }
                }
            }
        }

        private static string Describe(StationComponent station, PlanetFactory factory)
        {
            string name = SafeExtraInfo(factory, station.entityId);
            if (string.IsNullOrEmpty(name)) name = "#" + station.id;
            return $"{factory.planet.displayName}/{name}";
        }

        // ------------------------------------------------------------------ lookup

        private static PlanetFactory ResolveFactory(int astroId)
        {
            GameData data = GameMain.data;
            if (data == null || data.galaxy == null) return null;
            PlanetData planet = data.galaxy.PlanetById(astroId);
            return planet != null ? planet.factory : null;
        }

        private static StationComponent ResolveStation(int astroId, int entityId)
        {
            PlanetFactory factory = ResolveFactory(astroId);
            if (factory == null) return null;
            if (entityId <= 0 || entityId >= factory.entityPool.Length) return null;
            int stationId = factory.entityPool[entityId].stationId;
            if (stationId <= 0 || stationId >= factory.transport.stationPool.Length) return null;
            StationComponent station = factory.transport.stationPool[stationId];
            return station != null && station.id == stationId ? station : null;
        }
    }

    [HarmonyPatch(typeof(UIControlPanelObjectEntry), "OnSelectButtonClick")]
    internal static class MultiSelectClickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UIControlPanelObjectEntry __instance)
        {
            if (!Cfg.Enabled.Value) return true;
            if (!(__instance is UIControlPanelStationEntry) && !(__instance is UIControlPanelAdvancedMinerEntry)) return true;
            try
            {
                if (VFInput.control)
                {
                    return !StationCopyPaste.ToggleMultiSelect(__instance);
                }
                if (VFInput.shift)
                {
                    if (StationCopyPaste.SelectRangeTo(__instance)) return false;
                    return true;
                }
                // a plain click changes the single selection and drops the multi selection
                StationCopyPaste.ClearMultiSelect();
                return true;
            }
            catch (Exception e)
            {
                CppLog.Error("multi-select click failed: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationEntry), "_OnUpdate")]
    internal static class StationEntryHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIControlPanelStationEntry __instance)
        {
            if (!Cfg.Enabled.Value || StationCopyPaste.MultiCount == 0) return;
            try
            {
                StationCopyPaste.ApplyHighlight(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("station highlight failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelAdvancedMinerEntry), "_OnUpdate")]
    internal static class MinerEntryHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIControlPanelAdvancedMinerEntry __instance)
        {
            if (!Cfg.Enabled.Value || StationCopyPaste.MultiCount == 0) return;
            try
            {
                StationCopyPaste.ApplyHighlight(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("miner highlight failed: " + e);
            }
        }
    }
}
