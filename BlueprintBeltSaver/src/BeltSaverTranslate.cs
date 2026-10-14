namespace BlueprintBeltSaver
{
    /// <summary>
    /// F2 window and F4 dialog text. Wording follows the plan; the two gridline span labels use the
    /// plan's configuration table ("LongitudeDistance = 沿纬线最远距离", "LatitudeDistance = 沿经线最远距离").
    /// Note: LongerBelts itself labels those two the other way round, so the in-game wording differs
    /// from the reference MOD even though the underlying math is identical.
    /// </summary>
    internal sealed class BeltSaverTranslate
    {
        internal string Title;
        internal string IfLongerOnGrid;
        internal string LongitudeDistance;
        internal string LatitudeDistance;
        internal string DistanceSetting;
        internal string PathMode;
        internal string[] PathModeStrings;
        internal string WarningNotice;
        internal string UnlimitInstruction;
        internal string CapLinkage;
        internal string CurrentCap;
        internal string Disabled;
        internal string OptimizeButton;
        internal string ConfirmTitle;
        internal string Confirm;
        internal string Cancel;
        internal string Cancelled;
        internal string NoBelts;
        internal string CapFromLongerBelts;
        internal string CapFromOwn;
        internal string ApplyDone;
        internal string Saved;
        internal string Total;
        internal string TotalLine;
        internal string NoChange;
        internal string ConfirmFailed;
        internal string TooManyBuildings;
        internal string RebindEnabled;
        internal string RebindYawSkipped;
        internal string RebindWarning;
        internal string SorterCheck;
        internal string ModeKeptOriginal;
        internal string ModeResampled;

        internal static BeltSaverTranslate Create(bool chinese)
        {
            bool zh = chinese;
            var text = new BeltSaverTranslate();
            if (zh)
            {
                text.Title = "蓝图省带 BlueprintBeltSaver";
                text.IfLongerOnGrid = "更改经纬线模式默认跨度";
                text.LongitudeDistance = "沿纬线最远距离";
                text.LatitudeDistance = "沿经线最远距离";
                text.DistanceSetting = "最远距离";
                text.PathMode = "传送带路径";
                text.PathModeStrings = new[]
                {
                    "原版升降逻辑(水平移动一格后每节升降0.5层至目标高度)",
                    "垂直等距分割(端点不水平，常规游戏需先在两端拉好水平带\n建议配合如建筑铺设无条件等放宽传送带铺设条件的功能使用)"
                };
                text.WarningNotice = "下列功能慎用!";
                text.UnlimitInstruction = "勾选以启用弱约束间距(常规游戏中会出现传送带过短或过长等错误)";
                text.CapLinkage = "以上参数同时用于徒手拉带与蓝图长带优化。";
                text.CurrentCap = "当前蓝图优化间距上限";
                text.Disabled = "本 MOD 已在配置中禁用（General/Enabled = false）。";
                text.OptimizeButton = "省带";
                text.ConfirmTitle = "BlueprintBeltSaver 蓝图长带化";
                text.Confirm = "确定";
                text.Cancel = "取消";
                text.Cancelled = "已取消，蓝图未作改动。";
                text.NoBelts = "该蓝图没有可优化的传送带。";
                text.CapFromLongerBelts = "来源：LongerBelts 窗口";
                text.CapFromOwn = "来源：本 MOD 设置窗口";
                text.ApplyDone = "优化完成：已刷新蓝图窗口、写入剪贴板，传送带 {0} 段。";
                text.Saved = "省";
                text.Total = "合计";
                text.TotalLine = "传送带总数：{0} → {1}（省 {2}）";
                text.NoChange = "没有可合并的传送带（现有间距已接近上限）。";
                text.ConfirmFailed = "无法显示确认弹窗，请查看日志。";
                text.TooManyBuildings = "优化后建筑数超过 15000 上限，已放弃本次优化。";
                text.RebindEnabled = "已重绑分拣器/建筑连接";
                text.RebindYawSkipped = "因朝向不一致跳过";
                text.RebindWarning = "已开启分拣器断链重绑：请在粘贴预览中确认无红色报错且分拣器可正常工作，再保存蓝图。";
                text.SorterCheck = "分拣器校验：共 {0} 个，原有空端 {1}，重新绑定 {2}，无可绑定 {3}，原连接通过 {4}";
                text.ModeKeptOriginal = "合并方式：只保留原有节点（粘贴时不会被网格换算改变跨度）。";
                text.ModeResampled = "合并方式：按弧长均匀插值（原节点方式无可减少，已改用插值；插值节点不在格点上）。";
            }
            else
            {
                text.Title = "BlueprintBeltSaver";
                text.IfLongerOnGrid = "Change default span in gridline mode";
                text.LongitudeDistance = "Longitude Span";
                text.LatitudeDistance = "Latitude Span";
                text.DistanceSetting = "Maximum Spacing (meters)";
                text.PathMode = "Belt Path Mode";
                text.PathModeStrings = new[]
                {
                    "Original elevating logic (moving horizontally one grid and changing\nelevation by 0.5 units per segment to reach the target height)",
                    "Vertical equidistant (horizontal conveyor belts at both ends\nor other auxiliary mods are required)"
                };
                text.WarningNotice = "Exercise caution when modifying the following settings!";
                text.UnlimitInstruction = "Ignore the minimum belt distance (short or over-long belts can occur in vanilla).";
                text.CapLinkage = "These values drive both hand-drawn belts and blueprint belt merging.";
                text.CurrentCap = "Current blueprint merge spacing cap";
                text.Disabled = "This mod is disabled (General/Enabled = false).";
                text.OptimizeButton = "Save Belts";
                text.ConfirmTitle = "BlueprintBeltSaver - Long Belt Conversion";
                text.Confirm = "OK";
                text.Cancel = "Cancel";
                text.Cancelled = "Cancelled; the blueprint is unchanged.";
                text.NoBelts = "This blueprint has no belts to merge.";
                text.CapFromLongerBelts = "source: LongerBelts window";
                text.CapFromOwn = "source: this mod's window";
                text.ApplyDone = "Done: blueprint window refreshed, clipboard updated, {0} belts remain.";
                text.Saved = "saved";
                text.Total = "Total";
                text.TotalLine = "Belts in total: {0} → {1} (saved {2})";
                text.NoChange = "Nothing could be merged; the existing spacing is already close to the cap.";
                text.ConfirmFailed = "The confirmation dialog could not be shown; see the log.";
                text.TooManyBuildings = "The optimized blueprint would exceed the 15000 building limit; keeping the original.";
                text.RebindEnabled = "Sorter/building links rebound";
                text.RebindYawSkipped = "runs skipped for yaw mismatch";
                text.RebindWarning = "Sorter link rebinding is ON: confirm the paste preview shows no errors and the sorters work before saving.";
                text.SorterCheck = "Sorters: {0} total, {1} empty ends found, {2} re-bound, {3} unbound, {4} verified";
                text.ModeKeptOriginal = "Merge mode: existing nodes only (paste-time grid maths cannot change the spans).";
                text.ModeResampled = "Merge mode: uniform arc resampling (existing nodes alone could not be shortened; the new nodes are not on grid points).";
            }
            return text;
        }
    }
}
