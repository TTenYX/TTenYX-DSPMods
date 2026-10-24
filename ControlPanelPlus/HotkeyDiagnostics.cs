using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ControlPanelPlus
{
    /// <summary>
    /// Support aid: when a mod hotkey is pressed while the control panel is open but the pump is
    /// gated (full screen UI in front / a text field has focus), log the gate state at Info level so
    /// a "hotkey does nothing" report can be diagnosed from one session. Normal presses only emit a
    /// Debug line.
    /// </summary>
    internal static class HotkeyDiagnostics
    {
        private static float _nextLogAt;

        public static void Tick(UIControlPanelWindow window)
        {
            if (window == null) return;
            if (!AnyHotkeyPressed()) return;
            if (Time.unscaledTime < _nextLogAt) return;
            _nextLogAt = Time.unscaledTime + 0.4f;

            try
            {
                bool blocked = VFInput.inFullscreenGUI || NativeUi.IsInputFocused(window);
                if (!blocked)
                {
                    CppLog.Debug("[HOTKEY] " + Describe(window));
                    return;
                }
                CppLog.Info("[HOTKEY BLOCKED] " + Describe(window));
            }
            catch (Exception e)
            {
                CppLog.Error("hotkey diagnostics failed: " + e.Message);
            }
        }

        public static bool AnyHotkeyPressed()
        {
            return Pressed(Cfg.CopyKey.Value)
                   || Pressed(Cfg.PasteKey.Value)
                   || Pressed(Cfg.ListUp.Value)
                   || Pressed(Cfg.ListDown.Value)
                   || Pressed(KeyCode.LeftArrow) || Pressed(KeyCode.RightArrow)
                   || Pressed(KeyCode.UpArrow) || Pressed(KeyCode.DownArrow);
        }

        private static bool Pressed(KeyCode key)
        {
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        private static string Describe(UIControlPanelWindow w)
        {
            UIGame ui = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIControlPanelFilterPanel fp = w.filterPanel;
            UIControlPanelStationInspector inspector = w.currentInspector as UIControlPanelStationInspector;
            bool nameFocused = inspector != null && inspector.nameInput != null && inspector.nameInput.isFocused;
            bool tagFocused = fp != null && fp.tagFilterInput != null && fp.tagFilterInput.isFocused;
            bool searchFocused = fp != null && fp.searchFilterInput != null && fp.searchFilterInput.isFocused;
            bool comboFocused = fp != null && fp.astroFilterBox != null && fp.astroFilterBox.searchInput != null && fp.astroFilterBox.searchInput.isFocused;
            GameObject selected = null;
            EventSystem es = EventSystem.current;
            if (es != null) selected = es.currentSelectedGameObject;

            string hotkeys = "copy=" + Cfg.CopyKey.Value + " paste=" + Cfg.PasteKey.Value
                             + " pageUp=" + Cfg.ListUp.Value + " pageDown=" + Cfg.ListDown.Value;
            return "panel=" + w.active
                   + " fullscreen=" + VFInput.inFullscreenGUI
                   + " paused=" + GameMain.isPaused
                   + " dash=" + (ui != null && ui.dashboard.active)
                   + " tree=" + (ui != null && ui.techTree.active)
                   + " star=" + (ui != null && ui.starmap.active)
                   + " dyson=" + (ui != null && ui.dysonEditor.active)
                   + " focused=" + NativeUi.IsInputFocused(w)
                   + " nameFocused=" + nameFocused
                   + " tagFocused=" + tagFocused
                   + " searchFocused=" + searchFocused
                   + " comboFocused=" + comboFocused
                   + " inspector=" + (w.currentInspector != null ? w.currentInspector.GetType().Name : "null")
                   + " selection=" + w.selection.astroId + "/" + w.selection.objId
                   + " esSel=" + (selected != null ? selected.name : "null")
                   + " | " + hotkeys;
        }
    }
}
