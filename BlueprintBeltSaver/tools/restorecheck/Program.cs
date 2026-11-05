using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Independent CLI regression harness. No production source is linked into this assembly.
// CLI: --fixtures [--out report.txt] | --batch <dir> [--out report.txt]
//      --export <inputdir> <outdir> [--out report.txt] | --compare <Adir> <Bdir>
//      --all [--originals dir] [--optimized dir] [--out report.txt]
// Exit codes: 0 all checks passed; 1 failed assertions; 2 usage/environment/input failure.
internal static class Program
{
    const BindingFlags BF = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const double Eps = 0.00021; // slightly above production serializer-noise epsilon (0.0002)
    const double MinMeters = 0.400001;
    const string Managed = "D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/Managed";
    const string DefaultOriginals = "X:/戴森球计划/【MOD】修改仓库/7.蓝图Blueprints/BlueprintBeltSaver 测试蓝图";
    const string DefaultOptimized = "X:/戴森球计划/【MOD】修改仓库/7.蓝图Blueprints/BlueprintBeltSaver 长带化导出/蓝图A-游戏外";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly List<string> Log = new();
    static Assembly Mod;
    static MethodInfo Restore;
    static int Pass, Fail, Refused;
    static string Output;
    static readonly FieldInfo[] BuildingFields = Fields(typeof(BlueprintBuilding));
    // Raw deserialization indices are caches, not semantic target identity; the bit-exact
    // source fingerprint below still includes them. Authoritative refs are checked in full.
    static readonly HashSet<string> LinkFields = new() { "inputObj", "outputObj", "index", "tempInputObjIdx", "tempOutputObjIdx" };
    static readonly HashSet<string> GeometryFields = new() { "localOffset_x", "localOffset_y", "localOffset_z", "localOffset_x2", "localOffset_y2", "localOffset_z2" };

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.CurrentCulture = Inv;
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            Output = Option(args, "--out", null);
            Info("RestoreCheck " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            if (args.Contains("--help")) { Help(); return Finish(0); }
            if (args.Contains("--inspect"))
            {
                foreach (var t in new[] { typeof(BlueprintData), typeof(BlueprintArea), typeof(BlueprintBuilding) })
                { Info(t.FullName); foreach (var f in Fields(t)) Info("  " + f.Name + " : " + f.FieldType); }
                return Finish(0);
            }
            if (args.Length > 0 && args[0] == "--compare")
            { Need(args, 3); Compare(args[1], args[2]); return Finish(Fail == 0 ? 0 : 1); }
            if (args.Length > 0 && args[0] == "--baseline")
            {
                Need(args, 2);
                foreach (var path in Directory.GetFiles(args[1], "*.txt").OrderBy(x => x, StringComparer.Ordinal))
                    Check("baseline:" + Path.GetFileNameWithoutExtension(path), () => { var bp = Load(path); var clone = RoundTrip(bp); Require(SemanticFingerprint(bp) == SemanticFingerprint(clone), "E_BASELINE_SERIALIZATION", SemanticDifference(bp, clone)); });
                return Finish(Fail == 0 ? 0 : 1);
            }
            LoadApi();
            string originals = Option(args, "--originals", DefaultOriginals);
            string optimized = Option(args, "--optimized", DefaultOptimized);
            if (args.Length == 0 || args[0] == "--fixtures") Fixtures(originals);
            else if (args[0] == "--batch") { Need(args, 2); Batch(args[1], null, false); }
            else if (args[0] == "--export") { Need(args, 3); Batch(args[1], args[2], false); }
            else if (args[0] == "--all")
            {
                Fixtures(originals);
                Batch(originals, null, false);
                Batch(optimized, null, true);
            }
            else throw new CheckException("E_USAGE", "unknown command; run --help");
            return Finish(Fail == 0 ? 0 : 1);
        }
        catch (Exception e)
        {
            e = Unwrap(e);
            Info("ERROR " + (e is CheckException ce ? ce.Code : "E_UNEXPECTED") + " " + e.Message);
            return Finish(2);
        }
    }

    static void Help() => Info("--fixtures | --all [--originals dir] [--optimized dir] | --batch dir | --export inputdir outdir | --compare Adir Bdir; optional --out report.txt and --mod DLL. Export preserves complete source headings and writes A_R_<input-basename>.txt. Failure is a nonzero exit code, not an unhandled exception.");
    static void Need(string[] a, int n) { if (a.Length < n) throw new CheckException("E_USAGE", "missing positional argument"); }
    static string Option(string[] a, string key, string fallback)
    { int i = Array.IndexOf(a, key); return i < 0 ? fallback : i + 1 < a.Length ? a[i + 1] : throw new CheckException("E_USAGE", "missing " + key + " value"); }
    static int Finish(int code)
    {
        Info($"SUMMARY PASS={Pass} FAIL={Fail} REFUSED={Refused} EXIT={code}");
        if (Output != null)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!); File.WriteAllLines(Output, Log, new UTF8Encoding(false)); }
            catch (Exception e) { Console.Error.WriteLine("E_REPORT " + e.Message); return 2; }
        }
        return code;
    }
    static void Info(string s) { Log.Add(s); Console.WriteLine(s); }
    static void Check(string name, Action f)
    {
        try { f(); Pass++; Info("PASS " + name); }
        catch (Exception e) { e = Unwrap(e); Fail++; Info("FAIL " + name + " :: " + (e is CheckException c ? c.Code + " " : "") + e.Message); }
    }
    static void Require(bool ok, string code, string detail) { if (!ok) throw new CheckException(code, detail); }
    sealed class CheckException : Exception
    { public readonly string Code; public CheckException(string code, string message) : base(message) { Code = code; } }
    static Exception Unwrap(Exception e) { while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException; return e; }
    static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string core = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "r2modmanPlus-local/DysonSphereProgram/profiles/TEST/BepInEx/core");
        foreach (string dir in new[] { AppContext.BaseDirectory, Managed, core })
        { string p = Path.Combine(dir, name); if (File.Exists(p)) return Assembly.LoadFrom(p); }
        return null;
    }
    static void LoadApi()
    {
        string dll = Option(Environment.GetCommandLineArgs(), "--mod", Path.Combine(AppContext.BaseDirectory, "BlueprintBeltSaver.dll"));
        Require(File.Exists(dll), "E_MOD_MISSING", dll);
        Mod = Assembly.LoadFrom(dll);
        var type = Mod.GetType("BlueprintBeltSaver.BlueprintGridRestorer");
        Require(type != null, "E_API_NOT_READY", "BlueprintGridRestorer is not in the built MOD; wait for main agent rebuild");
        Restore = type.GetMethod("TryRestore", BF);
        var ps = Restore?.GetParameters();
        Require(Restore?.ReturnType == typeof(bool) && ps?.Length == 5 && ps[0].ParameterType == typeof(BlueprintData)
            && ps[1].IsOut && ps[1].ParameterType == typeof(BlueprintData).MakeByRefType() && ps[2].IsOut
            && ps[3].ParameterType == typeof(bool) && ps[4].ParameterType == typeof(bool),
            "E_API_SIGNATURE", "expected bool TryRestore(BlueprintData,out BlueprintData,out Report,bool quantizeHeight=true,bool strictGrid=false)");
        var reportType = ps[2].ParameterType.GetElementType();
        foreach (string key in new[] { "Before", "After", "Added", "Removed", "RestoredRuns", "ProtectedRuns", "Reason" })
            Require(reportType.GetField(key, BF) != null || reportType.GetProperty(key, BF) != null, "E_REPORT_API", key);
        Info("API " + Restore + " MOD=" + dll + " SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
    }
    sealed record Outcome(bool Ok, BlueprintData Result, object Report)
    {
        public int Int(string name) => Convert.ToInt32(Value(Report, name), Inv);
        public string Reason => Convert.ToString(Value(Report, "Reason"), Inv) ?? "";
        public override string ToString() => $"ok={Ok} belts={Int("Before")}->{Int("After")} added={Int("Added")} removed={Int("Removed")} restored={Int("RestoredRuns")} protected={Int("ProtectedRuns")} reason={Reason}";
    }
    static Outcome Invoke(BlueprintData source, bool quantizeHeight = true, bool strictGrid = false)
    {
        string before = ExactFingerprint(source);
        string RootValue(object value)
        {
            if (value is IEnumerable seq && value is not string)
            {
                var values = seq.Cast<object>().ToArray();
                if (values.Any(v => v is BlueprintBuilding)) return "nodes[" + string.Join(",", values.Select(v => v is BlueprintBuilding n ? n.index + ":" + Pos(n) : Scalar(v))) + "]";
            }
            return Scalar(value);
        }
        var root = source == null ? new Dictionary<string, string>() : Fields(typeof(BlueprintData)).Where(f => f.Name != "buildings").ToDictionary(f => f.Name, f => RootValue(f.GetValue(source)));
        var snapshots = (source?.buildings ?? Array.Empty<BlueprintBuilding>()).Where(n => n != null).Distinct().ToDictionary(n => n,
            n => BuildingFields.ToDictionary(f => f.Name, f => f.GetValue(n) is BlueprintBuilding r ? "ref:" + r.index + ":" + Pos(r) : Scalar(f.GetValue(n))));
        object[] p = { source, null, null, quantizeHeight, strictGrid };
        bool ok = (bool)Restore.Invoke(null, p);
        string changed = string.Join(" / ", snapshots.SelectMany(kv => BuildingFields.Select(f => (n: kv.Key, f, now: f.GetValue(kv.Key) is BlueprintBuilding r ? "ref:" + r.index + ":" + Pos(r) : Scalar(f.GetValue(kv.Key))))
            .Where(x => kv.Value[x.f.Name] != x.now).Select(x => "node#" + x.n.index + "." + x.f.Name + ":" + kv.Value[x.f.Name] + "->" + x.now)).Take(6));
        string rootChanged = source == null ? "" : string.Join(" / ", Fields(typeof(BlueprintData)).Where(f => f.Name != "buildings" && root[f.Name] != RootValue(f.GetValue(source))).Select(f => "root." + f.Name + ":" + root[f.Name] + "->" + RootValue(f.GetValue(source))));
        Require(before == ExactFingerprint(source), "E_SOURCE_MUTATED", "all fields/arrays/coords/refs immutable; " + changed + rootChanged);
        Require(p[2] != null, "E_REPORT_NULL", "report must always be populated");
        var result = new Outcome(ok, p[1] as BlueprintData, p[2]);
        if (ok)
        {
            Require(result.Result?.buildings != null, "E_RESULT_NULL", "success must provide a blueprint");
            Require(string.IsNullOrEmpty(result.Reason), "E_SUCCESS_REASON", result.Reason);
            int countBefore = (source?.buildings ?? Array.Empty<BlueprintBuilding>()).Count(IsBelt);
            int countAfter = result.Result.buildings.Count(IsBelt);
            Require(result.Int("Before") == countBefore && result.Int("After") == countAfter, "E_COUNTS", result.ToString() + $" actual={countBefore}->{countAfter}");
            Require(result.Int("Added") >= 0 && result.Int("Removed") >= 0 && countAfter - countBefore == result.Int("Added") - result.Int("Removed"), "E_DELTA", result.ToString());
        }
        else Require(!string.IsNullOrWhiteSpace(result.Reason), "E_NO_ERROR_CODE", "false needs a machine-readable Reason");
        return result;
    }

    static FieldInfo[] Fields(Type t) => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    static object Value(object x, string name) => x?.GetType().GetField(name, BF)?.GetValue(x) ?? x?.GetType().GetProperty(name, BF)?.GetValue(x);
    static void Set(object x, string field, object v)
    { var f = x.GetType().GetField(field, BF); if (f != null) f.SetValue(x, ConvertValue(v, f.FieldType)); }
    static object ConvertValue(object x, Type t) => x == null || t.IsInstanceOfType(x) ? x : t.IsEnum ? Enum.ToObject(t, x) : Convert.ChangeType(x, t, Inv);
    static bool IsBelt(BlueprintBuilding b) => b != null && b.itemId >= 2001 && b.itemId <= 2009;
    static string Scalar(object x, bool quantized = false)
    {
        if (x == null) return "null";
        if (x is float f) return quantized ? Q(f).ToString(Inv) : BitConverter.SingleToInt32Bits(f).ToString("X8", Inv);
        if (x is double d) return quantized ? Q(d).ToString(Inv) : BitConverter.DoubleToInt64Bits(d).ToString("X16", Inv);
        if (x is string s) return "str:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        if (x is DateTime dt) return dt.ToBinary().ToString(Inv);
        if (x.GetType().IsPrimitive || x.GetType().IsEnum || x is decimal) return Convert.ToString(x, Inv);
        if (x is IEnumerable a) return "[" + string.Join(";", a.Cast<object>().Select(v => Scalar(v, quantized))) + "]";
        return "{" + string.Join(";", Fields(x.GetType()).Select(f => f.Name + "=" + Scalar(f.GetValue(x), quantized))) + "}";
    }
    static long Q(double x) => (long)Math.Round(x * 8192.0, MidpointRounding.AwayFromZero);
    static string Pos(BlueprintBuilding b, bool exact = false) => b == null ? "null" : $"{b.areaIndex}|{Scalar(b.localOffset_x, !exact)},{Scalar(b.localOffset_y, !exact)},{Scalar(b.localOffset_z, !exact)}|{b.itemId}";
    static string SemanticScalar(FieldInfo f, object value, bool quantized)
    {
        if (value is float v && v == 0f) value = 0f; // game roundtrip legitimately normalizes signed zero
        if (f.Name == "parameters" && value == null) value = Array.Empty<int>();
        if (f.Name == "content" && value == null) value = "";
        return Scalar(value, quantized && GeometryFields.Contains(f.Name));
    }
    static bool MetadataCompatible(BlueprintBuilding a, BlueprintBuilding b)
    {
        foreach (var f in BuildingFields.Where(f => !LinkFields.Contains(f.Name) && !GeometryFields.Contains(f.Name)))
        {
            var av = f.GetValue(a); var bv = f.GetValue(b);
            if (f.Name is "yaw" or "yaw2" or "pitch" or "pitch2" or "tilt" or "tilt2")
            {
                if (Math.Abs(Mathf.DeltaAngle((float)av, (float)bv)) > .005f) return false;
            }
            else if (SemanticScalar(f, av, false) != SemanticScalar(f, bv, false)) return false;
        }
        return true;
    }
    static string MetadataDifference(BlueprintBuilding a, BlueprintBuilding b) => string.Join(",", BuildingFields.Where(f => !LinkFields.Contains(f.Name) && !GeometryFields.Contains(f.Name) && SemanticScalar(f, f.GetValue(a), false) != SemanticScalar(f, f.GetValue(b), false)).Select(f => f.Name + ":" + SemanticScalar(f, f.GetValue(a), false) + "->" + SemanticScalar(f, f.GetValue(b), false)));
    static string Metadata(BlueprintBuilding b) => string.Join(";", BuildingFields.Where(f => !LinkFields.Contains(f.Name) && !GeometryFields.Contains(f.Name)).Select(f => f.Name + "=" + SemanticScalar(f, f.GetValue(b), false)));
    static string NodeData(BlueprintBuilding b, bool quantized) => b == null ? "null" : string.Join(";", BuildingFields.Where(f => !LinkFields.Contains(f.Name)).Select(f => f.Name + "=" + SemanticScalar(f, f.GetValue(b), quantized)));
    static string SemanticNode(BlueprintBuilding b) => b == null ? "null" : NodeData(b, true) + ";in=" + Pos(b.inputObj) + ";out=" + Pos(b.outputObj);
    static bool HeadingField(FieldInfo f) => f.Name is not ("buildings" or "Version" or "Patch" or "_tmp_buidings" or "_del_buidings");
    static string Heading(BlueprintData b) => string.Join(";", Fields(typeof(BlueprintData)).Where(HeadingField).Select(f => f.Name + "=" + Scalar(f.GetValue(b))));
    static string ExactFingerprint(BlueprintData b)
    {
        var seen = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();
        string Encode(object value)
        {
            if (value == null) return "null";
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is DateTime || value is decimal) return type.FullName + ":" + Scalar(value);
            if (type.IsValueType) return type.FullName + "{" + string.Join(";", Fields(type).Select(f => f.Name + "=" + Encode(f.GetValue(value)))) + "}";
            if (!seen.TryGetValue(value, out int id)) { id = seen.Count; seen[value] = id; queue.Enqueue(value); }
            return "ref#" + id;
        }
        var text = new StringBuilder(Encode(b));
        while (queue.Count > 0)
        {
            var value = queue.Dequeue();
            text.Append('|').Append(seen[value]).Append(':').Append(value.GetType().FullName);
            if (value is IEnumerable seq) text.Append('[').AppendJoin(';', seq.Cast<object>().Select(Encode)).Append(']');
            else text.Append('{').AppendJoin(';', Fields(value.GetType()).Select(f => f.Name + "=" + Encode(f.GetValue(value)))).Append('}');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    static string SemanticFingerprint(BlueprintData b) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Heading(b) + "\n" + string.Join("\n", (b.buildings ?? Array.Empty<BlueprintBuilding>()).Select(SemanticNode).OrderBy(x => x, StringComparer.Ordinal)))));
    static string SemanticDifference(BlueprintData a, BlueprintData b)
    {
        if (Heading(a) != Heading(b))
            return "heading fields: " + string.Join(",", Fields(typeof(BlueprintData)).Where(f => f.Name != "buildings" && Scalar(f.GetValue(a)) != Scalar(f.GetValue(b))).Select(f => f.Name));
        var aa = a.buildings.Select(SemanticNode).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var bb = b.buildings.Select(SemanticNode).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < Math.Min(aa.Length, bb.Length); i++) if (aa[i] != bb[i]) return "first node mismatch: A=" + aa[i] + " B=" + bb[i];
        return "building count " + aa.Length + "->" + bb.Length;
    }
    static BlueprintData Load(string path)
    {
        var b = new BlueprintData();
        var err = b.FromBase64String(File.ReadAllText(path).Trim());
        Require(err == BlueprintDataIOError.OK, "E_PARSE_" + err, path);
        Require(b.buildings != null && b.areas != null, "E_PARSE_EMPTY", path);
        return b;
    }
    static BlueprintData RoundTrip(BlueprintData b)
    {
        string text = b.ToBase64String();
        Require(!string.IsNullOrEmpty(text), "E_SERIALIZE_EMPTY", "game ToBase64String returned empty");
        var r = new BlueprintData();
        var e = r.FromBase64String(text);
        Require(e == BlueprintDataIOError.OK, "E_ROUNDTRIP_" + e, "game FromBase64String rejected result");
        Require(r.buildings != null, "E_ROUNDTRIP_EMPTY", "reload buildings null");
        return r;
    }

    // Directed graph is built independently from BOTH recorded reference directions.
    // Counts alone are insufficient: exact endpoint/branch/turn anchors induce a bijection
    // between original and restored components, and every new directed span must cover only
    // an oriented collinear subpath in its original component (no line invention/shortcut).
    sealed class Graph
    {
        public readonly BlueprintBuilding[] Nodes;
        public readonly HashSet<(BlueprintBuilding A, BlueprintBuilding B)> Edges = new();
        public readonly Dictionary<BlueprintBuilding, List<BlueprintBuilding>> In = new(), Out = new();
        public readonly Dictionary<BlueprintBuilding, int> Line = new();
        public int Components;
        public Graph(BlueprintData data)
        {
            Nodes = data.buildings.Where(IsBelt).ToArray();
            foreach (var n in Nodes) { In[n] = new(); Out[n] = new(); }
            var all = new HashSet<BlueprintBuilding>(data.buildings.Where(n => n != null));
            foreach (var n in data.buildings.Where(n => n != null))
            {
                Require(n.inputObj == null || all.Contains(n.inputObj), "E_DANGLING_INPUT", Pos(n));
                Require(n.outputObj == null || all.Contains(n.outputObj), "E_DANGLING_OUTPUT", Pos(n));
                if (!IsBelt(n)) continue;
                if (IsBelt(n.outputObj)) Edges.Add((n, n.outputObj));
                if (IsBelt(n.inputObj)) Edges.Add((n.inputObj, n));
            }
            foreach (var e in Edges) { Out[e.A].Add(e.B); In[e.B].Add(e.A); }
            foreach (var start in Nodes)
            {
                if (Line.ContainsKey(start)) continue;
                int id = Components++; var q = new Queue<BlueprintBuilding>(); q.Enqueue(start); Line[start] = id;
                while (q.Count > 0)
                {
                    var n = q.Dequeue();
                    foreach (var v in In[n].Concat(Out[n])) if (!Line.ContainsKey(v)) { Line[v] = id; q.Enqueue(v); }
                }
            }
        }
        public HashSet<BlueprintBuilding> Anchors(BlueprintData data)
        {
            var a = new HashSet<BlueprintBuilding>();
            foreach (var n in Nodes)
            {
                if (In[n].Count != 1 || Out[n].Count != 1) { a.Add(n); continue; }
                var p = In[n][0]; var q = Out[n][0];
                if (!AxisContinuation(p, n, q) || p.itemId != n.itemId || q.itemId != n.itemId
                    || p.areaIndex != n.areaIndex || q.areaIndex != n.areaIndex) a.Add(n);
            }
            foreach (var owner in data.buildings.Where(n => n != null && !IsBelt(n)))
            { if (IsBelt(owner.inputObj)) a.Add(owner.inputObj); if (IsBelt(owner.outputObj)) a.Add(owner.outputObj); }
            // A closed collinear cycle (including degenerate two-node cycle) still gets an anchor.
            foreach (int id in Line.Values.Distinct())
                if (!a.Any(n => Line[n] == id)) a.Add(Nodes.First(n => Line[n] == id));
            return a;
        }
    }
    static bool AxisContinuation(BlueprintBuilding a, BlueprintBuilding b, BlueprintBuilding c)
    {
        if (a.areaIndex != b.areaIndex || b.areaIndex != c.areaIndex) return false;
        var u = Vec(b) - Vec(a); var v = Vec(c) - Vec(b);
        return u.sqrMagnitude > 1e-8f && v.sqrMagnitude > 1e-8f
            && Vector3.Cross(u, v).magnitude <= Eps * (u.magnitude + v.magnitude) && Vector3.Dot(u, v) > 0f;
    }
    static Vector3 Vec(BlueprintBuilding b) => new(b.localOffset_x, b.localOffset_y, b.localOffset_z);
    static bool PointOn(BlueprintBuilding p, BlueprintBuilding a, BlueprintBuilding b, out double t)
    {
        t = 0;
        if (p.areaIndex != a.areaIndex || a.areaIndex != b.areaIndex) return false;
        var delta = Vec(b) - Vec(a); double len = delta.sqrMagnitude;
        if (len < 1e-10) return (Vec(p) - Vec(a)).magnitude <= Eps;
        t = Vector3.Dot(Vec(p) - Vec(a), delta) / len;
        return t >= -Eps && t <= 1 + Eps && (Vec(p) - (Vec(a) + delta * (float)t)).magnitude <= Eps;
    }
    static bool Covered(BlueprintBuilding a, BlueprintBuilding b, Graph original, int sourceLine)
    {
        // Exact preserved spans include seam/slope/degenerate links; only new spans
        // require ordinary collinear, orientation-preserving coverage.
        foreach (var e in original.Edges)
            if (original.Line[e.A] == sourceLine && Pos(a) == Pos(e.A) && Pos(b) == Pos(e.B)) return true;
        var d = Vec(b) - Vec(a); double length2 = d.sqrMagnitude;
        if (length2 < 1e-10) return false;
        var intervals = new List<(double Lo, double Hi)>();
        foreach (var e in original.Edges)
        {
            if (original.Line[e.A] != sourceLine || e.A.areaIndex != a.areaIndex || e.B.areaIndex != a.areaIndex) continue;
            var ed = Vec(e.B) - Vec(e.A);
            if (Vector3.Dot(d, ed) <= 0 || ed.sqrMagnitude < 1e-10) continue;
            double lo = Vector3.Dot(Vec(e.A) - Vec(a), d) / length2;
            double hi = Vector3.Dot(Vec(e.B) - Vec(a), d) / length2;
            if (hi < -Eps || lo > 1 + Eps) continue;
            double overlapLo = Math.Max(0, lo), overlapHi = Math.Min(1, hi);
            bool NearOriginal(double t)
            {
                var p = Vec(a) + d * (float)t;
                double u = Vector3.Dot(p - Vec(e.A), ed) / ed.sqrMagnitude;
                var closest = Vec(e.A) + ed * (float)Math.Clamp(u, 0, 1);
                return (p - closest).magnitude <= Eps;
            }
            if (NearOriginal(overlapLo) && NearOriginal(overlapHi)) intervals.Add((overlapLo, overlapHi));
        }
        double reach = 0;
        foreach (var r in intervals.OrderBy(x => x.Lo))
        { if (r.Lo > reach + Eps) return false; reach = Math.Max(reach, r.Hi); if (reach >= 1 - Eps) return true; }
        return false;
    }
    static void Validate(BlueprintData source, BlueprintData result, bool doPlacement = true)
    {
        Require(Heading(source) == Heading(result), "E_HEADING_CHANGED", "all original heading/area/icon/description data must be preserved");
        var old = new Graph(source); var cur = new Graph(result);
        Require(old.Components == cur.Components, "E_COMPONENTS", $"{old.Components}->{cur.Components}");
        var anchors = old.Anchors(source);
        var byPos = cur.Nodes.GroupBy(n => Pos(n)).ToDictionary(g => g.Key, g => g.ToArray());
        var o2r = new Dictionary<int, int>(); var r2o = new Dictionary<int, int>();
        foreach (var anchor in anchors)
        {
            Require(byPos.TryGetValue(Pos(anchor), out var candidates), "E_ANCHOR_LOST", Pos(anchor) + $" indegree={old.In[anchor].Count} outdegree={old.Out[anchor].Count} nearest=" + string.Join(" / ", cur.Nodes.Where(n => n.itemId == anchor.itemId).OrderBy(n => (Vec(n) - Vec(anchor)).sqrMagnitude).Take(2).Select(n => Pos(n) + " delta=" + (Vec(n) - Vec(anchor)).ToString("F7"))));
            var matching = candidates.Where(n => NodeData(n, true) == NodeData(anchor, true)).ToArray();
            Require(matching.Length > 0, "E_ANCHOR_DATA", Pos(anchor));
            // Duplicate-position nodes are not blindly treated as topology matches.
            var lines = matching.Select(n => cur.Line[n]).Distinct().ToArray();
            Require(lines.Length == 1, "E_AMBIGUOUS_ANCHOR", Pos(anchor));
            int ol = old.Line[anchor], rl = lines[0];
            Require(!o2r.TryGetValue(ol, out int prev) || prev == rl, "E_LINE_SPLIT", Pos(anchor));
            Require(!r2o.TryGetValue(rl, out int prevO) || prevO == ol, "E_LINE_MERGED", Pos(anchor));
            o2r[ol] = rl; r2o[rl] = ol;
            var keep = matching[0];
            Require(old.In[anchor].Count == cur.In[keep].Count && old.Out[anchor].Count == cur.Out[keep].Count,
                "E_ENDPOINT_DIRECTION", Pos(anchor) + " indegree/outdegree changed");
        }
        Require(o2r.Count == old.Components && r2o.Count == cur.Components, "E_NEW_LINE", "all components require exact original ancestry");
        foreach (var edge in cur.Edges)
            Require(Covered(edge.A, edge.B, old, r2o[cur.Line[edge.A]]), "E_NEW_PATH", Pos(edge.A) + " -> " + Pos(edge.B));
        // Non-belt geometry, parameters, slots, orientation, input/output targets stay exact.
        var devices = result.buildings.Where(n => n != null && !IsBelt(n)).GroupBy(n => NodeData(n, true)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var owner in source.buildings.Where(n => n != null && !IsBelt(n)))
        {
            string key = NodeData(owner, true);
            Require(devices.TryGetValue(key, out var ds) && ds.Count > 0, "E_DEVICE_GEOMETRY", Pos(owner));
            int i = ds.FindIndex(d => SemanticNode(d) == SemanticNode(owner));
            Require(i >= 0, "E_DEVICE_TARGET", Pos(owner) + " target geometry/ref side changed"); ds.RemoveAt(i);
        }
        Require(devices.Values.All(v => v.Count == 0), "E_NEW_DEVICE", "non-belt multiset changed");
        // Every inserted belt inherits a real donor's exact metadata on its original path.
        var oldNodeKeys = new HashSet<string>(old.Nodes.Select(n => NodeData(n, true)));
        foreach (var n in cur.Nodes.Where(n => !oldNodeKeys.Contains(NodeData(n, true))))
        {
            int line = r2o[cur.Line[n]];
            Require(old.Edges.Any(e => old.Line[e.A] == line && PointOn(n, e.A, e.B, out _) && (MetadataCompatible(e.A, n) || MetadataCompatible(e.B, n))),
                "E_BELT_METADATA", Pos(n) + " no metadata-identical same-path donor; " + string.Join(" / ", old.Edges.Where(e => old.Line[e.A] == line && PointOn(n, e.A, e.B, out _)).Take(2).Select(e => "donor " + Pos(e.A) + " differences=" + MetadataDifference(e.A, n) + " other=" + MetadataDifference(e.B, n))));
            Require(Math.Abs(n.localOffset_x2 - n.localOffset_x) < Eps && Math.Abs(n.localOffset_y2 - n.localOffset_y) < Eps && Math.Abs(n.localOffset_z2 - n.localOffset_z) < Eps,
                "E_BELT_SECONDARY_GEOMETRY", Pos(n) + " added node has stale secondary coordinates");
        }
        if (doPlacement) PlacementCheck(source, result, old, cur);
    }

    // Use production PasteEnvelope only for legal original-clean placements and its hard
    // gate. The separate check below explicitly recomputes float spherical positions and
    // checks both NEW short and long links, which the production upper gate alone cannot.
    static void PlacementCheck(BlueprintData source, BlueprintData result, Graph old, Graph cur)
    {
        var envType = Mod.GetType("BlueprintBeltSaver.PasteEnvelope");
        var env = envType.GetMethod("Build", BF).Invoke(null, new object[] { source.areas, source.primaryAreaIdx, source.buildings, new Func<BlueprintBuilding, bool>(IsBelt), true });
        var spans = new HashSet<string>(old.Edges.Select(e => Pos(e.A) + ">" + Pos(e.B)));
        Func<BlueprintBuilding, BlueprintBuilding, bool> pre = (a, b) => spans.Contains(Pos(a) + ">" + Pos(b));
        var gate = envType.GetMethod("Gate", BF).Invoke(env, new object[] { result.buildings, new Func<BlueprintBuilding, bool>(IsBelt), pre });
        var frames = ((IEnumerable)Value(env, "ProtectedFrames")).Cast<object>().ToArray();
        var newEdges = cur.Edges.Where(e => !pre(e.A, e.B)).ToArray();
        if (newEdges.Length == 0) return;
        Require(frames.Length > 0, "E_NO_LEGAL_PLACEMENTS", "new links without protected original-clean legal placements");
        Require(Convert.ToInt32(Value(gate, "Over"), Inv) == 0, "E_PASTE_GATE", Convert.ToString(Value(gate, "First"), Inv));
        double worst = 0, shortest = double.PositiveInfinity;
        foreach (var f in frames)
        {
            foreach (var e in newEdges)
            {
                float d2 = (IndependentPosition(e.A, f) - IndependentPosition(e.B, f)).sqrMagnitude;
                worst = Math.Max(worst, Math.Sqrt(d2)); shortest = Math.Min(shortest, Math.Sqrt(d2));
                Require(d2 <= 5.3f + 0.00002f, "E_NEW_TOO_LONG", $"{Pos(e.A)}->{Pos(e.B)} length={Math.Sqrt(d2):R}");
                Require(d2 >= MinMeters * MinMeters - 0.00002, "E_NEW_TOO_SHORT", $"{Pos(e.A)}->{Pos(e.B)} length={Math.Sqrt(d2):R}");
            }
        }
        Info($"  placement frames={frames.Length} new-links={newEdges.Length} length=[{shortest:F6},{worst:F6}]m");
    }
    static Vector3 IndependentPosition(BlueprintBuilding b, object f)
    {
        var lat = (float[])Value(f, "RefLat"); var lon = (float[])Value(f, "RefLon"); var grid = (float[])Value(f, "LonPerGrid");
        int ai = lat.Length == 1 ? 0 : b.areaIndex;
        bool swap = (bool)Value(f, "Swap");
        float signX = (float)Value(f, "LonSign"), signY = (float)Value(f, "LatSign");
        float x = swap ? b.localOffset_y : b.localOffset_x, y = swap ? b.localOffset_x : b.localOffset_y;
        float phi = lat[ai] + y * (MathF.PI * 2f / 1000f) * signY;
        phi = Math.Clamp(phi, -MathF.PI / 2f, MathF.PI / 2f);
        return BlueprintUtils.GetDir(lon[ai] + x * grid[ai] * signX, phi) * (200.2f + b.localOffset_z * 1.3333333f);
    }

    static BlueprintData Seed(string originals)
    {
        string p = Directory.GetFiles(originals, "*.txt").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        Require(p != null, "E_SEED_MISSING", originals);
        return Load(p);
    }
    static BlueprintData Fixture(BlueprintData seed, params BlueprintBuilding[] nodes)
    {
        var b = seed.Clone(); Require(b != null, "E_SEED_CLONE", "seed has no full headings");
        var area = new BlueprintArea();
        foreach (var f in Fields(typeof(BlueprintArea))) if (f.FieldType.IsValueType) f.SetValue(area, Activator.CreateInstance(f.FieldType));
        Set(area, "index", 0); Set(area, "parentIndex", -1); Set(area, "areaSegments", 200); Set(area, "width", 48); Set(area, "height", 32);
        b.areas = new[] { area }; b.primaryAreaIdx = 0; b.buildings = nodes;
        for (int i = 0; i < nodes.Length; i++) { if (nodes[i] == null) continue; nodes[i].index = i; Set(nodes[i], "areaIndex", 0); }
        return b;
    }
    static BlueprintBuilding Belt(float x, float y = 0, float z = 0, int tier = 2001)
    {
        var n = new BlueprintBuilding();
        Set(n, "itemId", tier); Set(n, "modelIndex", tier == 2001 ? 35 : tier == 2002 ? 36 : 37); Set(n, "areaIndex", 0);
        Set(n, "inputToSlot", 1); Set(n, "outputToSlot", 1); Set(n, "tempInputObjIdx", -1); Set(n, "tempOutputObjIdx", -1);
        n.localOffset_x = x; n.localOffset_y = y; n.localOffset_z = z;
        Set(n, "localOffset_x2", x); Set(n, "localOffset_y2", y); Set(n, "localOffset_z2", z);
        return n;
    }
    static BlueprintBuilding[] Chain(float[] values, bool vertical = false, bool reversed = false, float transverse = 0, float z = 0)
    {
        var ns = values.Select(v => vertical ? Belt(transverse, v, z) : Belt(v, transverse, z)).ToArray();
        if (reversed) Array.Reverse(ns);
        for (int i = 0; i + 1 < ns.Length; i++) Link(ns[i], ns[i + 1]);
        return ns;
    }
    static void Link(BlueprintBuilding a, BlueprintBuilding b, bool reciprocal = true) { a.outputObj = b; if (reciprocal) b.inputObj = a; }
    static void Grid(BlueprintData b, bool vertical, float transverse, float lo, float hi)
    {
        foreach (var n in b.buildings.Where(IsBelt))
        {
            float v = vertical ? n.localOffset_y : n.localOffset_x;
            Require(Math.Abs((vertical ? n.localOffset_x : n.localOffset_y) - transverse) < Eps, "E_TRANSVERSE_MOVED", Pos(n));
            Require(v >= lo - Eps && v <= hi + Eps, "E_ENDPOINT_OVERSHOOT", Pos(n));
            if (Math.Abs(v - lo) > Eps && Math.Abs(v - hi) > Eps)
                Require(Math.Abs(v - Math.Round(v)) < Eps, "E_NOT_GRID", Pos(n));
        }
    }
    static Outcome TestRestore(BlueprintData b, bool expectSuccess = true, bool requireAdded = false, bool noChange = false, bool validate = true)
    {
        var o = Invoke(b); Info("  " + o);
        Require(o.Ok == expectSuccess, "E_OUTCOME", o.ToString());
        if (!o.Ok) return o;
        if (validate) Validate(b, o.Result);
        if (requireAdded) Require(o.Int("After") > o.Int("Before") && o.Int("Added") > 0, "E_NO_RESTORATION", o.ToString());
        if (noChange) Require(SemanticFingerprint(b) == SemanticFingerprint(o.Result) && o.Int("Added") == 0 && o.Int("Removed") == 0, "E_EXPECTED_PROTECTED", o.ToString());
        var again = Invoke(o.Result);
        Require(again.Ok && again.Int("Added") == 0 && again.Int("Removed") == 0 && SemanticFingerprint(o.Result) == SemanticFingerprint(again.Result), "E_NOT_IDEMPOTENT", again.ToString());
        // The loaded result is checked against the original geometry/topology again.
        var loaded = RoundTrip(o.Result);
        if (validate) Validate(b, loaded);
        Require(SemanticFingerprint(o.Result) == SemanticFingerprint(loaded), "E_SERIALIZATION_SEMANTICS", "reload changed fields/targets");
        var third = Invoke(loaded);
        Require(third.Ok && third.Int("Added") == 0 && third.Int("Removed") == 0 && SemanticFingerprint(loaded) == SemanticFingerprint(third.Result), "E_ROUNDTRIP_NOT_IDEMPOTENT", third.ToString());
        return o;
    }
    static void OracleFixtures(BlueprintData seed)
    {
        void Reject(Action body)
        {
            bool rejected = false;
            try { body(); } catch (CheckException e) { rejected = true; Info("  independent oracle rejected " + e.Code); }
            Require(rejected, "E_ORACLE_FALSE_NEGATIVE", "unsafe mutation escaped the independent oracle");
        }
        Check("oracle-swapped-tails-same-component-count", () =>
        {
            var a = Chain(new[] { 0f, 1f, 2f, 3f }); var b = Chain(new[] { 0f, 1f, 2f, 3f }, transverse: 1);
            var src = Fixture(seed, a.Concat(b).ToArray()); var dst = src.Clone();
            var aa = dst.buildings.Where(n => n.localOffset_y == 0).OrderBy(n => n.localOffset_x).ToArray();
            var bb = dst.buildings.Where(n => n.localOffset_y == 1).OrderBy(n => n.localOffset_x).ToArray();
            Link(aa[1], bb[2]); Link(bb[1], aa[2]);
            Require(new Graph(src).Components == new Graph(dst).Components, "E_ORACLE_SETUP", "must test more than line count"); Reject(() => Validate(src, dst, false));
        });
        Check("oracle-reversed-direction", () =>
        {
            var src = Fixture(seed, Chain(new[] { 0f, 1f, 2f, 3f })); var dst = src.Clone(); var n = dst.buildings.OrderBy(x => x.localOffset_x).ToArray();
            foreach (var x in n) { x.inputObj = null; x.outputObj = null; } for (int i = 1; i < n.Length; i++) Link(n[i], n[i - 1]); Reject(() => Validate(src, dst, false));
        });
        Check("oracle-ring-cut-still-one-component", () =>
        {
            var ns = new[] { Belt(0), Belt(1), Belt(1, 1), Belt(0, 1) }; for (int i = 0; i < ns.Length; i++) Link(ns[i], ns[(i + 1) % ns.Length]);
            var src = Fixture(seed, ns); var dst = src.Clone(); var last = dst.buildings.Last(); var first = last.outputObj; last.outputObj = null; first.inputObj = null;
            Require(new Graph(dst).Components == 1, "E_ORACLE_SETUP", "ring cut must retain one component"); Reject(() => Validate(src, dst, false));
        });
        Check("oracle-device-retarget-same-line", () =>
        {
            var ns = Chain(new[] { 0f, 1f, 2f, 3f }); var owner = Belt(1, 4); Set(owner, "itemId", 2011); owner.inputObj = ns[1];
            var src = Fixture(seed, ns.Concat(new[] { owner }).ToArray()); var dst = src.Clone();
            dst.buildings.First(n => !IsBelt(n)).inputObj = dst.buildings.First(n => IsBelt(n) && n.localOffset_x == 2); Reject(() => Validate(src, dst, false));
        });
        Check("oracle-belt-metadata-corruption", () =>
        {
            var src = Fixture(seed, Chain(new[] { 0f, 1f, 2f, 3f })); var dst = src.Clone(); dst.buildings[1].filterId = 1001; Reject(() => Validate(src, dst, false));
        });
        Check("oracle-exact-source-fingerprint-all-data", () =>
        {
            var src = Fixture(seed, Chain(new[] { 0f, 1f, 2f, 3f })); Set(src.buildings[1], "parameters", new[] { 1, 2, 3 }); string fp = ExactFingerprint(src);
            src.buildings[1].parameters[1] = 9; Require(fp != ExactFingerprint(src), "E_ORACLE_FINGERPRINT", "parameter contents ignored");
            fp = ExactFingerprint(src); src.buildings[1].inputObj = src.buildings[2]; Require(fp != ExactFingerprint(src), "E_ORACLE_FINGERPRINT", "object refs ignored");
            fp = ExactFingerprint(src); src.buildings[1].localOffset_z2 = .5f; Require(fp != ExactFingerprint(src), "E_ORACLE_FINGERPRINT", "secondary geometry ignored");
        });
    }
    static void Fixtures(string originals)
    {
        var seed = Seed(originals);
        OracleFixtures(seed);
        Info("FIXTURES real game assembly=" + typeof(BlueprintData).Assembly.Location);
        foreach (var spec in new[] { ("horizontal-offgrid", false, false), ("vertical-offgrid", true, false), ("reversed-offgrid", false, true) })
        {
            Check(spec.Item1, () => { var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, spec.Item2, spec.Item3)); var o = TestRestore(b, requireAdded: true); Grid(o.Result, spec.Item2, 0, 0, 6); });
        }
        Check("long-grid-spans-net-added-allowed", () => { var b = Fixture(seed, Chain(new[] { 0f, 2f, 4f, 6f })); Grid(TestRestore(b, requireAdded: true).Result, false, 0, 0, 6); });
        Check("same-grid-no-op", () => TestRestore(Fixture(seed, Chain(new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f })), noChange: true));
        Check("narrow-final-gap-protected", () => { var b = Fixture(seed, Chain(new[] { 0.25f, 1.65f, 3.05f, 4.45f, 5.85f, 6.1f })); var o = TestRestore(b); Require(o.Result.buildings.Where(IsBelt).All(n => Math.Abs(n.localOffset_x - 6f) > Eps), "E_NARROW_TAIL", "introduced lattice point at 6 creates unsafe 0.1-cell tail"); });
        Check("offgrid-endpoints-exact", () => { var b = Fixture(seed, Chain(new[] { 0.5f, 2f, 3.5f, 5f, 6.5f })); var o = TestRestore(b); Grid(o.Result, false, 0, .5f, 6.5f); });
        Check("fractional-transverse-protected", () => TestRestore(Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, transverse: .25f)), noChange: true));
        Check("slope-height-protected", () => { var n = Chain(new[] { 0f, 1f, 2f, 3f }); for (int i = 0; i < n.Length; i++) { n[i].localOffset_z = i * .5f; Set(n[i], "localOffset_z2", i * .5f); } TestRestore(Fixture(seed, n), noChange: true); });
        Check("diagonal-curved-protected", () => { var n = Chain(new[] { 0f, 1f, 2f, 3f }); for (int i = 0; i < n.Length; i++) { n[i].localOffset_y = i * .4f; Set(n[i], "localOffset_y2", i * .4f); } TestRestore(Fixture(seed, n), noChange: true); });
        Check("sharp-turn-exact", () => { var n = new[] { Belt(0), Belt(1.5f), Belt(3), Belt(3, 1.5f), Belt(3, 3) }; for (int i = 0; i + 1 < n.Length; i++) Link(n[i], n[i + 1]); TestRestore(Fixture(seed, n)); });
        Check("degenerate-coincident-protected", () => { var n = new[] { Belt(0), Belt(0), Belt(1) }; Link(n[0], n[1]); Link(n[1], n[2]); var b = Fixture(seed, n); var o = Invoke(b); Require(!o.Ok || SemanticFingerprint(b) == SemanticFingerprint(o.Result), "E_DEGENERATE_CHANGED", o.ToString()); });
        Check("ring-no-new-line", () => { var n = new[] { Belt(0), Belt(1.5f), Belt(3), Belt(3, 1.5f), Belt(3, 3), Belt(1.5f, 3), Belt(0, 3), Belt(0, 1.5f) }; for (int i = 0; i < n.Length; i++) Link(n[i], n[(i + 1) % n.Length]); TestRestore(Fixture(seed, n)); });
        Check("merge-multiple-inputs-no-invented-line", () => { var a = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }); var b = new[] { Belt(3, -3), Belt(3, -1.5f) }; Link(b[0], b[1]); Link(b[1], a[2], false); TestRestore(Fixture(seed, a.Concat(b).ToArray())); });
        Check("parallel-lines-no-cross-ancestry", () => { var a = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }); var b = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, transverse: 1); TestRestore(Fixture(seed, a.Concat(b).ToArray())); });
        Check("seam-protected", () => { var n = new[] { Belt(598), Belt(599.5f), Belt(-.5f), Belt(1) }; for (int i = 0; i + 1 < n.Length; i++) Link(n[i], n[i + 1]); var b = Fixture(seed, n); var o = TestRestore(b); Require(o.Result.buildings.Where(IsBelt).Any(x => Math.Abs(x.localOffset_x - 599.5f) < Eps) && o.Result.buildings.Where(IsBelt).Any(x => Math.Abs(x.localOffset_x + .5f) < Eps), "E_SEAM_MOVED", "seam endpoints changed"); });
        Check("attachment-interior-inserter-machine-exact", () =>
        {
            var ns = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f, 7.5f, 9f });
            var sorter = Belt(3, 2); Set(sorter, "itemId", 2011); Set(sorter, "modelIndex", 41); sorter.inputObj = ns[2];
            var machine = Belt(12, 5); Set(machine, "itemId", 2303); Set(machine, "modelIndex", 65); sorter.outputObj = machine;
            Set(sorter, "localOffset_x2", 3); Set(sorter, "localOffset_y2", 0); Set(sorter, "inputFromSlot", 1); Set(sorter, "outputToSlot", 2);
            TestRestore(Fixture(seed, ns.Concat(new[] { sorter, machine }).ToArray()));
        });
        Check("tier-boundary-and-belt-metadata", () =>
        {
            var ns = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f, 7.5f, 9f });
            for (int i = 3; i < ns.Length; i++) { Set(ns[i], "itemId", 2002); Set(ns[i], "modelIndex", 36); }
            Set(ns[2], "parameters", new[] { 17, 23, 42 }); Set(ns[2], "recipeId", 99); Set(ns[2], "filterId", 1001);
            TestRestore(Fixture(seed, ns));
        });
        Check("collision-no-new-pole-hit", () =>
        {
            var ns = Chain(new[] { 0f, 1.5f, 2.2f, 3.8f, 4.5f, 6f }); var pole = Belt(3); Set(pole, "itemId", 2201); Set(pole, "modelIndex", 44);
            var o = TestRestore(Fixture(seed, ns.Concat(new[] { pole }).ToArray()));
            Require(o.Result.buildings.Where(IsBelt).All(n => Math.Abs(n.localOffset_x - 3f) > .27f), "E_NEW_COLLISION", "restored belt inside pole node-sphere/AABB clearance");
        });
        Check("cross-area-seam-attachment-protected", () =>
        {
            var ns = Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }); var b = Fixture(seed, ns); var area = new BlueprintArea();
            foreach (var f in Fields(typeof(BlueprintArea))) f.SetValue(area, f.GetValue(b.areas[0]));
            Set(area, "index", 1); Set(area, "parentIndex", 0); Set(area, "anchorLocalOffsetY", 32); b.areas = new[] { b.areas[0], area };
            for (int i = 2; i < ns.Length; i++) Set(ns[i], "areaIndex", 1);
            var o = Invoke(b); Require(!o.Ok || o.Result.buildings.Any(n => Pos(n) == Pos(ns[1])) && o.Result.buildings.Any(n => Pos(n) == Pos(ns[2])), "E_CROSS_AREA_MOVED", o.ToString());
            if (o.Ok) Validate(b, o.Result);
        });
        Check("null-safe-error", () => TestRestore(null, expectSuccess: false));
        Check("missing-buildings-safe-error", () => { var b = Fixture(seed); b.buildings = null; TestRestore(b, expectSuccess: false); });
        Check("empty-blueprint-no-op-or-code", () => { var b = Fixture(seed); var o = Invoke(b); if (o.Ok) { Require(o.Result.buildings.Length == 0, "E_EMPTY_INVENTION", o.ToString()); } });
        Check("nonfinite-safe-error", () => { var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f })); b.buildings[1].localOffset_x = float.NaN; TestRestore(b, expectSuccess: false); });
        Check("dangling-safe-error", () => { var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f })); b.buildings[1].outputObj = Belt(9); TestRestore(b, expectSuccess: false); });
        Check("strict-grid-supported-success", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, z: 1.5f));
            var o = Invoke(b, true, true); Require(o.Ok, "E_STRICT_VALID_REJECTED", o.ToString()); Validate(b, o.Result); Grid(o.Result, false, 0, 0, 6);
            Require(o.Result.buildings.Where(IsBelt).All(n => Math.Abs(n.localOffset_z * 2 - Math.Round(n.localOffset_z * 2)) < Eps), "E_HEIGHT_NOT_HALFGRID", o.ToString());
        });
        Check("strict-grid-offgrid-endpoint-rejected", () =>
        {
            var b = Fixture(seed, Chain(new[] { .25f, 1.65f, 3.05f, 4.45f, 6.25f })); var o = Invoke(b, true, true);
            Require(!o.Ok && o.Reason == "strict-grid-conflict", "E_STRICT_ENDPOINT", o.ToString());
        });
        Check("strict-grid-fractional-transverse-rejected", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, transverse: .25f)); var o = Invoke(b, true, true);
            Require(!o.Ok && o.Reason == "strict-grid-conflict", "E_STRICT_TRANSVERSE", o.ToString());
        });
        Check("height-quantized-default-incompatible-protected", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, z: 1.25f)); var o = Invoke(b);
            Require(o.Ok, "E_PARTIAL_HEIGHT_REJECTED", o.ToString()); Validate(b, o.Result);
            Require(SemanticFingerprint(b) == SemanticFingerprint(o.Result), "E_UNSAFE_HEIGHT_SNAP", "unsupported height must not move exact endpoints");
        });
        Check("height-not-quantized-restores-and-preserves", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, z: 1.25f)); var o = Invoke(b, false);
            Require(o.Ok && o.Int("Added") > 0, "E_HEIGHT_OPTION", o.ToString()); Validate(b, o.Result); Grid(o.Result, false, 0, 0, 6);
            Require(o.Result.buildings.Where(IsBelt).All(n => Math.Abs(n.localOffset_z - 1.25f) < Eps), "E_HEIGHT_CHANGED", o.ToString());
            var again = Invoke(o.Result, false); Require(again.Ok && again.Int("Added") == 0 && again.Int("Removed") == 0 && SemanticFingerprint(o.Result) == SemanticFingerprint(again.Result), "E_HEIGHT_IDEMPOTENCE", again.ToString());
        });
        Check("strict-height-policy-both-configurations", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, z: 1.25f)); var yes = Invoke(b, true, true); var no = Invoke(b, false, true);
            Require(!yes.Ok && yes.Reason == "strict-grid-conflict", "E_STRICT_HEIGHT", yes.ToString());
            Require(no.Ok, "E_STRICT_HEIGHT_DISABLED", no.ToString()); Validate(b, no.Result); Grid(no.Result, false, 0, 0, 6);
        });
        Check("default-config-independent", () =>
        {
            var b = Fixture(seed, Chain(new[] { 0f, 1.5f, 3f, 4.5f, 6f })); var first = Invoke(b);
            var names = new[] { "BBS_REBIND", "BBS_FORCEREBIND", "BBS_CAP", "BBS_SAFETY" }; var before = names.ToDictionary(k => k, Environment.GetEnvironmentVariable);
            try { foreach (string k in names) Environment.SetEnvironmentVariable(k, "0"); var second = Invoke(b); Require(first.Ok && second.Ok && SemanticFingerprint(first.Result) == SemanticFingerprint(second.Result), "E_CONFIG_DEPENDENCY", "static restorer must not depend on optimization config/env"); }
            finally { foreach (var kv in before) Environment.SetEnvironmentVariable(kv.Key, kv.Value); }
        });
    }

    static void Batch(string input, string exportDir, bool libraryOnly)
    {
        libraryOnly |= Environment.GetCommandLineArgs().Contains("--library-only");
        Require(Directory.Exists(input) || File.Exists(input), "E_INPUT_DIR", input);
        var files = File.Exists(input) ? new[] { input } : Directory.GetFiles(input, libraryOnly ? "A_lib_*.txt" : "*.txt").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Require(files.Length > 0, "E_INPUT_NO_FILES", input);
        if (exportDir != null) Directory.CreateDirectory(exportDir);
        Info($"BATCH input={input} files={files.Length} export={exportDir ?? "none"}");
        int totalBefore = 0, totalAfter = 0, totalAdded = 0, totalRemoved = 0;
        foreach (string file in files)
        {
            Check("batch:" + Path.GetFileNameWithoutExtension(file), () =>
            {
                var src = Load(file); var o = Invoke(src); Info("  " + o);
                totalBefore += o.Int("Before");
                if (!o.Ok)
                {
                    Refused++; Info("  SAFE-REFUSAL " + o.Reason + " source unchanged");
                    Require(o.Result == null || SemanticFingerprint(src) == SemanticFingerprint(o.Result), "E_REJECTED_CHANGED", "false must not provide partial edits");
                    return;
                }
                totalAfter += o.Int("After"); totalAdded += o.Int("Added"); totalRemoved += o.Int("Removed");
                Validate(src, o.Result);
                var again = Invoke(o.Result);
                Require(again.Ok && again.Int("Added") == 0 && again.Int("Removed") == 0 && SemanticFingerprint(o.Result) == SemanticFingerprint(again.Result), "E_BATCH_IDEMPOTENCE", again.ToString());
                var loaded = RoundTrip(o.Result); Validate(src, loaded);
                Require(SemanticFingerprint(o.Result) == SemanticFingerprint(loaded), "E_BATCH_ROUNDTRIP_SEMANTICS", "game serialization changed geometry/metadata/reference targets");
                var reloaded = Invoke(loaded);
                Require(reloaded.Ok && reloaded.Int("Added") == 0 && reloaded.Int("Removed") == 0 && SemanticFingerprint(loaded) == SemanticFingerprint(reloaded.Result), "E_BATCH_RELOADED_IDEMPOTENCE", reloaded.ToString());
                if (exportDir != null)
                {
                    string target = Path.Combine(exportDir, "A_R_" + Path.GetFileName(file));
                    File.WriteAllText(target, o.Result.ToBase64String(), new UTF8Encoding(false));
                    var exported = Load(target);
                    Require(SemanticFingerprint(exported) == SemanticFingerprint(loaded), "E_EXPORT_SEMANTICS", target);
                    Require(Heading(src) == Heading(exported), "E_EXPORT_HEADING", target);
                    Info("  EXPORT " + target);
                }
            });
        }
        Info($"BATCH TOTAL belts={totalBefore}->{totalAfter} added={totalAdded} removed={totalRemoved} refused={Refused}; net added is permitted for restoration");
    }
    static string PairName(string file)
    {
        string n = Path.GetFileNameWithoutExtension(file);
        foreach (string prefix in new[] { "A_R_", "B_R_", "A_lib_", "B_lib_" }) if (n.StartsWith(prefix, StringComparison.Ordinal)) n = n.Substring(prefix.Length);
        return n;
    }
    static void Compare(string a, string b)
    {
        Require(Directory.Exists(a) && Directory.Exists(b), "E_COMPARE_DIR", a + " / " + b);
        var fa = Directory.GetFiles(a, "*.txt").GroupBy(PairName).ToDictionary(g => g.Key, g => g.ToArray());
        var fb = Directory.GetFiles(b, "*.txt").GroupBy(PairName).ToDictionary(g => g.Key, g => g.ToArray());
        Require(fa.Count > 0 && fb.Count > 0, "E_COMPARE_EMPTY", "need blueprint files in both directories");
        foreach (string key in fa.Keys.Union(fb.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            Check("compare:" + key, () =>
            {
                bool hasA = fa.TryGetValue(key, out var aa), hasB = fb.TryGetValue(key, out var bb);
                Require(hasA && hasB, "E_COMPARE_UNPAIRED", key);
                Require(aa.Length == 1 && bb.Length == 1, "E_COMPARE_AMBIGUOUS", key + " has more than one candidate; use library-only folders");
                var ba = Load(aa[0]); var bc = Load(bb[0]);
                Require(SemanticFingerprint(ba) == SemanticFingerprint(bc), "E_SEMANTIC_DIFF", "full data/ref/header multiset differs (coordinates quantized 1/8192; index renumbering ignored)");
                Info("  SEMANTIC-IDENTICAL " + key);
            });
        }
    }
}
