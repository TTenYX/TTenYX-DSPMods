using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 黑雾工坊 (DarkFogWorkshop)
    ///
    /// 用物流塔把科学矩阵（玩家俗称"糖"）兑换成黑雾材料：
    /// - 第 1 格是科学矩阵，且该格被设为【本地需求】（行星内物流运输站 = 需求；
    ///   星际物流运输站 = 本地需求那一栏）；
    /// - 第 2/3/4 格中任意一格放了黑雾材料（能量碎片 / 黑雾矩阵 / 硅基神经元 /
    ///   负熵奇点 / 物质重组器 / 核心素）；
    /// 满足条件后，每帧按"算力"消耗第 1 格的矩阵，并在对应槽位产出黑雾材料。
    ///
    /// 算力公式（每帧单独结算，结余不结转）：
    ///   每帧算力 = U × Y        （U = MatrixPointPerFrame，Y = 科技研究速度）
    ///   矩阵点数 = 矩阵算力 × 消耗个数
    ///   黑雾材料算力 = X × 材料算力  （X = ExchangeFactor）
    ///
    /// 全部数值见 config\TTenYX.DSP.DarkFogWorkshop.cfg。
    /// </summary>
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class DarkFogWorkshop : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "TTenYX.DSP.DarkFogWorkshop";
        public const string PLUGIN_NAME = "DarkFogWorkshop";
        /// <summary>问号提示的标题。</summary>
        public const string HELP_TITLE = "黑雾工坊 DarkFogWorkshop";
        public const string PLUGIN_VERSION = "1.0.0";

        private static readonly Harmony harmony = new Harmony(PLUGIN_GUID);
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            // Awake 必定在主线程：记下主线程 id，供工作线程判断用。
            UIStationWindowPatch.CaptureMainThread();
            ModConfig.Bind(Config);

            harmony.PatchAll(typeof(DarkFogWorkshopPatch));
            harmony.PatchAll(typeof(UIStationWindowPatch));

            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载（作者 TTenYX）");
            Log.LogInfo($"U={ModConfig.MatrixPointPerFrame.Value}  X={ModConfig.ExchangeFactor.Value}  " +
                        $"Y={(ModConfig.UseGameTechSpeed.Value ? "存档科技研究速度" : ModConfig.TechSpeedPercent.Value + "%")}");
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        /// <summary>按当前语言返回中/英文本。</summary>
        internal static string Tr(string zh, string en)
        {
            return Localization.isZHCN ? zh : en;
        }

        /// <summary>物品名（游戏本地化，取不到就退回 ID）。</summary>
        internal static string ItemName(int itemId)
        {
            if (itemId <= 0)
            {
                return "-";
            }
            ItemProto proto = LDB.items.Select(itemId);
            return proto != null ? proto.name : itemId.ToString();
        }
    }
}
