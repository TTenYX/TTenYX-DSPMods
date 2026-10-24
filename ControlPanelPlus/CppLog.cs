namespace ControlPanelPlus
{
    /// <summary>
    /// Single funnel for the mod's log output. <see cref="Verbose"/> is switched off for the
    /// release build after the in-game self tests are green; warnings/errors always go out.
    /// </summary>
    internal static class CppLog
    {
        public const string Prefix = "[CPP] ";

        /// <summary>Step-by-step tracing. Turned off for release once the self test is green.</summary>
        public static bool Verbose = true;

        public static void Debug(string message)
        {
            if (Verbose) ControlPanelPlusPlugin.Log?.LogInfo(Prefix + message);
        }

        public static void Info(string message)
        {
            ControlPanelPlusPlugin.Log?.LogInfo(Prefix + message);
        }

        public static void Warn(string message)
        {
            ControlPanelPlusPlugin.Log?.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            ControlPanelPlusPlugin.Log?.LogError(Prefix + message);
        }
    }
}
