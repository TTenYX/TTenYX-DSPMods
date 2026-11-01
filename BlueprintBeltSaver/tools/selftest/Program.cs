using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace BBSaver.SelfTest
{
    // 蓝图 A/B 对比测试的游戏内半边（2026-11-01 用户方案）：
    //   B = 游戏内长带化：优先调用已部署插件的真实 TryOptimize（真实 cfg、双模式取舍、
    //       分拣器校验、硬上限与拓扑门禁），标题界面无玩家位置时其建模退化为赤道；
    //       反射失败则回退 Optimize 直调（同样 0°建模）。结果经游戏真实 API
    //       CheckBuildingData + Clone(ToBase64String→FromBase64String) 导出为「蓝图B」。
    //   判定：拓扑（线路数/分裂/串线/分拣器跨线/丢目标）、新链节硬上限、0↔1 高过渡完整。
    //   离线半边（蓝图A）与逐节点 diff 由离线校验台 --exportbp / --bpdiff 完成。
    [BepInPlugin("com.dsp.bbsaver.selftest", "BBSaver.SelfTest", "0.3")]
    [BepInDependency("com.dsp.blueprintbeltsaver", BepInDependency.DependencyFlags.SoftDependency)]
    public class SelfTestPlugin : BaseUnityPlugin
    {
        private static readonly string BlueprintDir =
            @"X:\戴森球计划\【MOD】修改仓库\7.蓝图Blueprints\BlueprintBeltSaver 测试蓝图";
        private static readonly string BOutDir =
            @"X:\戴森球计划\【MOD】修改仓库\7.蓝图Blueprints\BlueprintBeltSaver 长带化导出\蓝图B-游戏内";
        private static readonly string Dir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string MarkerPath = Path.Combine(Dir, "RUN_TESTS");
        private static readonly string LogPath = Path.Combine(Dir, "selftest.log");

        private const double Hard = 2.301172;
        private const double CapOpt = 2.301172 * 0.98;

        private bool _done;
        private DateTime _markerStamp = DateTime.MinValue;
        private readonly List<string> _lines = new List<string>();
        private int _pass, _fail;
        private static ManualLogSource _log;

        private void Awake() { _log = Logger; }

        private void Update()
        {
            if (!File.Exists(MarkerPath)) return;
            if (GameMain.instance == null) return;
            var stamp = File.GetLastWriteTimeUtc(MarkerPath);
            if (_done && stamp == _markerStamp) return;
            _markerStamp = stamp; _done = true;
            _lines.Clear(); _pass = 0; _fail = 0;
            try { RunAll(); }
            catch (Exception e) { Record("FATAL", false, e.ToString()); }
            finally { WriteReport(); }
        }

        private void Record(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _lines.Add((ok ? "PASS " : "FAIL ") + name + "  :: " + detail);
        }

        private void Info(string line) { _lines.Add("      " + line); }

        private void WriteReport()
        {
            var head = new List<string>
            {
                "BBSaver SelfTest  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "game = " + (GameMain.instance != null ? "ok" : "null"),
                "blueprintDir = " + BlueprintDir
            };
            head.AddRange(_lines);
            head.Add("SUMMARY PASS=" + _pass + " FAIL=" + _fail);
            File.WriteAllLines(LogPath, head, new System.Text.UTF8Encoding(false));
            _log.LogInfo("SUMMARY PASS=" + _pass + " FAIL=" + _fail + " → " + LogPath);
            // 全自动化收尾：报告落盘后自行退出游戏（下一轮 A/B 对比无需人工关游戏）。
            Application.Quit();
        }

        private static bool IsBelt(global::BlueprintBuilding b) => b != null && b.itemId >= 2001 && b.itemId <= 2009;

        private void RunAll()
        {
            Assembly mod = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BlueprintBeltSaver");
            Record("mod-assembly-loaded", mod != null, mod == null ? "未找到" : mod.Location);
            if (mod == null) return;
            var calibration = mod.GetType("BlueprintBeltSaver.BeltSpacingCalibration");
            var obsType = mod.GetType("BlueprintBeltSaver.BeltSpacingCalibration+Observation");
            var optimizer = mod.GetType("BlueprintBeltSaver.BeltPathOptimizer");
            var optimize = optimizer.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Optimize" && m.GetParameters().Length == 9);
            var resolve = calibration.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "Resolve" && m.GetParameters().Length >= 4)
                .OrderBy(m => m.GetParameters().Length).FirstOrDefault();
            var fromYaw = obsType.GetMethod("FromYaw", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var distance = calibration.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "Distance" && m.GetParameters().Length >= 3)
                .OrderBy(m => m.GetParameters().Length).FirstOrDefault();
            Record("reflection-ready", optimize != null && resolve != null && fromYaw != null && distance != null,
                "optimize=" + (optimize != null) + " resolve=" + (resolve != null)
                + " fromYaw=" + (fromYaw != null) + " distance=" + (distance != null));
            if (optimize == null || resolve == null || fromYaw == null || distance == null) return;

            object InvokePadded(MethodInfo mi, object[] given)
            {
                var ps = mi.GetParameters();
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    full[i] = i < given.Length ? given[i] : ps[i].IsOptional ? Type.Missing : null;
                return mi.Invoke(null, full);
            }

            // ── B：游戏内长带化（遍历全部测试蓝图，逐图导出蓝图B）──
            global::BlueprintData bpB = null; string modeB = "";
            var pluginType = mod.GetType("BlueprintBeltSaver.BlueprintBeltSaverPlugin");
            var pluginInstance = UnityEngine.Object.FindObjectOfType(pluginType);
            var tryOptimize = pluginType != null ? pluginType.GetMethod("TryOptimize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
            Record("B-plugin-ready", pluginInstance != null && tryOptimize != null,
                "plugin=" + (pluginInstance != null) + " tryOptimize=" + (tryOptimize != null));
            Directory.CreateDirectory(BOutDir);
            foreach (var file in Directory.GetFiles(BlueprintDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string text = File.ReadAllText(file).Trim();
                global::BlueprintData source;
                try
                {
                    // 必须完整 FromBase64String（含头部）——CreateNew 跳过头部，后续 TryOptimize
                    // 里 source.Clone() 会因 ToBase64String 缺头而返回空（插件注释明确警告过）。
                    source = new global::BlueprintData();
                    if (source.FromBase64String(text) != global::BlueprintDataIOError.OK)
                    { Record("parse:" + name, false, "FromBase64String 失败"); continue; }
                }
                catch (Exception ex) { Record("parse:" + name, false, ex.Message); continue; }
                if (source == null || source.buildings == null || source.buildings.Length == 0)
                { Record("parse:" + name, false, "CreateNew 返回空"); continue; }
                int before = source.buildings.Count(IsBelt);

                // ── bisect：五层指纹（源/Clone/格点后/插值后/采纳后），与离线探针逐层对账 ──
                try
                {
                    string fpSrc = Fingerprint(source.buildings);
                    var workB = source.Clone();
                    var trialB = source.Clone();
                    string fpWork = Fingerprint(workB.buildings);
                    var obsM = fromYaw.Invoke(null, new object[] { 0.0, 0f });
                    var model = InvokePadded(resolve, new object[] { workB.areas, workB.buildings,
                        new Func<global::BlueprintBuilding, bool>(IsBelt), obsM });
                    Func<global::BlueprintBuilding, global::BlueprintBuilding, double> meters =
                        (a, b) => (double)InvokePadded(distance, new object[] { a, b, model });
                    bool multiBand = false;
                    var mbField = model.GetType().GetField("MultiBand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var mbProp = model.GetType().GetProperty("MultiBand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mbField != null) multiBand = (bool)mbField.GetValue(model);
                    else if (mbProp != null) multiBand = (bool)mbProp.GetValue(model);
                    var gArgs = new object[] { workB.buildings, new Func<global::BlueprintBuilding, bool>(IsBelt),
                        new Func<global::BlueprintBuilding, float>(b => (float)CapOpt), meters, null, true, true, multiBand, true };
                    var gRes = (global::BlueprintBuilding[])optimize.Invoke(null, gArgs);
                    workB.buildings = gRes;
                    string fpGrid = Fingerprint(gRes);
                    var rArgs = new object[] { trialB.buildings, new Func<global::BlueprintBuilding, bool>(IsBelt),
                        new Func<global::BlueprintBuilding, float>(b => (float)CapOpt), meters, null, true, false, multiBand, true };
                    var rRes = (global::BlueprintBuilding[])optimize.Invoke(null, rArgs);
                    trialB.buildings = rRes;
                    string fpTrial = Fingerprint(rRes);
                    int gN = gRes.Count(IsBelt), rN = rRes.Count(IsBelt);
                    var chosenB = rN < gN ? trialB : workB;
                    string fpFinal = Fingerprint(chosenB.buildings);
                    Info($"bisect:{name} src={fpSrc} clone={fpWork} grid({gN})={fpGrid} resample({rN})={fpTrial} adopt({chosenB.buildings.Count(IsBelt)})={fpFinal} multiBand={multiBand}");
                }
                catch (Exception ex)
                {
                    Info($"bisect:{name} 异常 {ex.GetType().Name}: {ex.Message}");
                }

                bpB = null; modeB = "";
                if (pluginInstance != null && tryOptimize != null)
                {
                    try
                    {
                        var p = new object[] { source, null, null, 0f, false, null };
                        bool ok = (bool)tryOptimize.Invoke(pluginInstance, p);
                        bpB = p[1] as global::BlueprintData;
                        modeB = ok ? "plugin-TryOptimize" : "plugin-refused";
                        Record("B:" + name, true, "TryOptimize ok=" + ok + "；summary=" + (p[2] as string ?? ""));
                    }
                    catch (Exception ex)
                    {
                        Record("B:" + name, false, "TryOptimize 异常：" + ex.GetType().Name + "：" + (ex.InnerException?.Message ?? ex.Message));
                        continue;
                    }
                }
                else
                {
                    Record("B:" + name, false, "找不到插件实例或 TryOptimize 方法");
                    continue;
                }
                if (bpB == null)
                {
                    Info(name + "：游戏内拒绝省带（门禁保守拦截，保留原件）→ 不导出蓝图B");
                    continue;
                }
                var cloneB = bpB.Clone();
                bool exportOk = cloneB != null && cloneB.buildings != null;
                string fileB = Path.Combine(BOutDir, "B_lib_" + name + ".txt");
                if (exportOk) File.WriteAllText(fileB, cloneB.ToBase64String(), new UTF8Encoding(false));
                Record("B-export:" + name, exportOk, exportOk
                    ? string.Format("belts {0} → {1}，已保存 {2}（模式 {3}）", before, cloneB.buildings.Count(IsBelt), Path.GetFileName(fileB), modeB)
                    : "Clone 返回空（游戏拒绝序列化）");
            }
            Record("e2e-in-game-done", true, "全部蓝图B已生成（或记录拒绝原因）；离线蓝图A已由 --export 生成，随后逐字节对比");
        }

        /// <summary>拓扑判定：线路数一致、无分裂/串线、分拣器引用不跨线不丢失。</summary>
        private static bool TopologyCheck(global::BlueprintBuilding[] original, global::BlueprintBuilding[] result,
            out string reason, out int linesBefore, out int linesAfter, out int refs)
        {
            reason = ""; linesBefore = 0; linesAfter = 0; refs = 0;
            Func<global::BlueprintBuilding[], Dictionary<global::BlueprintBuilding, int>> linesOf = bs =>
            {
                var belts = bs.Where(IsBelt).ToArray();
                var parent = belts.ToDictionary(x => x, x => x);
                Func<global::BlueprintBuilding, global::BlueprintBuilding> find = null;
                find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                foreach (var b in belts)
                {
                    if (b.outputObj != null && IsBelt(b.outputObj) && parent.ContainsKey(b.outputObj))
                    { var ra = find(b); var rb = find(b.outputObj); if (ra != rb) parent[ra] = rb; }
                    if (b.inputObj != null && IsBelt(b.inputObj) && parent.ContainsKey(b.inputObj))
                    { var ra = find(b); var rb = find(b.inputObj); if (ra != rb) parent[ra] = rb; }
                }
                var lineOf = new Dictionary<global::BlueprintBuilding, int>();
                int next = 0; var rootId = new Dictionary<global::BlueprintBuilding, int>();
                foreach (var b in belts)
                {
                    var r = find(b);
                    if (!rootId.TryGetValue(r, out int id)) id = rootId[r] = next++;
                    lineOf[b] = id;
                }
                return lineOf;
            };
            var before = linesOf(original);
            var after = linesOf(result);
            linesBefore = before.Values.Distinct().Count();
            linesAfter = after.Values.Distinct().Count();
            if (linesBefore != linesAfter)
            { reason = string.Format("线路数 {0} → {1}（准则：必须一致）", linesBefore, linesAfter); return false; }
            var origSet = new HashSet<global::BlueprintBuilding>(original);
            var o2r = new Dictionary<int, int>(); var r2o = new Dictionary<int, int>();
            foreach (var n in result)
            {
                if (!IsBelt(n) || !origSet.Contains(n) || !before.ContainsKey(n) || !after.ContainsKey(n)) continue;
                int o = before[n], r = after[n];
                if (o2r.TryGetValue(o, out var r0) && r0 != r)
                { reason = string.Format("原线#{0} 分裂（断链）到结果线 {1}/{2}", o, r0, r); return false; }
                o2r[o] = r;
                if (r2o.TryGetValue(r, out var o0) && o0 != o)
                { reason = string.Format("结果线#{0} 串接原线 {1}/{2}（串线/吸附）", r, o0, o); return false; }
                r2o[r] = o;
            }
            var ownerByPos = new Dictionary<string, global::BlueprintBuilding>();
            foreach (var n in result)
            {
                if (IsBelt(n)) continue;
                ownerByPos[PosKey(n)] = n;
            }
            foreach (var src in original)
            {
                if (src == null || IsBelt(src)) continue;
                global::BlueprintBuilding cur;
                if (!ownerByPos.TryGetValue(PosKey(src), out cur)) continue;
                foreach (var isIn in new[] { true, false })
                {
                    var t0 = isIn ? src.inputObj : src.outputObj;
                    if (t0 == null || !IsBelt(t0) || !before.ContainsKey(t0)) continue;
                    var tn = isIn ? cur.inputObj : cur.outputObj;
                    if (tn == null || !IsBelt(tn) || !after.ContainsKey(tn))
                    { reason = string.Format("建筑@{0} 的{1}端带引用在导出后丢失", PosKey(src), isIn ? "输入" : "输出"); return false; }
                    if (!o2r.TryGetValue(before[t0], out var rLine) || rLine != after[tn])
                    { reason = string.Format("建筑@{0} 的{1}端被跨线重绑（游戏会把分拣器吸到别的物品条）", PosKey(src), isIn ? "输入" : "输出"); return false; }
                    refs++;
                }
            }
            return true;
        }

        private static string PosKey(global::BlueprintBuilding b)
        {
            return b.localOffset_x.ToString("F2") + "|" + b.localOffset_y.ToString("F2") + "|" + b.localOffset_z.ToString("F2") + "|" + b.itemId;
        }

        // 五层指纹：FNV-1a(64) 依数组序扫 (itemId, 量化x, 量化y, 量化z)，附带数。与离线探针同式。
        internal static string Fingerprint(global::BlueprintBuilding[] arr)
        {
            if (arr == null) return "null";
            ulong h = 14695981039346656037UL;
            int belts = 0;
            foreach (var b in arr)
            {
                if (b == null) { h = (h ^ 0xFF) * 1099511628211UL; continue; }
                h = (h ^ (ulong)(uint)b.itemId) * 1099511628211UL;
                long qx = (long)Math.Round(b.localOffset_x * 8192.0, MidpointRounding.AwayFromZero);
                long qy = (long)Math.Round(b.localOffset_y * 8192.0, MidpointRounding.AwayFromZero);
                long qz = (long)Math.Round(b.localOffset_z * 8192.0, MidpointRounding.AwayFromZero);
                h = (h ^ (ulong)qx) * 1099511628211UL;
                h = (h ^ (ulong)qy) * 1099511628211UL;
                h = (h ^ (ulong)qz) * 1099511628211UL;
                if (IsBelt(b))
                {
                    belts++;
                    var o = b.outputObj;
                    if (o != null)
                    {
                        h = (h ^ (ulong)(long)Math.Round(o.localOffset_x * 8192.0, MidpointRounding.AwayFromZero)) * 1099511628211UL;
                        h = (h ^ (ulong)(long)Math.Round(o.localOffset_y * 8192.0, MidpointRounding.AwayFromZero)) * 1099511628211UL;
                    }
                    else h = (h ^ 0xEE) * 1099511628211UL;
                }
            }
            return belts + "/" + h.ToString("X16");
        }
    }
}
