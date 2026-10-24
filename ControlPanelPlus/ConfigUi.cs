namespace ControlPanelPlus
{
    /// <summary>
    /// Duck-typed tag object read by BepInEx.ConfigurationManager (it matches the type name and
    /// copies same-named fields into its SettingEntryBase). The UI sorts entries of a section by
    /// Order descending, then display name - so a larger value is shown first.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public int Order;
    }
}
