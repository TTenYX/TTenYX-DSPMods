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

            Sprite icon = ViewPlanetIcon;
            UIButton button = icon != null ? NativeUi.MakeIconButton(ButtonName, icon, 22f) : null;
            bool iconStyle = button != null;
            if (button == null) button = NativeUi.MakeSmallTextButton("定位", new Vector2(36f, 22f), 12, ButtonName);
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
                button.tips.tipTitle = "定位 / Locate";
                button.tips.tipText = "左键：打开星图并保留总控面板\n右键：打开星图并关闭总控面板\nShift+右键：关闭总控面板并点亮导航线";
                button.tips.corner = 3;
                button.tips.offset = new Vector2(18f, -20f);
                button.tips.delay = 0.7f;
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
                else
                {
                    // left click: keep the control panel open, only close the full screen editors
                    uiGame.ShutTechTree();
                    uiGame.ShutDashboard();
                    uiGame.ShutDysonEditorImmediately();
                }
                uiGame.CloseEnemyBriefInfo();

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
