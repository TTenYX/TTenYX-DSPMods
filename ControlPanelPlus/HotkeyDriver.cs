using System;
using UnityEngine;

namespace ControlPanelPlus
{
    /// <summary>
    /// Single input pump for every hotkey of the mod. Runs only while the control panel is open and no
    /// text input is focused, so it never steals keys from other windows or from text entry.
    /// </summary>
    internal class HotkeyDriver : MonoBehaviour
    {
        private void Update()
        {
            if (!Cfg.Enabled.Value) return;
            if (UIRoot.instance == null || UIRoot.instance.uiGame == null) return;
            if (GameMain.data == null) return;

            UIControlPanelWindow window = UIRoot.instance.uiGame.controlPanelWindow;
            if (window == null || !window.active) return;

            HotkeyDiagnostics.Tick(window);

            // the panel may stay open while the starmap (opened by "locate") is in front; never
            // handle panel hotkeys while any full screen UI is active
            if (VFInput.inFullscreenGUI) return;

            // configuration changes (ConfigurationManager) refresh the panel immediately
            ConfigWatcher.Poll(window);

            if (NativeUi.IsInputFocused(window)) return;

            try
            {
                HandleStationCopyPaste(window);
                AstroListTweaks.HandleHotkeys(window);
            }
            catch (Exception e)
            {
                CppLog.Error("hotkey handling failed: " + e);
            }
        }

        private void LateUpdate()
        {
            if (!Cfg.Enabled.Value) return;
            // runs after every right-click handler of this frame (UXAssist may have replaced the
            // filter meanwhile); Shift+right-click appends to the filter snapshot of the last frame
            ItemFilterShortcuts.Tick();
        }

        private void HandleStationCopyPaste(UIControlPanelWindow window)
        {
            KeyCode copyKey = Cfg.CopyKey.Value;
            KeyCode pasteKey = Cfg.PasteKey.Value;
            UIControlPanelStationInspector inspector = window.currentInspector as UIControlPanelStationInspector;
            bool hasStation = inspector != null && inspector.station != null && inspector.station.id > 0;

            if (copyKey != KeyCode.None && copyKey != pasteKey && Input.GetKeyDown(copyKey))
            {
                if (hasStation)
                {
                    StationCopyPaste.CopyFromSelection(window);
                }
                else
                {
                    CppLog.Debug("copy key pressed without a station inspector selection");
                }
            }

            if (pasteKey != KeyCode.None && Input.GetKeyDown(pasteKey))
            {
                if (hasStation || StationCopyPaste.MultiCount > 0)
                {
                    StationCopyPaste.Paste(window, VFInput.shift);
                }
                else
                {
                    CppLog.Debug("paste key pressed without a station selection");
                }
            }
        }
    }
}
