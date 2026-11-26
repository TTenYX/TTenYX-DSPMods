using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SmartWarningIcons
{
    internal static class SignIcons
    {
        internal const uint SignTypeInsufficientInput = 31;
        internal const uint SignTypeOutputPile = 25;
        internal const int SignalIdInsufficientInput = 500 + (int)SignTypeInsufficientInput;
        internal const int SignalIdOutputPile = 500 + (int)SignTypeOutputPile;

        private const int AtlasColumns = 4;
        private const int AtlasRows = 8;
        private const int AtlasCell = 256;
        private const int IconCell = IconSet.kIconSize;

        private static Texture2D _redCell, _yellowCell, _redIcon, _yellowIcon;
        private static Sprite _redSprite, _yellowSprite;
        private static bool _assetsAttempted, _assetsReady;
        private static Material _patchedMaterial;
        private static Texture _sourceAtlas, _failedAtlas;
        private static Texture2D _patchedAtlas;

        private static bool EnsureAssets()
        {
            if (_assetsAttempted)
                return _assetsReady;
            _assetsAttempted = true;
            try
            {
                _redCell = LoadTexture("InsufficientInput.png", AtlasCell);
                _yellowCell = LoadTexture("OutputPile.png", AtlasCell);
                _redIcon = LoadTexture("InsufficientInputSmall.png", IconCell);
                _yellowIcon = LoadTexture("OutputPileSmall.png", IconCell);
                var rect = new Rect(0, 0, IconCell, IconCell);
                var pivot = new Vector2(0.5f, 0.5f);
                _redSprite = Sprite.Create(_redIcon, rect, pivot);
                _yellowSprite = Sprite.Create(_yellowIcon, rect, pivot);
                return _assetsReady = true;
            }
            catch (Exception ex)
            {
                ReleaseAssets();
                SmartWarningIconsPlugin.Log?.LogWarning($"Custom warning icons unavailable; retaining native signs: {ex.Message}");
                return false;
            }
        }

        private static Texture2D LoadTexture(string name, int size)
        {
            var bytes = File.ReadAllBytes(ResolveAssetPath(name));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes) || texture.width != size || texture.height != size)
                    throw new InvalidDataException($"{name} must be a {size}x{size} PNG.");
                texture.name = $"SmartWarningIcons/{name}";
                return texture;
            }
            catch
            {
                Object.Destroy(texture);
                throw;
            }
        }

        private static string ResolveAssetPath(string name)
        {
            var root = SmartWarningIconsPlugin.PluginDir;
            // 常规安装图标在 assets/ 子目录；个别管理器或手动解压会把包内文件平铺到插件根目录，两处都找。
            var nested = Path.Combine(root, "assets", name);
            if (File.Exists(nested))
                return nested;
            var flat = Path.Combine(root, name);
            if (File.Exists(flat))
                return flat;
            throw new FileNotFoundException($"Icon asset not found under assets/ or the plugin folder: {name}");
        }

        /// <summary>恢复被换回的图集；正常帧只比较当前材质的纹理引用。</summary>
        internal static bool HealAtlas()
        {
            var material = Configs.builtin != null ? Configs.builtin.entitySignMat : null;
            var atlas = material != null ? material.mainTexture : null;
            if (atlas == null || atlas == _failedAtlas)
                return false;
            if (atlas == _patchedAtlas)
                return true;
            if (!EnsureAssets())
                return false;

            if (atlas == _sourceAtlas && _patchedAtlas != null && material == _patchedMaterial)
            {
                material.mainTexture = _patchedAtlas;
                return true;
            }

            Texture2D readable = null;
            RenderTexture temporary = null;
            var previous = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            try
            {
                if (atlas.width != AtlasColumns * AtlasCell || atlas.height != AtlasRows * AtlasCell)
                    throw new InvalidDataException($"Unsupported sign atlas size: {atlas.width}x{atlas.height}.");

                readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, atlas.mipmapCount > 1)
                {
                    name = "SmartWarningIcons/sign-states",
                    filterMode = atlas.filterMode,
                    wrapModeU = atlas.wrapModeU,
                    wrapModeV = atlas.wrapModeV,
                    wrapModeW = atlas.wrapModeW,
                    anisoLevel = atlas.anisoLevel,
                    mipMapBias = atlas.mipMapBias
                };
                temporary = RenderTexture.GetTemporary(atlas.width, atlas.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = temporary.sRGB;
                Graphics.Blit(atlas, temporary);
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, false);
                WriteSignCell(readable, SignTypeInsufficientInput, _redCell);
                WriteSignCell(readable, SignTypeOutputPile, _yellowCell);
                readable.Apply();

                ReleaseAtlas();
                material.mainTexture = readable;
                _patchedMaterial = material;
                _sourceAtlas = atlas;
                _patchedAtlas = readable;
                _failedAtlas = null;
                readable = null;
                SmartWarningIconsPlugin.Log?.LogInfo($"Registered world signs {SignTypeInsufficientInput}/{SignTypeOutputPile} (4-column atlas).");
                return true;
            }
            catch (Exception ex)
            {
                _failedAtlas = atlas;
                SmartWarningIconsPlugin.Log?.LogWarning($"Could not patch sign atlas; retaining native signs: {ex.Message}");
                return false;
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) Object.Destroy(readable);
            }
        }

        private static void WriteSignCell(Texture2D target, uint signType, Texture2D source)
        {
            // 实装 Entity Sign Batch 着色器：U 随 signType 横向循环，V 每四槽换行。
            var x = (int)(signType % AtlasColumns) * AtlasCell;
            var y = (int)(signType / AtlasColumns) * AtlasCell;
            target.SetPixels(x, y, AtlasCell, AtlasCell, source.GetPixels());
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Configs), nameof(Configs.Awake))]
        private static void OnConfigsAwake() => HealAtlas();

        // 原版 Create 在 loaded=true 时会早退；后缀也必须如此，不能重复消费两个格子。
        [HarmonyPrefix]
        [HarmonyPatch(typeof(IconSet), nameof(IconSet.Create))]
        private static void OnIconSetCreating(IconSet __instance, out bool __state) => __state = !__instance.loaded;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(IconSet), nameof(IconSet.Create))]
        private static void OnIconSetCreate(IconSet __instance, bool __state)
        {
            if (!__state || !EnsureAssets())
                return;

            var nextId = 1;
            foreach (var used in __instance.spriteIndexMap.Values)
                if (used >= nextId)
                    nextId = (int)used + 1;

            const int cells = IconSet.kIconCountPerAxis * IconSet.kIconCountPerAxis;
            if (nextId + 2 > cells || __instance.signalIconIndex.Length <= SignalIdInsufficientInput)
            {
                SmartWarningIconsPlugin.Log?.LogWarning("IconSet has no room for two custom warning icons.");
                return;
            }

            // 与原版已经上传的 CPU 图集保持一致；不依赖跨纹理格式的 CopyTexture。
            RegisterIcon(__instance, _yellowIcon, _yellowSprite, SignalIdOutputPile, nextId);
            RegisterIcon(__instance, _redIcon, _redSprite, SignalIdInsufficientInput, nextId + 1);
            __instance.texture.Apply(true);
            __instance.signalIconIndexBuffer.SetData(__instance.signalIconIndex);
        }

        private static void RegisterIcon(IconSet set, Texture2D texture, Sprite sprite, int signalId, int id)
        {
            var x = id % IconSet.kIconCountPerAxis * IconCell;
            var y = id / IconSet.kIconCountPerAxis * IconCell;
            set.texture.SetPixels(x, y, IconCell, IconCell, texture.GetPixels());
            set.spriteIndexMap[sprite] = (uint)id;
            set.signalIconIndex[signalId] = (uint)id;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SignalProtoSet), nameof(SignalProtoSet.IconSprite))]
        private static void OnSignalIconSprite(int signalId, ref Sprite __result)
        {
            if (!_assetsReady || __result != null)
                return;
            if (signalId == SignalIdInsufficientInput)
                __result = _redSprite;
            else if (signalId == SignalIdOutputPile)
                __result = _yellowSprite;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), nameof(UIOptionWindow._OnCreate))]
        private static void OnWarnMaskCreated(UIOptionWindow __instance)
        {
            var buttons = __instance.buildingWarnButtons;
            if (buttons == null || buttons.Length == 0 || !EnsureAssets())
                return;

            var template = buttons[0];
            var right = ((RectTransform)template.transform).anchoredPosition.x;
            foreach (var button in buttons)
            {
                var x = ((RectTransform)button.transform).anchoredPosition.x;
                if (x > right)
                {
                    template = button;
                    right = x;
                }
            }

            var size = ((RectTransform)template.transform).sizeDelta;
            var yellow = AddWarnButton(template, right + size.x, _yellowSprite, SignTypeOutputPile);
            var red = AddWarnButton(template, right + size.x * 2, _redSprite, SignTypeInsufficientInput);
            var extended = new UIButton[buttons.Length + 2];
            Array.Copy(buttons, extended, buttons.Length);
            extended[buttons.Length] = yellow;
            extended[buttons.Length + 1] = red;
            __instance.buildingWarnButtons = extended;
            UpdateWarnTips(__instance);
        }

        private static UIButton AddWarnButton(UIButton template, float x, Sprite sprite, uint signType)
        {
            var added = Object.Instantiate(template, template.transform.parent);
            added.name = $"smart-warning-icon-{signType}";
            var rect = (RectTransform)added.transform;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            var image = added.transform.Find("image")?.GetComponent<Image>();
            if (image != null)
                image.sprite = sprite;
            added.data = 1 << ((int)signType - 1);
            return added;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), nameof(UIOptionWindow.OnLanguageChange))]
        private static void UpdateWarnTips(UIOptionWindow __instance)
        {
            var buttons = __instance.buildingWarnButtons;
            if (!_assetsReady || buttons == null)
                return;
            var chinese = Localization.isZHCN;
            foreach (var button in buttons)
            {
                switch (button.name)
                {
                    case "smart-warning-icon-25":
                        button.tips.tipTitle = chinese ? "产物堆砌" : "Output Piled Up";
                        button.tips.tipText = chinese ? "建筑因产物缓冲区满而停工时，在其上方显示警告图标。" :
                            "Show the warning above a building that has stopped because its output buffer is full.";
                        break;
                    case "smart-warning-icon-31":
                        button.tips.tipTitle = chinese ? "缺原料" : "Insufficient Input";
                        button.tips.tipText = chinese ? "显示建筑上方的缺原料警告图标。" : "Show the insufficient-input sign above buildings.";
                        break;
                }
            }
        }

        internal static void Dispose()
        {
            ReleaseAtlas();
            ReleaseAssets();
            _assetsAttempted = false;
            _failedAtlas = null;
        }

        private static void ReleaseAtlas()
        {
            if (_patchedMaterial != null && _patchedMaterial.mainTexture == _patchedAtlas)
                _patchedMaterial.mainTexture = _sourceAtlas;
            if (_patchedAtlas != null)
                Object.Destroy(_patchedAtlas);
            _patchedMaterial = null;
            _sourceAtlas = null;
            _patchedAtlas = null;
        }

        private static void ReleaseAssets()
        {
            Object.Destroy(_redSprite);
            Object.Destroy(_yellowSprite);
            Object.Destroy(_redCell);
            Object.Destroy(_yellowCell);
            Object.Destroy(_redIcon);
            Object.Destroy(_yellowIcon);
            _redSprite = _yellowSprite = null;
            _redCell = _yellowCell = _redIcon = _yellowIcon = null;
            _assetsReady = false;
        }
    }
}
