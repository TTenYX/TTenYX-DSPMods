using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace ControlPanelPlus
{
    /// <summary>
    /// Detects compatible plugins whose features take precedence over ours (round 3 requirements).
    /// The override fields are self test hooks only - they are never written by production code.
    /// </summary>
    public static class PluginPresence
    {
        public const string CheatEnablerGuid = "org.soardev.cheatenabler";
        public const string UiTweaksGuid = "starfi5h.plugin.UITweaks";

        private static bool? _cheatEnabler;
        private static bool? _uiTweaks;

        /// <summary>Self test hook: forces the CheatEnabler presence result when not null.</summary>
        public static bool? CheatEnablerPresenceOverride = null;

        /// <summary>Self test hook: forces the UITweaks presence result when not null.</summary>
        public static bool? UiTweaksPresenceOverride = null;

        public static bool CheatEnablerInstalled
        {
            get
            {
                if (CheatEnablerPresenceOverride.HasValue) return CheatEnablerPresenceOverride.Value;
                if (!_cheatEnabler.HasValue) _cheatEnabler = Has(CheatEnablerGuid);
                return _cheatEnabler.Value;
            }
        }

        public static bool UiTweaksInstalled
        {
            get
            {
                if (UiTweaksPresenceOverride.HasValue) return UiTweaksPresenceOverride.Value;
                if (!_uiTweaks.HasValue) _uiTweaks = Has(UiTweaksGuid);
                return _uiTweaks.Value;
            }
        }

        private static bool Has(string guid)
        {
            try
            {
                return Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(guid);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
