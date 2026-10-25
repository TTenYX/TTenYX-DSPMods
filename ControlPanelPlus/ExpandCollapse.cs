using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ControlPanelPlus
{
    /// <summary>
    /// F1: default expand/collapse policy for planet entries + the two "expand all / collapse all"
    /// buttons in the window title bar.
    ///
    /// Expand state lives in <see cref="UIControlPanelWindow.astroExpands"/>. For non-local planets a
    /// planet is expanded when its astroId is IN the set; the local planet uses the sentinel key 0 and
    /// is expanded when 0 is NOT in the set. Any change must be paired with
    /// <see cref="UIControlPanelWindow.SetPlanetEntryExpand"/> so the list heights are recalculated.
    /// </summary>
    public static class ExpandCollapse
    {
        private const int LocalKey = 0;

        private static readonly FieldInfo ResultsField = AccessTools.Field(typeof(UIControlPanelWindow), "results");

        private static string _appliedSignature;
        private static bool _sessionOpened;
        private static bool _skipDefaultsForThisOpen;

        public static List<ControlPanelTarget> Results(UIControlPanelWindow w)
        {
            if (w == null || ResultsField == null) return null;
            return ResultsField.GetValue(w) as List<ControlPanelTarget>;
        }

        public sealed class PlanetRow
        {
            public int astroId;
            public int index;
        }

        public static List<PlanetRow> PlanetRows(UIControlPanelWindow w)
        {
            List<PlanetRow> rows = new List<PlanetRow>();
            List<ControlPanelTarget> results = Results(w);
            if (results == null) return rows;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].entryType != EControlPanelEntryType.None)
                {
                    continue;
                }
                PlanetRow row = new PlanetRow();
                row.astroId = results[i].astroId;
                row.index = i;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Called from the _OnOpen prefix - before the window rebuilds its result list - so the
        /// "keep the previous expand state on re-open" flag is in place when the defaults would run.
        /// </summary>
        public static void OnWindowOpening()
        {
            _appliedSignature = null;
            _skipDefaultsForThisOpen = _sessionOpened;
            _sessionOpened = true;
        }

        /// <summary>Called when a game is initialised so the "first open" state starts fresh.</summary>
        public static void OnGameInit()
        {
            _sessionOpened = false;
            _skipDefaultsForThisOpen = false;
            _appliedSignature = null;
            _toggleButton = null;
        }

        public static void OnWindowOpened(UIControlPanelWindow w)
        {
            EnsureTopButtons(w);
        }

        public static void OnWindowClosed()
        {
            _appliedSignature = null;
        }

        /// <summary>Called right after the vanilla result list has been rebuilt (and after F4 filtering).</summary>
        public static void AfterResults(UIControlPanelWindow w)
        {
            if (w == null) return;
            List<PlanetRow> planets = PlanetRows(w);
            string signature = Signature(w, planets);
            if (signature == _appliedSignature)
            {
                return;
            }
            _appliedSignature = signature;

            if (_skipDefaultsForThisOpen)
            {
                _skipDefaultsForThisOpen = false;
                UpdateToggleLabel(w);
                CppLog.Debug("panel reopened: keeping the previous planet expand state");
                return;
            }

            if (planets.Count == 0)
            {
                UpdateToggleLabel(w);
                return;
            }

            bool single = planets.Count == 1;
            bool enabled = single ? Cfg.ExpandDefaultSingle.Value : Cfg.CollapseAllDefaultMulti.Value;
            if (!enabled) return;

            bool expand = single;
            Apply(w, planets, expand, single ? "单星球默认展开" : "多星球默认折叠");
            UpdateToggleLabel(w);
        }

        private static string Signature(UIControlPanelWindow w, List<PlanetRow> planets)
        {
            StringBuilder sb = new StringBuilder(128);
            ControlPanelFilter filter = w.filter;
            sb.Append((int)filter.typeFilter).Append('|')
              .Append(filter.astroFilter).Append('|')
              .Append((int)filter.sortMethod).Append('|')
              .Append(filter.stateFilter).Append('|')
              .Append(filter.tagFilter ?? "").Append('|')
              .Append(filter.searchFilter ?? "");
            if (filter.itemsFilter != null)
            {
                sb.Append('|');
                for (int i = 0; i < filter.itemsFilter.Length; i++)
                {
                    sb.Append(filter.itemsFilter[i]).Append(',');
                }
            }
            sb.Append("|p:");
            for (int i = 0; i < planets.Count; i++)
            {
                sb.Append(planets[i].astroId).Append(',');
            }
            return sb.ToString();
        }

        public static void Apply(UIControlPanelWindow w, List<PlanetRow> planets, bool expand, string reason)
        {
            int localAstroId = LocalAstroId();
            int changed = 0;
            for (int i = 0; i < planets.Count; i++)
            {
                PlanetRow row = planets[i];
                bool isLocal = row.astroId == localAstroId && localAstroId != 0;
                bool current = IsExpanded(w, row.astroId, isLocal);
                if (current == expand) continue;
                SetExpandState(w, row.astroId, isLocal, expand);
                w.SetPlanetEntryExpand(row.index, expand);
                changed++;
            }
            CppLog.Debug($"{reason}: planets={planets.Count} changed={changed}");
        }

        private static int LocalAstroId()
        {
            PlanetData planet = GameMain.data != null ? GameMain.data.localPlanet : null;
            return planet != null ? planet.astroId : 0;
        }

        public static bool IsExpanded(UIControlPanelWindow w, int astroId, bool isLocal)
        {
            if (isLocal) return !w.astroExpands.Contains(LocalKey);
            return w.astroExpands.Contains(astroId);
        }

        private static void SetExpandState(UIControlPanelWindow w, int astroId, bool isLocal, bool expand)
        {
            if (isLocal)
            {
                if (expand) w.astroExpands.Remove(LocalKey);
                else w.astroExpands.Add(LocalKey);
            }
            else
            {
                if (expand) w.astroExpands.Add(astroId);
                else w.astroExpands.Remove(astroId);
            }
        }

        // ---------------------------------------------------------------- top button

        private const string ToggleButtonName = "cpt-expand-toggle";
        private static UIButton _toggleButton;

        private static void EnsureTopButtons(UIControlPanelWindow w)
        {
            if (w == null) return;
            Transform existing = w.transform.Find(ToggleButtonName);
            if (existing != null)
            {
                _toggleButton = existing.GetComponent<UIButton>();
                UpdateToggleLabel(w);
                return;
            }

            UIButton button = NativeUi.MakeSmallTextButton(Lang.T("全部展开", "Expand All"), new Vector2(72f, 22f), 13, ToggleButtonName);
            if (button == null) return;
            button.transform.SetParent(w.transform, worldPositionStays: false);
            NativeUi.AnchorTopRight((RectTransform)button.transform, -43f, -25f, new Vector2(72f, 22f));
            button.onClick += OnToggleClick;
            _toggleButton = button;
            UpdateToggleLabel(w);
            CppLog.Debug("expand/collapse toggle button injected");
        }

        private static bool AllRowsExpanded(UIControlPanelWindow w, List<PlanetRow> planets)
        {
            bool all = planets.Count > 0;
            for (int i = 0; i < planets.Count; i++) all &= IsRowExpanded(w, planets[i]);
            return all;
        }

        private static bool IsRowExpanded(UIControlPanelWindow w, PlanetRow row)
        {
            int localAstroId = LocalAstroId();
            bool isLocal = row.astroId == localAstroId && localAstroId != 0;
            return IsExpanded(w, row.astroId, isLocal);
        }

        /// <summary>Updates the label of the single expand/collapse toggle button.</summary>
        public static void UpdateToggleLabel(UIControlPanelWindow w)
        {
            if (_toggleButton == null || w == null) return;
            Text text = NativeUi.TextOf(_toggleButton);
            if (text == null) return;
            bool allExpanded = AllRowsExpanded(w, PlanetRows(w));
            text.text = allExpanded ? Lang.T("全部折叠", "Collapse All") : Lang.T("全部展开", "Expand All");
        }

        private static void OnToggleClick(int obj)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                UIControlPanelWindow w = NativeUi.CurrentWindow();
                if (w == null || !w.active) return;
                List<PlanetRow> planets = PlanetRows(w);
                bool allExpanded = AllRowsExpanded(w, planets);
                _appliedSignature = Signature(w, planets);
                Apply(w, planets, !allExpanded, allExpanded ? "全部折叠按钮" : "全部展开按钮");
                UpdateToggleLabel(w);
            }
            catch (Exception e)
            {
                CppLog.Error("expand/collapse toggle failed: " + e);
            }
        }

        public static void SetAll(bool expand)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                UIControlPanelWindow w = NativeUi.CurrentWindow();
                if (w == null || !w.active) return;
                List<PlanetRow> planets = PlanetRows(w);
                _appliedSignature = Signature(w, planets);
                Apply(w, planets, expand, expand ? "全部展开按钮" : "全部折叠按钮");
                UpdateToggleLabel(w);
            }
            catch (Exception e)
            {
                CppLog.Error("expand/collapse all failed: " + e);
            }
        }
    }
}
