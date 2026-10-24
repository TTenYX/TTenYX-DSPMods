using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ComboBox = UnityEngine.UI.Youthcat.UIComboBox;

namespace ControlPanelPlus
{
    /// <summary>
    /// F5: star/planet name sorting, custom prefix/postfix (rich text allowed) and the PageUp/PageDown
    /// list hotkeys for the "location" combo box of the filter panel.
    ///
    /// The vanilla box is rebuilt from scratch by UIControlPanelFilterPanel.ReconstructAstroFilterBox;
    /// its first two entries are always "all systems" (value -1) and "current planet/system" (value 0).
    /// Entries use astroId encoding: star = starIndex * 100, planet = starId + planetIndex.
    /// </summary>
    public static class AstroListTweaks
    {
        private sealed class StarGroup
        {
            public int starIndex;
            public readonly List<ComboBox.EntryData> entries = new List<ComboBox.EntryData>();

            public string SortName()
            {
                return entries.Count > 0 ? entries[0].text : "";
            }
        }

        public static void Rebuild(UIControlPanelFilterPanel panel)
        {
            if (panel == null) return;
            ComboBox box = panel.astroFilterBox;
            if (box == null) return;

            NativeUi.EnableRichText(box);

            // StatsUITweaks-like distinction: stars are yellow, planets are indented.
            const string starPrefix = "<color=yellow>";
            const string starPostfix = "</color>";
            const string planetPrefix = "  ";

            bool wantSort = Cfg.SortByName.Value;
            if (!wantSort) return;

            int count = box.entryCount;
            if (count <= 2) return;

            List<ComboBox.EntryData> all = new List<ComboBox.EntryData>(count);
            for (int i = 0; i < count; i++)
            {
                all.Add(box.GetEntry(i));
            }
            if (all.Count <= 2) return;

            List<StarGroup> groups = new List<StarGroup>();
            Dictionary<int, StarGroup> byStar = new Dictionary<int, StarGroup>();
            for (int i = 2; i < all.Count; i++)
            {
                ComboBox.EntryData entry = all[i];
                if (entry == null) continue;
                int starIndex = entry.value / 100;
                StarGroup group;
                if (!byStar.TryGetValue(starIndex, out group))
                {
                    group = new StarGroup();
                    group.starIndex = starIndex;
                    byStar[starIndex] = group;
                    groups.Add(group);
                }
                group.entries.Add(entry);
            }

            if (wantSort)
            {
                groups.Sort((a, b) => string.Compare(a.SortName(), b.SortName(), StringComparison.CurrentCultureIgnoreCase));
            }

            int selectedValue = int.MinValue;
            if (box.selectedIndex >= 0)
            {
                ComboBox.EntryData selected = box.GetEntry(box.selectedIndex);
                if (selected != null) selectedValue = selected.value;
            }

            bool events = box.onChangeEventEnabled;
            box.onChangeEventEnabled = false;
            try
            {
                box.ClearEntries();
                box.AddEntry(all[0]);
                box.AddEntry(all[1]);
                for (int g = 0; g < groups.Count; g++)
                {
                    StarGroup group = groups[g];
                    for (int e = 0; e < group.entries.Count; e++)
                    {
                        ComboBox.EntryData entry = group.entries[e];
                        bool isStar = entry.value > 0 && entry.value % 100 == 0;
                        entry.text = isStar
                            ? starPrefix + entry.text + starPostfix
                            : planetPrefix + entry.text;
                        box.AddEntry(entry);
                    }
                }

                if (selectedValue != int.MinValue)
                {
                    for (int i = 0; i < box.entryCount; i++)
                    {
                        ComboBox.EntryData entry = box.GetEntry(i);
                        if (entry != null && entry.value == selectedValue)
                        {
                            box.selectedIndex = i;
                            break;
                        }
                    }
                }
            }
            finally
            {
                box.onChangeEventEnabled = events;
            }

            CppLog.Debug($"astro list rebuilt: groups={groups.Count} entries={box.entryCount} sorted={wantSort}");
        }

        // ------------------------------------------------------------------ hotkeys

        public static void HandleHotkeys(UIControlPanelWindow w)
        {
            UIControlPanelFilterPanel panel = w.filterPanel;
            if (panel == null || panel.astroFilterBox == null) return;

            KeyCode up = Cfg.ListUp.Value;
            KeyCode down = Cfg.ListDown.Value;
            if (up != KeyCode.None && down != KeyCode.None && up == down)
            {
                // misconfiguration: only honour the down key
                if (Input.GetKeyDown(down)) StepList(panel, 1);
            }
            else
            {
                if (up != KeyCode.None && Input.GetKeyDown(up)) StepList(panel, -1);
                if (down != KeyCode.None && Input.GetKeyDown(down)) StepList(panel, 1);
            }

            // Arrow keys step aside while the pointer is over a logistic storage slot, because
            // UXAssist's "logistic storage capacity" tweak uses the arrows there.
            if (NativeUi.MouseOverStorageSlot()) return;

            if (Input.GetKeyDown(KeyCode.LeftArrow)) StepPlanet(w, -1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) StepPlanet(w, 1);
            if (Input.GetKeyDown(KeyCode.UpArrow)) StepSelection(w, -1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) StepSelection(w, 1);
        }

        public static void StepList(UIControlPanelFilterPanel panel, int direction)
        {
            ComboBox box = panel.astroFilterBox;
            int count = box.entryCount;
            if (count <= 0) return;

            int index = box.selectedIndex;
            if (index < 0) index = direction > 0 ? -1 : 0;
            bool onlyStars = VFInput.control;

            for (int guard = 0; guard <= count; guard++)
            {
                index += direction;
                if (index < 0) index = count - 1;
                if (index >= count) index = 0;
                if (!onlyStars) break;
                ComboBox.EntryData entry = box.GetEntry(index);
                if (entry == null) continue;
                if (entry.value == -1 || (entry.value > 0 && entry.value % 100 == 0)) break;
            }

            if (index < 0 || index >= count) return;
            box.selectedIndex = index;
            CppLog.Debug($"astro list hotkey direction={direction} -> index={index}");
        }

        /// <summary>
        /// Left/Right: jump to the first matching entry of the previous / next planet in the result
        /// list (`direction` = -1 / +1). Planets the current filter does not show are skipped; the
        /// walk wraps around.
        /// </summary>
        public static void StepPlanet(UIControlPanelWindow w, int direction)
        {
            List<ControlPanelTarget> results = WindowAccess.Results(w);
            if (results == null || results.Count == 0) return;
            int allowed = (int)w.filter.typeFilter;

            List<int> planets = new List<int>();
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].entryType != EControlPanelEntryType.None) continue;
                if (FirstEntryOfPlanet(results, results[i].astroId, allowed, out _)) planets.Add(results[i].astroId);
            }
            if (planets.Count == 0) return;

            int anchor = AnchorAstroId(w, results);
            int position = planets.IndexOf(anchor);
            if (position < 0) position = direction > 0 ? -1 : 0;

            int next = position + direction;
            if (next < 0) next = planets.Count - 1;
            if (next >= planets.Count) next = 0;

            if (FirstEntryOfPlanet(results, planets[next], allowed, out ControlPanelTarget target))
            {
                SelectTarget(w, target);
                CppLog.Debug($"planet hotkey direction={direction} anchor={anchor} -> planet={planets[next]}");
            }
        }

        private static bool FirstEntryOfPlanet(List<ControlPanelTarget> results, int astroId, int allowed, out ControlPanelTarget target)
        {
            for (int i = 0; i < results.Count; i++)
            {
                ControlPanelTarget candidate = results[i];
                if (candidate.entryType == EControlPanelEntryType.None) continue;
                if (candidate.astroId != astroId) continue;
                if ((allowed & TypeBit(candidate.entryType)) == 0) continue;
                target = candidate;
                return true;
            }
            target = ControlPanelTarget.none;
            return false;
        }

        public static void StepSelection(UIControlPanelWindow w, int direction)
        {
            List<ControlPanelTarget> results = WindowAccess.Results(w);
            if (results == null || results.Count == 0) return;

            int current = -1;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] == w.selection)
                {
                    current = i;
                    break;
                }
            }

            int index = current;
            for (int guard = 0; guard < results.Count; guard++)
            {
                if (current < 0)
                {
                    index = direction > 0 ? 0 : results.Count - 1;
                }
                else
                {
                    index += direction;
                }
                if (index < 0) index = results.Count - 1;
                if (index >= results.Count) index = 0;
                if (results[index].entryType == EControlPanelEntryType.None) continue;
                SelectTarget(w, results[index]);
                return;
            }
        }

        private static void SelectTarget(UIControlPanelWindow w, ControlPanelTarget target)
        {
            w.selection = target;
            try
            {
                w.FocusToEntryByObjId(target.astroId, target.objId, target.objType);
            }
            catch (Exception e)
            {
                CppLog.Warn("focus entry failed: " + e.Message);
            }
        }

        private static int AnchorAstroId(UIControlPanelWindow w, List<ControlPanelTarget> results)
        {
            ControlPanelTarget selection = w.selection;
            if (!selection.isNull && selection.astroId != 0) return selection.astroId;

            PlanetData local = GameMain.data != null ? GameMain.data.localPlanet : null;
            if (local != null) return local.astroId;

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].entryType == EControlPanelEntryType.None) return results[i].astroId;
            }
            return 0;
        }

        private static int TypeBit(EControlPanelEntryType type)
        {
            switch (type)
            {
                case EControlPanelEntryType.InterstellarStation: return 2;
                case EControlPanelEntryType.OrbitCollector: return 4;
                case EControlPanelEntryType.LocalStation: return 8;
                case EControlPanelEntryType.VeinCollector: return 0x10;
                case EControlPanelEntryType.Dispenser: return 0x20;
                case EControlPanelEntryType.Marker: return 0x40;
                default: return 0;
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelFilterPanel))]
    internal static class AstroFilterBoxPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("ReconstructAstroFilterBox")]
        public static void AfterReconstruct(UIControlPanelFilterPanel __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                AstroListTweaks.Rebuild(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("astro list rebuild failed: " + e);
            }
        }
    }
}
