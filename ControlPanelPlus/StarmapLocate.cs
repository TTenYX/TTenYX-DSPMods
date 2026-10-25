using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ControlPanelPlus
{
    /// <summary>
    /// F2: a "locate on starmap" button left of every planet entry's navigation button.
    /// The click flow mirrors the native OnAstro2AstroSettingButtonClick
    /// (UIControlPanelStationInspector.cs 1833-1883) and additionally selects the planet in the starmap.
    /// It intentionally does not toggle the navigation line (fixed in round 2).
    /// </summary>
    public static class StarmapLocate
    {
        public const string ButtonName = "cpt-locate-btn";

        private static Sprite _viewPlanetIcon;
        private static Sprite _cleanIcon;

        /// <summary>
        /// The starmap "view planet" sprite ships with a glowing halo. Take only its solid glyph
        /// (threshold the alpha) so the button shows a crisp icon without the glow; when the texture
        /// cannot be read, draw a clean magnifier instead.
        /// </summary>
        /// <summary>Icon shown on the button (cleaned once).</summary>
        public static Sprite LocateIcon => CleanIcon(ViewPlanetIcon);

        private static Sprite CleanIcon(Sprite source)
        {
            if (_cleanIcon != null) return _cleanIcon;
            if (source != null)
            {
                try
                {
                    Texture2D texture = source.texture;
                    if (texture != null && texture.isReadable)
                    {
                        Rect r = source.rect;
                        int w = Mathf.RoundToInt(r.width);
                        int h = Mathf.RoundToInt(r.height);
                        if (w > 0 && h > 0)
                        {
                            Color[] pixels = texture.GetPixels((int)r.x, (int)r.y, w, h);
                            for (int i = 0; i < pixels.Length; i++)
                            {
                                pixels[i].a = pixels[i].a >= 0.72f ? 1f : 0f;
                            }
                            Texture2D clean = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            clean.filterMode = FilterMode.Bilinear;
                            clean.wrapMode = TextureWrapMode.Clamp;
                            clean.SetPixels(pixels);
                            clean.Apply();
                            _cleanIcon = Sprite.Create(clean, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
                            _cleanIcon.name = "cpt-locate-clean";
                            return _cleanIcon;
                        }
                    }
                }
                catch (Exception e)
                {
                    CppLog.Debug("clean icon from sprite failed: " + e.Message);
                }
            }
            _cleanIcon = CreateMagnifierIcon();
            return _cleanIcon;
        }

        private static Sprite CreateMagnifierIcon()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float px = x + (sx + 0.5f) * 0.5f;
                            float py = y + (sy + 0.5f) * 0.5f;
                            float ring = Mathf.Sqrt((px - 25f) * (px - 25f) + (py - 39f) * (py - 39f));
                            if (ring >= 11.5f && ring <= 15.5f) alpha += 0.25f;
                            // small "planet" dot inside the lens
                            float dot = Mathf.Sqrt((px - 25f) * (px - 25f) + (py - 39f) * (py - 39f));
                            if (dot <= 4.5f) alpha += 0.25f;
                            if (DistanceToSegment(px, py, 35.5f, 28.5f, 49f, 15f) <= 3.6f) alpha += 0.25f;
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "cpt-locate-magnifier";
            return sprite;
        }

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax;
            float vy = by - ay;
            float wx = px - ax;
            float wy = py - ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 > 0f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
            float dx = wx - vx * t;
            float dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Localized tooltip of the locate button (also exercised by the self test).</summary>
        public static void GetTooltip(out string title, out string text)
        {
            title = Lang.T("定位", "Locate");
            text = Lang.Chinese
                ? "左键：打开星图并保留总控面板\n右键：打开星图并关闭总控面板\nShift+右键：关闭总控面板并点亮导航线"
                : "Left click: open the star map and keep the panel\nRight click: open the star map and close the panel\nShift+right click: close the panel and point the navigation line";
        }

        /// <summary>Same star map "view planet" sprite PlanetFinder uses for its button.</summary>
        private static Sprite ViewPlanetIcon
        {
            get
            {
                if (_viewPlanetIcon != null) return _viewPlanetIcon;
                UIStarmap starmap = UIRoot.instance != null && UIRoot.instance.uiGame != null ? UIRoot.instance.uiGame.starmap : null;
                if (starmap != null && starmap.cursorFunctionButton2 != null)
                {
                    Transform icon = starmap.cursorFunctionButton2.transform.Find("icon");
                    Image image = icon != null ? icon.GetComponent<Image>() : null;
                    if (image != null) _viewPlanetIcon = image.sprite;
                }
                return _viewPlanetIcon;
            }
        }

        public static void EnsureButton(UIControlPanelPlanetEntry entry)
        {
            if (entry == null || entry.navigationButton == null) return;
            Transform banner = entry.navigationButton.transform.parent;
            if (banner == null || banner.Find(ButtonName) != null) return;

            Sprite icon = LocateIcon;
            UIButton button = icon != null ? NativeUi.MakeIconButton(ButtonName, icon, 22f) : null;
            bool iconStyle = button != null;
            if (button == null) button = NativeUi.MakeSmallTextButton(Lang.T("定位", "Locate"), new Vector2(36f, 22f), 12, ButtonName);
            if (button == null) return;

            RectTransform naviRect = (RectTransform)entry.navigationButton.transform;
            RectTransform rect = (RectTransform)button.transform;
            rect.SetParent(banner, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(naviRect.anchoredPosition.x - 6f, 0f);
            if (iconStyle)
            {
                string tipTitle;
                string tipText;
                GetTooltip(out tipTitle, out tipText);
                button.tips.tipTitle = tipTitle;
                button.tips.tipText = tipText;
                button.tips.corner = 3;
                button.tips.offset = new Vector2(18f, -20f);
                button.tips.delay = 0.7f;
                // fixed three lines: widen the tip so neither language wraps
                button.tips.width = NativeUi.MeasureTipWidth(tipText, 16);
                NativeUi.MakeIconButtonTransparent(button);
            }
            else
            {
                rect.sizeDelta = new Vector2(36f, 22f);
                rect.localScale = Vector3.one;
            }

            LocateButtonHandler handler = button.gameObject.AddComponent<LocateButtonHandler>();
            handler.entry = entry;
            button.onClick += handler.OnButtonClick;
            button.onRightClick += handler.OnButtonRightClick;

            CppLog.Debug("locate button injected into planet entry banner");
        }

        public static void RefreshVisibility(UIControlPanelPlanetEntry entry)
        {
            if (entry == null) return;
            Transform t = entry.navigationButton != null ? entry.navigationButton.transform.parent : null;
            if (t == null) return;
            Transform button = t.Find(ButtonName);
            if (button == null) return;
            bool visible = Cfg.EnableStarmapButton.Value && entry.isTargetDataValid;
            if (button.gameObject.activeSelf != visible)
            {
                button.gameObject.SetActive(visible);
            }
        }

        public static void Locate(UIControlPanelPlanetEntry entry, bool closePanel, bool linkNavigation)
        {
            if (entry == null || !entry.isTargetDataValid) return;
            if (!Cfg.Enabled.Value || !Cfg.EnableStarmapButton.Value) return;

            PlanetData planet = entry.planet;
            try
            {
                UIGame uiGame = UIRoot.instance.uiGame;
                UIStarmap starmap = uiGame.starmap;
                if (starmap == null) return;

                Player mainPlayer = GameMain.mainPlayer;
                mainPlayer.combatState = false;
                uiGame.ShutPlayerInventory();
                if (closePanel)
                {
                    // right click: close the control panel like the vanilla flow
                    uiGame.ShutAllFunctionWindow();
                }
                // Always clear the other full screen editors. DashboardOverhaul can pin its window,
                // so a second direct close is attempted below when the first one did not stick.
                uiGame.ShutTechTree();
                uiGame.ShutDashboard();
                uiGame.ShutDysonEditorImmediately();
                uiGame.CloseEnemyBriefInfo();
                if (uiGame.dashboard != null && uiGame.dashboard.active)
                {
                    CppLog.Warn("dashboard is still active after ShutDashboard; forcing it closed");
                    try
                    {
                        uiGame.dashboard._Close();
                    }
                    catch (Exception e)
                    {
                        CppLog.Error("forced dashboard close failed: " + e);
                    }
                }

                if (!starmap.active)
                {
                    uiGame.BeforeEnterFullScreenUI();
                    starmap.FadeIn();
                }

                starmap.SetViewStar(planet.star);
                starmap.focusPlanet = null;
                starmap.focusStar = null;
                starmap.focusHive = null;
                starmap.focusEnemyId = 0;

                double radius = planet.realRadius * 0.00025 * 6.0;
                starmap.screenCameraController.SetViewTarget(planet, null, null, null, VectorLF3.zero, radius, radius,
                    lockRot: true, immediate: false);

                // Left click never touches the nav line; Shift+right click points it at the planet.
                if (linkNavigation && mainPlayer.navigation != null)
                {
                    mainPlayer.navigation.indicatorAstroId = planet.astroId;
                }

                UIStarmapPlanet uiPlanet = FindPlanetUi(starmap, planet);
                if (uiPlanet != null)
                {
                    starmap.focusPlanet = uiPlanet;
                    starmap.focusStar = null;
                    starmap.focusHive = null;
                    starmap.focusEnemyId = 0;
                    starmap.OnFocusTargetChange();
                }

                CppLog.Debug($"locate planet {planet.displayName} (astroId={planet.astroId}) selected={uiPlanet != null}");
            }
            catch (Exception e)
            {
                CppLog.Error("starmap locate failed: " + e);
            }
        }

        private static UIStarmapPlanet FindPlanetUi(UIStarmap starmap, PlanetData planet)
        {
            if (starmap.planetUIs == null) return null;
            for (int i = 0; i < starmap.planetUIs.Length; i++)
            {
                UIStarmapPlanet uiPlanet = starmap.planetUIs[i];
                if (uiPlanet != null && uiPlanet.planet == planet)
                {
                    return uiPlanet;
                }
            }
            return null;
        }
    }

    internal class LocateButtonHandler : MonoBehaviour
    {
        public UIControlPanelPlanetEntry entry;

        public void OnButtonClick(int obj)
        {
            StarmapLocate.Locate(entry, false, false);
        }

        public void OnButtonRightClick(int obj)
        {
            // Shift+right click closes the panel and points the navigation line at the planet.
            bool shift = VFInput.shift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            StarmapLocate.Locate(entry, true, shift);
        }
    }

    [HarmonyPatch(typeof(UIControlPanelPlanetEntry))]
    internal static class PlanetEntryPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("_OnRegEvent")]
        public static void OnRegEvent(UIControlPanelPlanetEntry __instance)
        {
            if (!Cfg.Enabled.Value || !Cfg.EnableStarmapButton.Value) return;
            try
            {
                StarmapLocate.EnsureButton(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("planet entry button injection failed: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnUpdate")]
        public static void OnUpdate(UIControlPanelPlanetEntry __instance)
        {
            if (!Cfg.Enabled.Value) return;
            try
            {
                StarmapLocate.RefreshVisibility(__instance);
            }
            catch (Exception e)
            {
                CppLog.Error("planet entry button refresh failed: " + e);
            }
        }
    }
}
