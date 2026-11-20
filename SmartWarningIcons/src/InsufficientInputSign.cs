using BepInEx.Configuration;

namespace SmartWarningIcons
{
    /// <summary>
    /// 「缺原料」警告（红色，signType 31）：建筑设了配方、因缺原料停工时，用红色图标替代原版笼统的「不工作」。
    /// 生产停工且未到周期完成点时，按已入库原料判定；研究模式按当前科技需要的矩阵点数判定。
    /// 与黄色警告共用帧末扫描；有少量积货时，仍可正常提示缺原料。
    /// </summary>
    internal static class InsufficientInputSign
    {
        private static ConfigEntry<bool> _enable;

        internal static void Init(ConfigFile config)
        {
            _enable = config.Bind(
                "InsufficientInput",
                "Enable",
                true,
                "Mark buildings that have a recipe but lack input materials with a dedicated red warning icon instead of the generic not-working icon.\n" +
                "给设置了配方但缺少原料的建筑显示专用的红色「缺原料」图标，替代笼统的「不工作」图标。");
        }

        /// <summary>红色缺原料扫描开关。</summary>
        internal static bool Enabled => _enable.Value;
    }
}
