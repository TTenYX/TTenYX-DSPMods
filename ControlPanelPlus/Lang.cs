using UnityEngine;

namespace ControlPanelPlus
{
    /// <summary>
    /// Every user facing string of the mod. The game language decides whether Chinese or English is
    /// shown (Chinese game -> Chinese, anything else -> English). Configuration descriptions and the
    /// README are bilingual instead, because they are read outside the game.
    /// </summary>
    public static class Lang
    {
        /// <summary>Self test hook: forces the language when not null (true = Chinese).</summary>
        public static bool? LanguageOverride = null;

        public static bool Chinese
        {
            get
            {
                if (LanguageOverride.HasValue) return LanguageOverride.Value;
                try
                {
                    return Localization.isZHCN;
                }
                catch (System.Exception)
                {
                    return true;
                }
            }
        }

        public static string T(string zh, string en)
        {
            return Chinese ? zh : en;
        }
    }
}
