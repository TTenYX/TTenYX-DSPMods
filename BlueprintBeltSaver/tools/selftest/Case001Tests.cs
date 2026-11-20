using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BBSaver.SelfTest
{
    public partial class SelfTestPlugin
    {
        private void RunCase001Tests(Assembly mod, object plugin, string output)
        {
            string fixture = @"X:\戴森球计划\【MOD】修改仓库\1.我的MOD\BlueprintBeltSaver\docs\handoff\data\restore-case001-source.txt";
            bool strictBefore = ConfigValue(plugin, "_restoreStrictGrid");
            try
            {
                string text = File.ReadAllText(fixture);
                var source = new BlueprintData();
                if (source.FromBase64String(text) != BlueprintDataIOError.OK) throw new Exception("case001 parse failed");
                var original = source.ToBase64String();
                var engine = mod.GetType("BlueprintBeltSaver.BlueprintGridRestorer").GetMethod("TryRestore", RestoreFlags);
                var args = new object[] { source, null, null, true, false };
                bool ok = (bool)engine.Invoke(null, args);
                var result = args[1] as BlueprintData;
                Record("case001-restore-count", ok && result != null && source.buildings.Length == 13 && result.buildings.Length == 15,
                    $"13→{result?.buildings.Length}; added={Get(args[2], "Added")} removed={Get(args[2], "Removed")}");
                if (!ok || result == null) return;
                bool exact = result.buildings.All(n => n.localOffset_x == Math.Round(n.localOffset_x) && n.localOffset_y == 0 && n.localOffset_z == 0 &&
                    n.localOffset_x2 == n.localOffset_x && n.localOffset_y2 == 0 && n.localOffset_z2 == 0);
                Record("case001-exact-grid", exact && result.buildings.Select(n => (int)n.localOffset_x).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 15)), "X=0…14, Y=Z=0, primary + secondary");
                var signals = result.buildings.Where(n => n.parameters != null && n.parameters.Length > 0).ToArray();
                Record("case001-signal-preserved", signals.Length == 1 && signals[0].localOffset_x == 1 && signals[0].parameters.SequenceEqual(new[] { 1001, 360 }), "exact signal [1001,360] at X=1");
                Record("case001-native-source-poses", (int)Get(args[2], "NativePlannedRuns") > 0 && (int)Get(args[2], "SnappedNodes") == 13 &&
                    (int)Get(args[2], "ExcludedShortSourcePoses") == 8 && (int)Get(args[2], "CheckedPoses") == 1524,
                    "native SnapLineNonAlloc; only 8 already-short source poses excluded");
                Record("case001-source-unchanged", source.ToBase64String() == original && File.ReadAllText(fixture) == text, "owned candidate only, input file unchanged");
                var againArgs = new object[] { result.Clone(), null, null, true, false };
                bool again = (bool)engine.Invoke(null, againArgs);
                Record("case001-idempotent", again && ((BlueprintData)againArgs[1]).ToBase64String() == result.ToBase64String(), "restore→serialize→restore is byte-identical");
                ConfigValue(plugin, "_restoreStrictGrid", true);
                var wrapperArgs = new object[] { source, null, null, false };
                bool strict = (bool)Call(plugin, "TryRestore", wrapperArgs);
                Record("case001-strict-wrapper", strict && wrapperArgs[1] is BlueprintData bp && bp.buildings.Length == 15, wrapperArgs[2] as string ?? "none");
                ConfigValue(plugin, "_restoreStrictGrid", strictBefore);
                File.WriteAllText(Path.Combine(output, "B_R_复原测试用例001.txt"), result.ToBase64String(), new UTF8Encoding(false));
                RealPreviewAudit(mod, source, result, "case001");
            }
            catch (Exception ex) { Record("case001-runtime", false, ex.InnerException?.ToString() ?? ex.ToString()); }
            finally { ConfigValue(plugin, "_restoreStrictGrid", strictBefore); }
        }
    }
}
