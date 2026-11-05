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
        internal string BlueprintCap;
        internal string BlueprintCapDesc;
        internal string CapFromBlueprint;
        internal string Disabled;
        internal string OptimizeButton;
        internal string RestoreButton;
        internal string RestoreConfirmTitle;
        internal string RestoreCancelled;
        internal string RestoreNoBelts;
        internal string RestoreApplyDone;
        internal string RestoreTotalLine;
        internal string RestoreRunLine;
        internal string RestoreNativeLine;
        internal string RestoreMode;
        internal string RestoreNoChange;
        internal string RestoreNotAppliedPreview;
        internal string RestoreFailedTitle;
        internal string RestoreFailedBody;
        internal string RestoreUnknownFailure;
        internal string RestoreApplyFailed;
        internal string RestoreConfirmFailed;
        internal string RestoreSection;
        internal string RestoreQuantizeHeight;
        internal string RestoreQuantizeHeightDesc;
        internal string RestoreStrictGrid;
        internal string RestoreStrictGridDesc;
        internal string RestoreProtectedNodes;
        internal string RestoreProtectionDetails;
        internal string RestoreProtectionReport;
        internal string RestoreProtectionReportFailed;
        internal string PendingSourceChanged;
        internal string RestorePendingSourceChanged;
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
        internal string FailedTitle;
        internal string FailedBody;
        internal string CloneFailed;
        internal string SelfCheckFailed;
        internal string TopologyFailed;
        internal string RebindEnabled;
        internal string RebindYawSkipped;
        internal string RebindWarning;
        internal string SorterCheck;
        internal string ModeKeptOriginal;
        internal string ModeResampled;
        internal string SecGeneral;
        internal string SecMerge;
        internal string SecSorter;
        internal string SecModel;
        internal string EnabledToggle;
        internal string HotkeyInfo;
        internal string GridExactMerging;
        internal string GridExactMergingDesc;
        internal string RebindSorterLinks;
        internal string RebindSorterLinksDesc;
        internal string SorterForceRebind;
        internal string SorterForceRebindDesc;
        internal string ModelAtPlayerPosition;
        internal string ModelAtPlayerPositionDesc;
        internal string UiFontSizeLabel;

        private bool _chinese;

        internal string RestoreFailureReason(string reason)
        {
            switch (reason)
            {
                case "invalid-blueprint": return _chinese ? "蓝图为空或缺少区域数据。" : "The blueprint is empty or has no area data.";
                case "building-limit": return _chinese ? "复原后的建筑数会超过 15000 上限。" : "The restored blueprint would exceed the 15000 building limit.";
                case "invalid-buildings": return _chinese ? "蓝图建筑数据无效，无法安全处理。" : "The blueprint contains invalid building data.";
                case "nonfinite-coordinate": return _chinese ? "蓝图包含无效坐标，无法安全处理。" : "The blueprint contains non-finite coordinates.";
                case "dangling-reference": return _chinese ? "蓝图存在指向缺失建筑的连接，无法安全处理。" : "The blueprint contains dangling building references.";
                case "clone-failed": return _chinese ? "原蓝图无法安全序列化复制。" : "The original blueprint could not be cloned through serialization safely.";
                case "no-legal-placement": return _chinese ? "未找到游戏允许的粘贴姿态，无法完成安全检查。" : "No game-legal paste pose was found for the safety check.";
                case "topology-failed": return _chinese ? "复原结果未通过拓扑或连接检查。" : "The restored result failed the topology or connection check.";
                case "distance-failed": return _chinese ? "复原结果未通过传送带间距检查。" : "The restored result failed the belt-distance check.";
                case "serialize-failed": return _chinese ? "复原结果无法安全序列化，未应用。" : "The restored result could not be serialized safely.";
                case "strict-grid-conflict": return _chinese ? "严格网格要求与安全保护冲突：仍有非整数 X/Y 或未量化高度的传送带节点，整体未应用。保留点见下方。" : "Strict grid conflicts with safety protection: some belt nodes still have non-integer X/Y or non-quantized heights. Nothing was applied; protected nodes are listed below.";
                default:
                    return _chinese ? "无法安全复原该蓝图，请查看日志。" : "This blueprint could not be restored safely; see the log.";
            }
        }

        internal static BeltSaverTranslate Create(bool chinese)
        {
            bool zh = chinese;
            var text = new BeltSaverTranslate { _chinese = chinese };
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
                text.CapLinkage = "最远距离只影响徒手拉带；蓝图长带优化使用下方的蓝图合并上限。";
                text.CurrentCap = "当前蓝图优化间距上限";
                text.BlueprintCap = "蓝图合并单段上限";
                text.BlueprintCapDesc = "独立于徒手拉带。默认取游戏硬上限 2.301172 m（长带化收益最大）；调低更保守，但会留下更多 1 格带无法合并。";
                text.CapFromBlueprint = "来源：蓝图合并上限（本 MOD）";
                text.Disabled = "本 MOD 已在配置中禁用（General/Enabled = false）。";
                text.OptimizeButton = "省带";
                text.RestoreButton = "复原";
                text.RestoreConfirmTitle = "BlueprintBeltSaver 蓝图网格复原";
                text.RestoreCancelled = "已取消复原，蓝图未作改动。";
                text.RestoreNoBelts = "该蓝图没有可复原的传送带。";
                text.RestoreApplyDone = "复原完成：已刷新蓝图窗口、写入剪贴板，传送带 {0} 个节点。";
                text.RestoreTotalLine = "传送带节点：{0} → {1}（新增 {2}，移除 {3}）";
                text.RestoreRunLine = "已复原区段：{0}；受保护区段：{1}；未调整区段：{2}。";
                text.RestoreNativeLine = "原生网格规划区段：{0}；已安全规范化浮点误差的节点：{1}。";
                text.RestoreMode = "沿现有走向生成游戏网格节点，优先使用原生路径规划。设备连接窗口、弯道、坡道和无法安全调整的部分保留；未绑定区段的极小坐标误差仅在安全校验通过后规范化。安全检查始终执行，不受省带设置影响。\n这不是原蓝图备份还原，不能找回未知原蓝图。";
                text.RestoreNoChange = "没有可安全调整的网格节点。确认后仍会清除本 MOD 的优化标记，停止此前的自动重算。";
                text.RestoreNotAppliedPreview = "以下为未应用的复原试算结果，当前蓝图保持原样：";
                text.RestoreFailedTitle = "BlueprintBeltSaver 复原未执行";
                text.RestoreFailedBody = "本次复原未通过安全检查或发生异常，已保留原蓝图（未做任何改动）。\n\n原因：{0}\n\n详细日志见 Player.log，关键字「belt grid restoration failed」。";
                text.RestoreUnknownFailure = "无法安全复原该蓝图，请查看日志。";
                text.RestoreApplyFailed = "应用复原结果失败，请查看日志。";
                text.RestoreConfirmFailed = "无法显示复原确认弹窗，蓝图未作改动。请查看日志。";
                text.RestoreSection = "—— 网格复原 ——";
                text.RestoreQuantizeHeight = "复原高度量化（0.5 的倍数）";
                text.RestoreQuantizeHeightDesc = "开启：复原后的传送带高度仅允许 0.5 的倍数。关闭：保留原高度，仍检查传送带间距。无法安全调整的连接点、坡道等保留；严格网格开启时有不符合项则整体不应用。";
                text.RestoreStrictGrid = "严格网格复原（不符合时整体不应用）";
                text.RestoreStrictGridDesc = "关闭：只复原安全部分，并列出保留点。开启：全部传送带节点 X/Y 必须是整数，高度按上项检查，否则整体不应用。拓扑、连接、带距与碰撞安全检查始终执行。";
                text.RestoreProtectedNodes = "仍未符合网格要求的保留节点：{0}。";
                text.RestoreProtectionDetails = "保留点明细（最多显示 12 个；编号、区域、X/Y/高度）：";
                text.RestoreProtectionReport = "完整保留点报告：{0}";
                text.RestoreProtectionReportFailed = "完整保留点报告无法写入，详见日志；上述保留点仅显示前 12 个。";
                text.PendingSourceChanged = "确认期间蓝图已切换、修改或本 MOD 已禁用，本次省带未应用。请重新点击省带。";
                text.RestorePendingSourceChanged = "确认期间蓝图已切换、修改，复原设置已变化或本 MOD 已禁用，本次复原未应用。请重新点击复原。";
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
                text.CloneFailed = "原件蓝图无法安全序列化（Clone 返回空），已放弃本次省带。";
                text.SelfCheckFailed = "省带结果未通过硬上限自检（可能产生「距离太远」），已保留原件。";
                text.TopologyFailed = "省带结果未通过拓扑自检（线路条数变化或分拣器被跨线重绑，详见日志），已保留原件。";
                text.FailedTitle = "BlueprintBeltSaver 省带未执行";
                text.FailedBody = "本次省带的结果未通过安全自检或发生异常，已保留原蓝图（未做任何改动）。\n\n原因：{0}\n\n详细日志见 Player.log 关键字「belt optimization failed」「自检不过」。";
                text.RebindEnabled = "已重绑分拣器/建筑连接";
                text.RebindYawSkipped = "因朝向不一致跳过";
                text.RebindWarning = "已开启分拣器断链重绑：请在粘贴预览中确认无红色报错且分拣器可正常工作，再保存蓝图。";
                text.SorterCheck = "分拣器校验：共 {0} 个，原有空端 {1}，重新绑定 {2}，无可绑定 {3}，原连接通过 {4}";
                text.ModeKeptOriginal = "合并方式：只保留原有节点（粘贴时不会被网格换算改变跨度）。";
                text.ModeResampled = "合并方式：按弧长均匀插值（原节点方式无可减少，已改用插值；插值节点不在格点上）。";
                text.SecGeneral = "—— 基础 ——";
                text.SecMerge = "—— 传送带合并 ——";
                text.SecSorter = "—— 分拣器 ——";
                text.SecModel = "—— 省带建模 ——";
                text.EnabledToggle = "启用本 MOD";
                text.HotkeyInfo = "开关本窗口热键：Alpha3 + R（改键请在插件配置 General/WindowHotkey）。";
                text.GridExactMerging = "格点合并（推荐开启）";
                text.GridExactMergingDesc = "开启：只保留原有传送带节点，不生成插值坐标，跨纬度带与高空粘贴安全。关闭：按弧长插值新节点，省得更多，但新节点不在格点上，跨纬度/高空粘贴可能被判「距离太远」。";
                text.RebindSorterLinks = "分拣器断链重绑";
                text.RebindSorterLinksDesc = "开启后，被分拣器引用的传送带节点也参与合并，连接会重绑到合并后的新段（多个分拣器共用同一长带）。开启后请先在粘贴预览确认无红色报错、分拣器正常工作再保存。";
                text.SorterForceRebind = "分拣器强制重绑（实验，需同时开启上一项）";
                text.SorterForceRebindDesc = "三件套：合并忽略分拣器自由布线；按取货点强制重绑到新带节点并重锚定偏移（取货点世界坐标不动）；重排建造顺序（带节点排到分拣器之前）。开启后请逐个确认分拣器正常再保存。";
                text.ModelAtPlayerPosition = "按机甲位置建模（库内省带）";
                text.ModelAtPlayerPositionDesc = "默认关闭 = 按赤道建模（跨纬度粘贴兼容性最好）；勾选 = 按机甲当前位置建模（弹窗数量即脚下粘贴的最终数量）。粘贴模式下的位置重算不受此项影响，始终按实际粘贴位置。";
                text.UiFontSizeLabel = "界面字号";
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
                text.CapLinkage = "Maximum Spacing affects hand-drawn belts only; blueprint merging uses the blueprint merge cap below.";
                text.CurrentCap = "Current blueprint merge spacing cap";
                text.BlueprintCap = "Blueprint merge cap";
                text.BlueprintCapDesc = "Independent of hand-drawn belts. Defaults to the game hard limit 2.301172 m (maximum merging); lowering it is more conservative but leaves more 1-grid belts unmerged.";
                text.CapFromBlueprint = "source: blueprint merge cap (this mod)";
                text.Disabled = "This mod is disabled (General/Enabled = false).";
                text.OptimizeButton = "Save Belts";
                text.RestoreButton = "Restore";
                text.RestoreConfirmTitle = "BlueprintBeltSaver - Grid Restoration";
                text.RestoreCancelled = "Restoration cancelled; the blueprint is unchanged.";
                text.RestoreNoBelts = "This blueprint has no belts to restore.";
                text.RestoreApplyDone = "Restored: blueprint window refreshed, clipboard updated, {0} belt nodes.";
                text.RestoreTotalLine = "Belt nodes: {0} → {1} (added {2}, removed {3})";
                text.RestoreRunLine = "Restored runs: {0}; protected runs: {1}; unaffected runs: {2}.";
                text.RestoreNativeLine = "Native grid-planned runs: {0}; safely normalized coordinate-noise nodes: {1}.";
                text.RestoreMode = "Generate game-grid nodes along the current route, preferring the native path planner. Equipment connection windows, curves, slopes and unsafe parts are kept; tiny coordinate noise on unbound sections is normalized only after safety validation. Safety checks always run regardless of merge settings.\nThis is not a blueprint backup restore and cannot recover an unknown original blueprint.";
                text.RestoreNoChange = "No grid nodes can be adjusted safely. Confirming still clears this mod's optimization marker and stops its previous automatic re-optimization.";
                text.RestoreNotAppliedPreview = "The following restoration preview was NOT applied; the current blueprint is unchanged:";
                text.RestoreFailedTitle = "BlueprintBeltSaver - restoration not applied";
                text.RestoreFailedBody = "Restoration failed a safety check or an error occurred; the original blueprint is kept unchanged.\n\nReason: {0}\n\nSee Player.log for details (keyword: belt grid restoration failed).";
                text.RestoreUnknownFailure = "This blueprint could not be restored safely; see the log.";
                text.RestoreApplyFailed = "Applying the restoration failed; see the log.";
                text.RestoreConfirmFailed = "The restoration confirmation dialog could not be shown; the blueprint is unchanged. See the log.";
                text.RestoreSection = "—— Grid Restoration ——";
                text.RestoreQuantizeHeight = "Quantize restored heights (multiples of 0.5)";
                text.RestoreQuantizeHeightDesc = "ON: restored belt heights must be multiples of 0.5. OFF: preserve original heights while still checking belt spacing. Unsafe connection/slope adjustments are kept; strict grid rejects the result if any node remains noncompliant.";
                text.RestoreStrictGrid = "Strict grid restoration (reject any noncompliance)";
                text.RestoreStrictGridDesc = "OFF: restore safe portions and list protected nodes. ON: every belt node must have integer X/Y coordinates; heights follow the option above, otherwise nothing is applied. Topology, connection, spacing and collision safety checks always run.";
                text.RestoreProtectedNodes = "Protected nodes still outside the grid requirements: {0}.";
                text.RestoreProtectionDetails = "Protected node details (up to 12; index, area, X/Y/height):";
                text.RestoreProtectionReport = "Complete protected-node report: {0}";
                text.RestoreProtectionReportFailed = "Could not write the complete protected-node report; see the log. Only the first 12 points are shown above.";
                text.PendingSourceChanged = "The blueprint changed or switched, or this mod was disabled during confirmation. The merge was not applied; click Save Belts again.";
                text.RestorePendingSourceChanged = "The blueprint changed or switched, restoration options changed, or this mod was disabled during confirmation. Restoration was not applied; click Restore again.";
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
                text.CloneFailed = "The original blueprint could not be serialized safely; nothing was changed.";
                text.SelfCheckFailed = "The merge result failed the hard-limit self-check (it could paste as too far); the original is kept.";
                text.TopologyFailed = "The merge result failed the topology self-check (line count changed or a sorter was re-bound across lines; see log); the original is kept.";
                text.FailedTitle = "BlueprintBeltSaver - merge not applied";
                text.FailedBody = "The merge result failed the safety check or an error occurred, so the original blueprint is kept unchanged.\n\nReason: {0}\n\nSee Player.log for details (keywords: belt optimization failed).";
                text.RebindEnabled = "Sorter/building links rebound";
                text.RebindYawSkipped = "runs skipped for yaw mismatch";
                text.RebindWarning = "Sorter link rebinding is ON: confirm the paste preview shows no errors and the sorters work before saving.";
                text.SorterCheck = "Sorters: {0} total, {1} empty ends found, {2} re-bound, {3} unbound, {4} verified";
                text.ModeKeptOriginal = "Merge mode: existing nodes only (paste-time grid maths cannot change the spans).";
                text.ModeResampled = "Merge mode: uniform arc resampling (existing nodes alone could not be shortened; the new nodes are not on grid points).";
                text.SecGeneral = "—— General ——";
                text.SecMerge = "—— Belt Merging ——";
                text.SecSorter = "—— Sorters ——";
                text.SecModel = "—— Modeling ——";
                text.EnabledToggle = "Enable this mod";
                text.HotkeyInfo = "Window hotkey: Alpha3 + R (change it in the plugin config, General/WindowHotkey).";
                text.GridExactMerging = "Grid-exact merging (recommended)";
                text.GridExactMergingDesc = "ON: only existing belt nodes are kept, no interpolated coordinates; safe across latitude bands and at high altitude. OFF: new nodes are interpolated by arc length (saves more, but they are off-grid and paste may report 'too far' across latitudes/altitude).";
                text.RebindSorterLinks = "Rebind broken sorter links";
                text.RebindSorterLinksDesc = "Belt nodes referenced by sorters also take part in merging; their links are re-bound to the covering merged belt (several sorters share one long belt). Check the paste preview before saving.";
                text.SorterForceRebind = "Force sorter rebind (experimental, needs the option above)";
                text.SorterForceRebindDesc = "Triple: merge ignores sorter free rewiring; re-bind by pickup coordinates onto the new belt node and re-anchor the offset (world pickup point unchanged); reorder build sequence (belts before sorters).";
                text.ModelAtPlayerPosition = "Model at mecha position (library merging)";
                text.ModelAtPlayerPositionDesc = "Default OFF = model at the equator (best cross-latitude paste compatibility); ON = model at the mecha's current latitude. Paste-time re-optimization always uses the real paste position.";
                text.UiFontSizeLabel = "UI Font Size";
            }
            return text;
        }
    }
}
