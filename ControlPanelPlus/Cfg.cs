using BepInEx.Configuration;
using UnityEngine;

namespace ControlPanelPlus
{
    /// <summary>
    /// All configuration entries of the mod. Only the entries listed in the design document
    /// (section 3) exist here - do not add more without updating the document first.
    /// </summary>
    public static class Cfg
    {
        public static ConfigEntry<bool> Enabled;

        public static ConfigEntry<bool> CollapseAllDefaultMulti;
        public static ConfigEntry<bool> ExpandDefaultSingle;

        public static ConfigEntry<bool> EnableStarmapButton;
        public static ConfigEntry<bool> AllowRemoteItemTransfer;

        public static ConfigEntry<bool> EnableSlots;
        public static ConfigEntry<bool>[] SlotSwitches = new ConfigEntry<bool>[5];
        public static ConfigEntry<bool> EnableSettings;
        public static ConfigEntry<bool> SetChargePower;
        public static ConfigEntry<bool> SetTripDrones;
        public static ConfigEntry<bool> SetTripShips;
        public static ConfigEntry<bool> SetWarpDistance;
        public static ConfigEntry<bool> SetWarperNecessary;
        public static ConfigEntry<bool> SetOrbitCollector;
        public static ConfigEntry<bool> SetDeliveryDrones;
        public static ConfigEntry<bool> SetDeliveryShips;
        public static ConfigEntry<bool> SetPilerCount;
        public static ConfigEntry<bool> EnablePriority;
        public static ConfigEntry<KeyCode> CopyKey;
        public static ConfigEntry<KeyCode> PasteKey;

        /// <summary>Per-slot paste gate of the "slots" block (slot index is 0 based, switches are 1 based).</summary>
        public static bool SlotOn(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < SlotSwitches.Length && SlotSwitches[slotIndex] != null
                   && SlotSwitches[slotIndex].Value;
        }

        public static ConfigEntry<bool> ItemFilterIntersect;

        public static ConfigEntry<bool> SortByName;

        public static ConfigEntry<KeyCode> ListUp;
        public static ConfigEntry<KeyCode> ListDown;

        /// <summary>ConfigDescription carrying a ConfigurationManager sort order (higher shows first).</summary>
        private static ConfigDescription Ordered(string description, int order)
        {
            return new ConfigDescription(description, null, new ConfigurationManagerAttributes { Order = order });
        }

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "本 MOD 总开关。关闭后所有功能停止工作（补丁保持加载但不做任何事）。");

            CollapseAllDefaultMulti = config.Bind("Expand", "CollapseAllDefaultMulti", true,
                "多星球结果（全星区 / 某星系含多个有物流设施的星球）时，星球条目默认全部折叠。");
            ExpandDefaultSingle = config.Bind("Expand", "ExpandDefaultSingle", true,
                "单星球结果（筛选为某星球，或某星系只有 1 个星球有物流设施）时，该星球条目默认展开。");

            EnableStarmapButton = config.Bind("Locate", "EnableStarmapButton", true, Ordered(
                "在每个星球条目的导航按钮左侧显示“星图定位”按钮（左键：保留面板；右键：关闭面板；Shift+右键：关闭面板并点亮导航线）。", 30));
            AllowRemoteItemTransfer = config.Bind("Station", "AllowRemoteItemTransfer", false,
                "允许在总控面板上从非本地星球取放物品（右键拿取 / 手持物品左键放入 / 清空按钮取回）。若安装了 CheatEnabler，则由其 Architect 模式决定，本项不生效。");

            CopyKey = config.Bind("StationCopy", "CopyKey", KeyCode.Comma, Ordered(
                "复制物流塔设置的按键（默认 < ，即逗号键）。仅当总控面板选中物流塔时生效。", 180));
            PasteKey = config.Bind("StationCopy", "PasteKey", KeyCode.Period, Ordered(
                "粘贴物流塔设置的按键（默认 > ，即句号键）。按住 Shift 时批量粘贴到当前星球槽位设置相同的塔。", 170));
            EnableSlots = config.Bind("StationCopy", "EnableSlots", true, Ordered(
                "复制/粘贴第一块：槽位设置（物品种类、数量上限、本地/远程供需、运输机/运输船自动填充）。", 160));
            for (int i = 0; i < SlotSwitches.Length; i++)
            {
                int slot = i + 1;
                SlotSwitches[i] = config.Bind("StationCopy", "Slot" + slot, true, Ordered(
                    "粘贴时是否写入第 " + slot + " 槽位（关闭后该槽位保持不变）。", 150 - i * 10));
            }
            EnableSettings = config.Bind("StationCopy", "EnableSettings", true, Ordered(
                "复制/粘贴第二块：塔设置（充电功率、运输机/运输船路程、曲速路程、起送量、集装数量、轨道采集器开关、翘曲器必备）。", 100));
            SetChargePower = config.Bind("StationCopy", "SetChargePower", true, Ordered(
                "粘贴时是否写入充电功率。", 90));
            SetTripDrones = config.Bind("StationCopy", "SetTripDrones", true, Ordered(
                "粘贴时是否写入物流运输机航程。", 80));
            SetTripShips = config.Bind("StationCopy", "SetTripShips", true, Ordered(
                "粘贴时是否写入物流运输船航程（仅星际物流站）。", 70));
            SetWarpDistance = config.Bind("StationCopy", "SetWarpDistance", true, Ordered(
                "粘贴时是否写入曲速启用距离（仅星际物流站）。", 60));
            SetWarperNecessary = config.Bind("StationCopy", "SetWarperNecessary", true, Ordered(
                "粘贴时是否写入“翘曲器必备”开关（仅星际物流站）。", 50));
            SetOrbitCollector = config.Bind("StationCopy", "SetOrbitCollector", true, Ordered(
                "粘贴时是否写入“包含轨道采集器”开关（仅星际物流站）。", 40));
            SetDeliveryDrones = config.Bind("StationCopy", "SetDeliveryDrones", true, Ordered(
                "粘贴时是否写入配送运输机数量。", 30));
            SetDeliveryShips = config.Bind("StationCopy", "SetDeliveryShips", true, Ordered(
                "粘贴时是否写入配送运输船数量（仅星际物流站）。", 20));
            SetPilerCount = config.Bind("StationCopy", "SetPilerCount", true, Ordered(
                "粘贴时是否写入集装数量。", 10));
            EnablePriority = config.Bind("StationCopy", "EnablePriority", true, Ordered(
                "复制/粘贴第三块：优先级设置（塔名/点对点、星际航路优先级、分组、槽位优先级锁）。", 0));

            ItemFilterIntersect = config.Bind("Filter", "ItemFilterIntersect", false,
                "物品筛选由“并集”改为“交集”：选 A+B 时只显示同时含有 A 和 B 的条目（默认关闭，保持原版并集）。");

            SortByName = config.Bind("AstroList", "SortByName", true,
                "位置下拉框按星系名称排序（前两项“查看全星系 / 查看当前星球”保持不动）。内置星系/星球区分：星系名黄色、星球名缩进两格。");

            ListUp = config.Bind("Hotkeys", "ListUp", KeyCode.PageUp,
                "位置下拉框向上切换一项（按住 Ctrl 只切换星系）。");
            ListDown = config.Bind("Hotkeys", "ListDown", KeyCode.PageDown,
                "位置下拉框向下切换一项（按住 Ctrl 只切换星系）。");
        }
    }
}
