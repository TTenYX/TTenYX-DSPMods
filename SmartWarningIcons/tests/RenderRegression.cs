using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SmartWarningIcons.Tests
{
    public sealed partial class SelfTestPlugin
    {
        private string TestRenderedSigns()
        {
            const int width = 384, height = 160;
            const CameraEvent drawEvent = CameraEvent.AfterEverything;
            var source = Configs.builtin.entitySignMat;
            Require(source != null && source.shader.isSupported && source.passCount > 0, "Entity Sign Batch material is unavailable on this graphics backend");
            var material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            var stage = new GameObject("SWI SelfTest sign camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = stage.AddComponent<Camera>();
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { hideFlags = HideFlags.HideAndDontSave };
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            var timeId = Shader.PropertyToID("_Time");
            var previousTime = Shader.GetGlobalVector(timeId);
            // Actual shader alpha: saturate(sin(_Time.y * 3.2) * 2 + 0.6). Capture its bright phase.
            const float bright = Mathf.PI / 6.4f;
            var brightTime = new Vector4(bright / 20f, bright, 2f * bright, 3f * bright);
            var scenarios = new (string Name, int Mask, bool Red, bool Yellow)[]
            {
                ("all on", int.MaxValue, true, true),
                ("red off", int.MaxValue & ~RedMask, false, true),
                ("yellow off", int.MaxValue & ~YellowMask, true, false),
                ("both off", int.MaxValue & ~(RedMask | YellowMask), false, false)
            };
            var frames = new Color32[scenarios.Length][];
            try
            {
                Require(target.Create(), "could not allocate sign test render target");
                camera.enabled = false;
                camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
                camera.renderingPath = RenderingPath.Forward;
                camera.depthTextureMode = DepthTextureMode.Depth;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.orthographic = true;
                camera.orthographicSize = 1.25f;
                camera.aspect = (float)width / height;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.transform.position = new Vector3(0f, 0f, -10f);

                // The original shader pass, buffer stride and 8 vertices per entity; no live factory changes.
                var data = new SignData[]
                {
                    default,
                    new SignData { signType = Red, x = -1.9f, w = 1.35f },
                    new SignData { signType = SignData.CUT_PRODUCTION_SOON, x = 0f, w = 1.35f },
                    new SignData { signType = Yellow, x = 1.9f, w = 1.35f }
                };
                using var signs = new ComputeBuffer(data.Length, SignData.dataLen);
                using var args = new ComputeBuffer(3, sizeof(int));
                using var emptyIndex = new ComputeBuffer(1, sizeof(uint));
                using var commands = new CommandBuffer { name = "SWI SelfTest Entity Sign Batch" };
                signs.SetData(data);
                emptyIndex.SetData(new uint[] { 0 });
                var properties = new MaterialPropertyBlock();
                properties.SetBuffer("_SignBuffer", signs);
                properties.SetBuffer("_Global_Int32_Vars", args);
                properties.SetBuffer("_Global_ItemIconIndexBuffer", emptyIndex);
                properties.SetBuffer("_Global_RecipeIconIndexBuffer", emptyIndex);
                properties.SetBuffer("_Global_TechIconIndexBuffer", emptyIndex);
                properties.SetBuffer("_Global_SignalIconIndexBuffer", emptyIndex);
                properties.SetTexture("_Global_IconTex", Texture2D.blackTexture);
                properties.SetTexture("_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? Texture2D.blackTexture : Texture2D.whiteTexture);
                properties.SetFloat("_Global_ShowEntitySign", 1f);
                properties.SetFloat("_Global_ShowEntityIcon", 0f);
                properties.SetVector(timeId, brightTime);
                commands.SetGlobalVector(timeId, brightTime);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Quads, 8 * data.Length, 1, properties);
                commands.SetGlobalVector(timeId, previousTime);
                camera.AddCommandBuffer(drawEvent, commands);
                try
                {
                    for (var i = 0; i < scenarios.Length; i++)
                    {
                        args.SetData(new[] { 0, 0, scenarios[i].Mask });
                        camera.Render();
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                        frames[i] = readback.GetPixels32();
                    }
                }
                finally { camera.RemoveCommandBuffer(drawEvent, commands); }
            }
            finally
            {
                Shader.SetGlobalVector(timeId, previousTime);
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(readback);
                Object.Destroy(target);
                Object.Destroy(stage);
                Object.Destroy(material);
            }

            const string evidence = "rendered-signs.png";
            SaveSignFrames(frames, width, height, Path.Combine(_directory, evidence));
            var summary = new string[scenarios.Length];
            for (var i = 0; i < scenarios.Length; i++)
            {
                var (label, _, showRed, showYellow) = scenarios[i];
                var left = SignRegion(frames[i], width, 0, width / 3);
                var center = SignRegion(frames[i], width, width / 3, 2 * width / 3);
                var right = SignRegion(frames[i], width, 2 * width / 3, width);
                var counts = $"{label}: red={left.Red}/{left.Lit}, stock7={center.Lit}, yellow={right.Yellow}/{right.Lit}";
                Require(center.Lit >= 100 && (showRed ? left.Red >= 100 : left.Lit <= 3) &&
                    (showYellow ? right.Yellow >= 100 : right.Lit <= 3), $"{counts} (hue/lit pixels); see {evidence}");
                summary[i] = counts;
            }
            var stockCheck = CompareStockSignCell(source.mainTexture);
            return $"{evidence} row-major: {string.Join("; ", summary)}; {stockCheck}; shader={source.shader.name}";
        }

        private static (int Lit, int Red, int Yellow) SignRegion(Color32[] pixels, int width, int firstX, int endX)
        {
            int lit = 0, red = 0, yellow = 0;
            for (var row = 0; row < pixels.Length; row += width)
            for (var x = firstX; x < endX; x++)
            {
                var p = pixels[row + x];
                if (p.r > 24 || p.g > 24 || p.b > 24) lit++;
                if (p.r > 65 && 2 * p.g < p.r && 2 * p.b < p.r) red++;
                if (p.r > 65 && 5 * p.g > 3 * p.r && 2 * p.g > 3 * p.b && p.r > p.b + 30) yellow++;
            }
            return (lit, red, yellow);
        }

        private static void SaveSignFrames(Color32[][] frames, int width, int height, string path)
        {
            const int gap = 4;
            var sheet = new Texture2D(2 * width + gap, 2 * height + gap, TextureFormat.RGBA32, false);
            try
            {
                for (var i = 0; i < frames.Length; i++)
                    sheet.SetPixels32(i % 2 * (width + gap), (1 - i / 2) * (height + gap), width, height, frames[i]);
                sheet.Apply(false, false);
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(sheet));
            }
            finally { Object.Destroy(sheet); }
        }

        private static string CompareStockSignCell(Texture patched)
        {
            // This ResourceManager path maps to the untouched BC7 texture in the 0.10.35 installation.
            var stock = Resources.Load<Texture2D>("auxes/sign/sign-states");
            Require(stock != null && patched != null && stock != patched && stock.width == patched.width && stock.height == patched.height,
                "could not get an independent stock sign-states atlas reference");
            var before = ReadStockSignCell(stock);
            var after = ReadStockSignCell(patched);
            int compared = 0, changed = 0, opaque = 0;
            const int size = 256;
            for (var y = 8; y < size - 8; y += 5)
            for (var x = 8; x < size - 8; x += 5)
            {
                var a = before[y * size + x];
                var b = after[y * size + x];
                compared++;
                if (a.a > 128) opaque++;
                if (Math.Abs(a.a - b.a) > 10 || (a.a > 32 &&
                    (Math.Abs(a.r - b.r) > 10 || Math.Abs(a.g - b.g) > 10 || Math.Abs(a.b - b.b) > 10))) changed++;
            }
            Require(opaque >= 32 && changed * 100 <= compared, $"stock sign7 changed in GPU readback: {changed}/{compared}, opaque={opaque}");
            return $"stock sign7 GPU readback preserved, {compared - changed}/{compared} samples within tolerance";
        }

        private static Color32[] ReadStockSignCell(Texture atlas)
        {
            const int size = 256;
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            var scratch = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                GL.sRGBWrite = scratch.sRGB;
                Graphics.Blit(atlas, scratch, new Vector2(0.25f, 0.125f), new Vector2(0.75f, 0.125f));
                RenderTexture.active = scratch;
                readback.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                return readback.GetPixels32();
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(scratch);
                Object.Destroy(readback);
            }
        }
    }
}
