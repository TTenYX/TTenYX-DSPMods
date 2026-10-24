using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ControlPanelPlus
{
    /// <summary>Reflection access to the private result lists of the control panel window.</summary>
    internal static class WindowAccess
    {
        private static readonly FieldInfo ResultsField = AccessTools.Field(typeof(UIControlPanelWindow), "results");
        private static readonly FieldInfo PositionsField = AccessTools.Field(typeof(UIControlPanelWindow), "resultPositions");
        private static readonly FieldInfo EntriesField = AccessTools.Field(typeof(UIControlPanelWindow), "resultEntries");
        private static readonly FieldInfo NeedVisibleField = AccessTools.Field(typeof(UIControlPanelWindow), "needDetermineEntryVisible");

        public static List<ControlPanelTarget> Results(UIControlPanelWindow w)
        {
            return w != null && ResultsField != null ? ResultsField.GetValue(w) as List<ControlPanelTarget> : null;
        }

        public static List<int> Positions(UIControlPanelWindow w)
        {
            return w != null && PositionsField != null ? PositionsField.GetValue(w) as List<int> : null;
        }

        public static List<UIControlPanelObjectEntry> Entries(UIControlPanelWindow w)
        {
            return w != null && EntriesField != null ? EntriesField.GetValue(w) as List<UIControlPanelObjectEntry> : null;
        }

        public static void MarkEntryVisibleDirty(UIControlPanelWindow w)
        {
            if (w == null || NeedVisibleField == null) return;
            NeedVisibleField.SetValue(w, true);
        }

        /// <summary>
        /// Removes one result entry together with its cached start position and pooled entry slot.
        /// Mirrors what the vanilla code does inside RemoveLastFilterResult, generalised to any index.
        /// </summary>
        public static void RemoveResultAt(UIControlPanelWindow w, int index)
        {
            List<ControlPanelTarget> results = Results(w);
            List<int> positions = Positions(w);
            List<UIControlPanelObjectEntry> entries = Entries(w);
            if (results == null || positions == null || entries == null) return;
            if (index < 0 || index >= results.Count) return;

            int height = positions[index + 1] - positions[index];
            results.RemoveAt(index);
            entries.RemoveAt(index);
            positions.RemoveAt(index);
            for (int i = index; i < positions.Count; i++)
            {
                positions[i] -= height;
            }
        }
    }
}
