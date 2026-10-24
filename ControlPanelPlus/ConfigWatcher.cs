using System;

namespace ControlPanelPlus
{
    /// <summary>
    /// Watches the two configuration switches whose effect is not naturally re-evaluated by the game
    /// (item filter intersection, astro list sorting). When one of them is changed at runtime
    /// (ConfigurationManager) the panel is refreshed immediately instead of waiting for the next
    /// filter switch or panel reopen.
    /// </summary>
    public static class ConfigWatcher
    {
        private static bool _initialized;
        private static bool _intersect;
        private static bool _sort;

        public static void Reset()
        {
            _initialized = false;
        }

        public static void Poll(UIControlPanelWindow window)
        {
            if (window == null || !window.active) return;

            bool intersect = Cfg.ItemFilterIntersect.Value;
            bool sort = Cfg.SortByName.Value;

            if (!_initialized)
            {
                _initialized = true;
                _intersect = intersect;
                _sort = sort;
                return;
            }

            bool filterModeChanged = intersect != _intersect;
            bool sortChanged = sort != _sort;

            _intersect = intersect;
            _sort = sort;

            try
            {
                if (filterModeChanged)
                {
                    window.DetermineFilterResults();
                    window.DetermineEntryVisible();
                    CppLog.Info("item filter mode switched at runtime, result list refreshed");
                }
                if (sortChanged)
                {
                    UIControlPanelFilterPanel panel = window.filterPanel;
                    if (panel != null)
                    {
                        panel.ReconstructAstroFilterBox();
                        panel.RefreshAstroFilterBox();
                        CppLog.Info("astro list sorting switched at runtime, dropdown rebuilt");
                    }
                }
            }
            catch (Exception e)
            {
                CppLog.Error("config change refresh failed: " + e);
            }
        }
    }
}
