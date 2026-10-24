using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SmartDustbin
{
    /// <summary>
    /// 垃圾桶 (SmartDustbin)
    ///
    /// 功能 1 —— 储物仓 / 储液罐垃圾桶：
    ///   小型/大型储物仓把「自动化输入限制」滑条拉到 0（可用格数 0）时，储物仓变为垃圾桶，
    ///   消耗所有输入的物品：手动放入、分拣器输入、以及储物仓上方的传送带末端 / 四向分流器落料。
    ///   储液罐把「输入」开关设为关时，消耗所有经传送带与手动注入的液体（只消耗液体类物品）。
    ///
    /// 功能 2 —— 传送带图标标签消耗：
    ///   给传送带设置图标标签信号 SIGNAL-404（原版信号，ID 404）后，消耗经过该传送带的物品。
    ///   标签数量为 0（默认）时全部消耗；填 N 时按「每统计周期 N 个」的速率消耗
    ///   （按物品个数计，实际整堆扣减：全是 4 堆叠货物时 N=600/min 平均每分钟消耗约 150 堆）。
    ///
    /// 判定条件全部使用原版已有状态（bans / inputSwitch / 图标标签），不写入存档，无任何前置 MOD。
    /// 全部配置见 BepInEx\config\TTenYX.DSP.SmartDustbin.cfg，修改后立即生效。
    /// </summary>
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SmartDustbin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "TTenYX.DSP.SmartDustbin";
        public const string PLUGIN_NAME = "SmartDustbin";
        public const string PLUGIN_VERSION = "0.2.0";

        private static readonly Harmony harmony = new Harmony(PLUGIN_GUID);
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);

            // 单个补丁类注册失败只记日志，不拖垮整个插件（游戏更新后目标方法可能变化）
            SafePatchAll(typeof(StorageVoidPatch), "功能1 储物仓");
            SafePatchAll(typeof(TankVoidPatch), "功能1 储液罐");
            SafePatchAll(typeof(BeltSignalVoidPatch), "功能2 传送带信号");

            // 配置热更新：储物仓判定缓存整体失效重建（含清理调用方的 lastFullItem 脏缓存）
            ModConfig.EnableStorageVoid.SettingChanged += (_, _) => StorageVoidPatch.InvalidateCache();
            ModConfig.EnableSmallStorage.SettingChanged += (_, _) => StorageVoidPatch.InvalidateCache();
            ModConfig.EnableLargeStorage.SettingChanged += (_, _) => StorageVoidPatch.InvalidateCache();

            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载（作者 TTenYX）");
            Log.LogInfo($"功能1={ModConfig.EnableStorageVoid.Value}（小型储物仓={ModConfig.EnableSmallStorage.Value} " +
                        $"大型储物仓={ModConfig.EnableLargeStorage.Value} 储液罐={ModConfig.EnableTank.Value}） " +
                        $"功能2={ModConfig.EnableBeltSignalVoid.Value} 统计周期={ModConfig.StatPeriodSeconds.Value:0.#}s");
        }

        private void SafePatchAll(Type patchClass, string label)
        {
            try
            {
                harmony.PatchAll(patchClass);
                Log.LogInfo($"补丁组已应用：{label}");
            }
            catch (Exception e)
            {
                Log.LogWarning($"注册补丁失败（{label}，不影响其它功能）：{e.Message}");
            }
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }
}
