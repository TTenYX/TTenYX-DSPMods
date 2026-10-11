using BepInEx.Configuration;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 全部数值都可在 BepInEx\config\TTenYX.DSP.DarkFogWorkshop.cfg 中调整。
    /// 除兑换系数 X 外，其余均为正整数（X 允许小数）。
    /// </summary>
    internal static class ModConfig
    {
        /// <summary>兑换系数 X：矩阵点数 = X × 黑雾材料算力，允许小数，&gt;0。</summary>
        internal static ConfigEntry<double> ExchangeFactor;
        /// <summary>矩阵转化算力速度 U（**每分钟**算力口径）。</summary>
        internal static ConfigEntry<int> MatrixPointPerMinute;
        /// <summary>矩阵转化算力速度 Y 的默认值（百分比，100 = 100%）。</summary>
        internal static ConfigEntry<int> TechSpeedPercent;
        /// <summary>true：Y 取存档的矩阵转化算力速度；false：用 TechSpeedPercent。</summary>
        internal static ConfigEntry<bool> UseGameTechSpeed;
        /// <summary>调试倍率，默认 1；&gt;1 会成倍放大每分钟算力。</summary>
        internal static ConfigEntry<double> SpeedMultiplier;
        /// <summary>每帧算力买不起 1 个矩阵时，是否攒零头凑够 1 个。</summary>
        internal static ConfigEntry<bool> AccumulatePartialPoints;
        /// <summary>详细诊断日志（逐槽位）。</summary>
        internal static ConfigEntry<bool> VerboseLog;

        /* ---- 科学矩阵算力 ---- */
        internal static ConfigEntry<int> ElectromagneticMatrixPoints;
        internal static ConfigEntry<int> EnergyMatrixPoints;
        internal static ConfigEntry<int> StructureMatrixPoints;
        internal static ConfigEntry<int> InformationMatrixPoints;
        internal static ConfigEntry<int> GravityMatrixPoints;
        internal static ConfigEntry<int> UniverseMatrixPoints;

        /* ---- 黑雾材料算力 ---- */
        internal static ConfigEntry<int> EnergyFragmentPoints;
        internal static ConfigEntry<int> DarkFogMatrixPoints;
        internal static ConfigEntry<int> SiliconNeuronPoints;
        internal static ConfigEntry<int> NegentropySingularityPoints;
        internal static ConfigEntry<int> MatterReassemblerPoints;
        internal static ConfigEntry<int> CoreElementPoints;

        /* ---- 其它 ---- */
        /// <summary>参与兑换的材料槽位上限（格号，2 = 只到第 2 格）。</summary>
        internal static ConfigEntry<int> OutputSlotLimit;
        internal static ConfigEntry<bool> DiagnosticLog;

        internal static void Bind(ConfigFile config)
        {
            const string general = "1. 通用";
            const string matrices = "2. 科学矩阵算力";
            const string materials = "3. 黑雾材料算力";
            const string ui = "4. 界面";

            ExchangeFactor = config.Bind(general, "ExchangeFactor", 1.0, new ConfigDescription(
                "兑换系数 X：矩阵点数 = X × 黑雾材料算力。可小数（如 0.5、2.5），必须大于 0。\n" +
                "X=1 时 1 个宇宙矩阵(3000) 可换 1 个核心素(3000) 或 60 个能量碎片(50)。",
                new AcceptableValueRange<double>(0.0001, 1000000.0)));

            MatrixPointPerMinute = config.Bind(general, "MatrixPointPerMinute", 7200, new ConfigDescription(
                "矩阵转化算力速度 U：物流塔**每分钟**可进行兑换的算力（Y=100% 时）。默认 7200。\n" +
                "最终**每分钟算力 = U × Y**（Y 为「矩阵转化算力速度」百分比，如 1500% 即 15 倍）。\n" +
                "例：U=7200、Y=1500% ⇒ 7200 × 15 = 108000 点/分钟。",
                new AcceptableValueRange<int>(1, 100000000)));

            TechSpeedPercent = config.Bind(general, "TechSpeedPercent", 100, new ConfigDescription(
                "矩阵转化算力速度 Y 的默认值（百分比）。100 表示 100%，即 1 倍。\n" +
                "举例：研究速度 14 级（游戏内显示 1500%）就填 1500。\n" +
                "UseGameTechSpeed = true 时本项被忽略。",
                new AcceptableValueRange<int>(1, 100000000)));

            UseGameTechSpeed = config.Bind(general, "UseGameTechSpeed", true, new ConfigDescription(
                "true：Y 自动取存档的研究速度数值（GameMain.history.techSpeed 是**倍率**，\n" +
                "  14 级为 15，等价于游戏面板显示的 1500%）。\n" +
                "false：Y 恒定使用上面的 TechSpeedPercent。\n" +
                "若发现实际速率与预期不符，把它设为 false 并手填数值。", null));

            SpeedMultiplier = config.Bind(general, "SpeedMultiplier", 1.0, new ConfigDescription(
                "调试倍率，默认 1（不放大）。最终每分钟算力 = U × Y × 本项。",
                new AcceptableValueRange<double>(0.0, 1000000.0)));

            AccumulatePartialPoints = config.Bind(general, "AccumulatePartialPoints", true, new ConfigDescription(
                "单帧算力买不起 1 个矩阵 / 1 个材料时，是否把这笔算力攒着、凑够 1 个再扣。\n" +
                "true（推荐）：结余仍然不结转，只会攒「没凑满 1 个」的零头。\n" +
                "  矩阵侧等效吞吐 = 材料种数 × 每分钟算力；每种材料的产出 = 每分钟算力 / 单价 个/分钟。\n" +
                "false：严格按单帧算力结算。此时若「每分钟算力 / 3600」小于单价，就会永远兑换不出来——\n" +
                "  例如每分钟算力 3600 点（=1 点/帧）时，物质重组器 100 点、核心素 300 点都买不起，\n" +
                "  结果是「只有能量碎片(50 点)能产出」，其它材料永远为 0。", null));

            VerboseLog = config.Bind(general, "VerboseLog", false, new ConfigDescription(
                "详细诊断日志：每次真正发生兑换时，逐槽位打印 物品/数量/上限/单价/配额/本帧产出。\n" +
                "**默认关闭**（会把日志刷满）。排查「某些材料不产出」时临时改成 true。", null));

            ElectromagneticMatrixPoints = BindPoints(config, matrices, "ElectromagneticMatrix", 50, "电磁矩阵 (1201)");
            EnergyMatrixPoints = BindPoints(config, matrices, "EnergyMatrix", 100, "能量矩阵 (1202)");
            StructureMatrixPoints = BindPoints(config, matrices, "StructureMatrix", 250, "结构矩阵 (1203)");
            InformationMatrixPoints = BindPoints(config, matrices, "InformationMatrix", 750, "信息矩阵 (1204)");
            GravityMatrixPoints = BindPoints(config, matrices, "GravityMatrix", 1500, "引力矩阵 (1205)");
            UniverseMatrixPoints = BindPoints(config, matrices, "UniverseMatrix", 3000, "宇宙矩阵 (1206)");

            EnergyFragmentPoints = BindPoints(config, materials, "EnergyFragment", 50, "能量碎片 (5206)");
            DarkFogMatrixPoints = BindPoints(config, materials, "DarkFogMatrix", 150, "黑雾矩阵 (5201)");
            SiliconNeuronPoints = BindPoints(config, materials, "SiliconNeuron", 100, "硅基神经元 (5202)");
            NegentropySingularityPoints = BindPoints(config, materials, "NegentropySingularity", 100, "负熵奇点 (5204)");
            MatterReassemblerPoints = BindPoints(config, materials, "MatterReassembler", 100, "物质重组器 (5203)");
            CoreElementPoints = BindPoints(config, materials, "CoreElement", 300, "核心素 (5205)");

            OutputSlotLimit = config.Bind(general, "OutputSlotLimit", 6, new ConfigDescription(
                "参与兑换的材料槽位上限（第几格）。默认 6：原版塔的全部槽位都能兑换。\n" +
                "本项只限制产出侧；第 1 格（矩阵）始终参与。装运输塔扩容后想用更多格就填更大。\n" +
                "每种材料各占一份算力，格数越多矩阵消耗越快。",
                new AcceptableValueRange<int>(2, 20)));

            DiagnosticLog = config.Bind(ui, "DiagnosticLog", false, new ConfigDescription(
                "把每座塔每 2 秒的兑换判定过程写入 BepInEx 日志，用于排查「不兑换」的问题。\n" +
                "排查完可以改成 false 关掉。", null));
        }
        private static ConfigEntry<int> BindPoints(ConfigFile config, string section, string key, int value, string label)
        {
            return config.Bind(section, key, value, new ConfigDescription(
                $"{label} 的算力（兑换权重）。必须为正整数。",
                new AcceptableValueRange<int>(1, 100000000)));
        }
    }
}
