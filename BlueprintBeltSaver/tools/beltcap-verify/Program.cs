using System.Globalization;
using System.Reflection;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

// 徒手拉带米制托底专项验证（004.18 / 2026-11-18）：直接反射调用构建 DLL 的
// LongerSnapLineNonAlloc / LongerGeodesic，断言输出相邻吸附点的实际弦长全部 ≤
// min(最远距离, 2.301172×0.98)。覆盖：经纬线 3 格（旧设置必挂场景）、1.75 格默认、
// 不同 currentDistance、高空带半径、45° 纬线、经线方向、测地线普通与螺旋（含高度差）。
// 退出码：0 全过；1 有失败。
internal static class Program
{
    private const BindingFlags BF = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const float HardCap = 2.301172f;
    private static int _pass, _fail;

    private static void Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var modDll = Path.Combine(AppContext.BaseDirectory, "BlueprintBeltSaver.dll");
        var mod = Assembly.LoadFrom(modDll);
        Console.WriteLine("MOD " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modDll))));
        var path = mod.GetType("BlueprintBeltSaver.LongerBeltPath")!;
        var grid = new PlanetGrid(EGridType.LatitudeLongitude, 200, UnityEngine.Quaternion.identity);

        void Set(string field, object v) => path.GetField(field, BF)!.SetValue(null, v);
        void Run(string label, float beginLatDeg, float spanDeg, float endLatDeg, float radius, float current)
        {
            Set("longerOnGrid", true);
            Set("shortestUnlimit", false);
            Set("currentDistance", current);
            float lat0 = beginLatDeg * Mathf.Deg2Rad, lat1 = endLatDeg * Mathf.Deg2Rad;
            var begin = new Vector3(Mathf.Cos(lat0), Mathf.Sin(lat0), 0f) * radius;
            var end = new Vector3(Mathf.Cos(spanDeg * Mathf.Deg2Rad) * Mathf.Cos(lat1), Mathf.Sin(lat1), -Mathf.Sin(spanDeg * Mathf.Deg2Rad) * Mathf.Cos(lat1)) * radius;
            var snaps = new Vector3[4096];
            object?[] args = { grid, begin, end, 1, snaps, 0 };
            int count = (int)path.GetMethod("LongerSnapLineNonAlloc", BF)!.Invoke(null, args)!;
            float cap = MathF.Min(current, HardCap);
            float worst = 0f;
            for (int i = 1; i < count; i++) worst = MathF.Max(worst, Vector3.Distance(snaps[i - 1], snaps[i]) * radius);
            bool ok = count >= 2 && worst <= cap + 0.0005f;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} grid-{label}: nodes={count} cap={cap:0.0000} worst={worst:0.0000} radius={radius:0.0}");
            if (ok) _pass++; else _fail++;
        }

        Run("3cells-equator-cap2.301172", 0f, 36f, 0f, 200.2f, 2.301172f);   // 旧 3 格设置跨度：超限自动加密
        Run("3cells-equator-cap1.9", 0f, 36f, 0f, 200.2f, 1.9f);
        Run("1.75cells-default", 0f, 24f, 0f, 200.2f, 1.9f);
        Run("3cells-highaltitude", 0f, 30f, 0f, 208.2f, 2.301172f);          // 高空带：半径更大
        Run("latitude45", 45f, 24f, 45f, 200.2f, 2.301172f);                 // 45° 纬线方向
        Run("meridian", 0f, 0.001f, 24f, 200.2f, 2.301172f);                 // 沿经线（纬度方向）
        Run("latitude82", 82.04f, 10f, 82.04f, 200.4f, 2.301172f);           // 82° 高纬：每格 ≈1.09 m
        Run("latitude82-meridian", 82.04f, 0.001f, 8f, 200.4f, 2.301172f);   // 82° 沿经线

        void Geodesic(string label, float current, float heightEnd, bool spiral)
        {
            Set("currentDistance", current);
            Set("isometricSegmentation", spiral ? 1 : 0);
            float r0 = 200.2f, r1 = 200.2f + heightEnd;
            var begin = new Vector3(Mathf.Cos(10f * Mathf.Deg2Rad), Mathf.Sin(10f * Mathf.Deg2Rad), 0f) * r0;
            var end = new Vector3(Mathf.Cos(30f * Mathf.Deg2Rad) * 0.8f, Mathf.Sin(30f * Mathf.Deg2Rad) * 0.8f, -Mathf.Sin(30f * Mathf.Deg2Rad) * 0.8f) * r1;
            var snaps = new Vector3[4096];
            int counts = 0;
            object?[] args = { begin, end, counts, snaps };
            bool unitSnaps = (bool)path.GetMethod("LongerGeodesic", BF)!.Invoke(null, args)!;
            counts = (int)args[2]!;
            // 返回 true = 单位向量（游戏侧再乘半径），false = 已带真实半径
            float radiusScale = unitSnaps ? r0 : 1f;
            float cap = MathF.Min(current, HardCap);
            float worst = 0f;
            for (int i = 1; i < counts; i++) worst = MathF.Max(worst, Vector3.Distance(snaps[i - 1], snaps[i]) * radiusScale);
            bool ok = counts >= 2 && worst <= cap + 0.0005f;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} geodesic-{label}: nodes={counts} unitSnaps={unitSnaps} cap={cap:0.0000} worst={worst:0.0000} heightEnd={heightEnd:0.#}");
            if (ok) _pass++; else _fail++;
        }
        Geodesic("plain-cap2.301172", 2.301172f, 0f, spiral: false);
        Geodesic("plain-cap1.9", 1.9f, 0f, spiral: false);
        Geodesic("spiral-cap2.301172-h6", 2.301172f, 6f, spiral: true);
        Geodesic("spiral-cap1.9-h6", 1.9f, 6f, spiral: true);

        Console.WriteLine($"SUMMARY PASS={_pass} FAIL={_fail}");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
