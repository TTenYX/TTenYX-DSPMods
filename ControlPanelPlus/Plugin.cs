using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ControlPanelPlus
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ControlPanelPlusPlugin : BaseUnityPlugin
    {
        public static ControlPanelPlusPlugin Instance { get; private set; }

        public static BepInEx.Logging.ManualLogSource Log { get; private set; }

        internal static Harmony Harmony { get; private set; }

        private HotkeyDriver _driver;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            CppLog.Verbose = false;

            Cfg.Bind(Config);

            Harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            Harmony.PatchAll(typeof(ControlPanelPlusPlugin).Assembly);

            _driver = gameObject.AddComponent<HotkeyDriver>();

            CppLog.Info($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded.");
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();
            Harmony = null;
        }
    }
}
