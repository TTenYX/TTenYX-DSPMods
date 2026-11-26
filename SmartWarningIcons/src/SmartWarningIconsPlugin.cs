using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SmartWarningIcons
{
    /// <summary>
    /// 智能警告图标：把原版笼统的「不工作」拆成两种可区分的专用警告——
    /// 红色「缺原料」（signType 31）与黄色「产物堆砌」（signType 25），
    /// 并把矿脉枯竭警告改成可配置阈值 + 可选按矿脉总量判断。
    /// 思路来自 Raptor 的 BetterWarningIcons（游戏 0.10.34 起停更，适配 PR 未合并）；
    /// 本插件为独立重写实现，代码与图标均为原创，未使用上游素材。
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class SmartWarningIconsPlugin : BaseUnityPlugin
    {
        public const string Guid = "TTenYX.DSP.SmartWarningIcons";
        public const string Name = "SmartWarningIcons";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        internal static string PluginDir;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            PluginDir = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(PluginDir))
                PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

            InsufficientInputSign.Init(Config);
            OutputPileSign.Init(Config);
            VeinDepletionSign.Init(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(SignIcons));
            _harmony.PatchAll(typeof(OutputPileSign));

            Log.LogInfo($"{Name} {Version} loaded (GUID: {Guid})");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            SignIcons.Dispose();
            Log = null;
        }
    }
}
