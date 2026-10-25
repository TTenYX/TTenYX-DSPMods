using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ComboBox = UnityEngine.UI.Youthcat.UIComboBox;

namespace ControlPanelPlus
{
    /// <summary>
    /// Small helpers that build UI out of native prefabs, following the rules in
    /// MOD-UI克隆残留问题.md (strip Localizer, set layout explicitly, clear text/tips).
    /// </summary>
    internal static class NativeUi
    {
        public static Font DinFont()
        {
            UIControlPanelWindow w = CurrentWindow();
            return w != null ? w.FONT_DIN : null;
        }

        public static UIControlPanelWindow CurrentWindow()
        {
            UIGame game = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            return game != null ? game.controlPanelWindow : null;
        }

        /// <summary>
        /// Clones the assembler window's small "copy" text button. The clone is returned inactive-parented
        /// to nothing; callers must parent it and set its rect explicitly.
        /// </summary>
        public static UIButton MakeSmallTextButton(string label, Vector2 size, int fontSize, string name)
        {
            UIGame game = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIButton src = game != null && game.assemblerWindow != null ? game.assemblerWindow.copyButton : null;
            if (src == null)
            {
                CppLog.Warn("native button template (assemblerWindow.copyButton) is not available");
                return null;
            }

            GameObject go = Object.Instantiate(src.gameObject);
            go.name = name;

            UIButton btn = go.GetComponent<UIButton>();
            Transform textTrans = go.transform.Find("Text");
            if (textTrans != null)
            {
                // The clone carries a Localizer that would overwrite our text with a translation key.
                Localizer loc = textTrans.GetComponent<Localizer>();
                if (loc != null) Object.DestroyImmediate(loc);

                Text txt = textTrans.GetComponent<Text>();
                if (txt != null)
                {
                    txt.text = label;
                    txt.fontSize = fontSize;
                    txt.supportRichText = true;
                    txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    txt.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }

            if (btn != null)
            {
                btn.tips.tipTitle = "";
                btn.tips.tipText = "";
            }

            go.SetActive(true);
            RectTransform rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            rt.sizeDelta = size;
            return btn;
        }

        /// <summary>
        /// Clones the research queue's pause icon button - the same template PlanetFinder uses for its
        /// "View Planet" button - and scales it to the requested visual size.
        /// </summary>
        public static UIButton MakeIconButton(string name, Sprite sprite, float size)
        {
            UIGame game = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIButton src = game != null && game.researchQueue != null ? game.researchQueue.pauseButton : null;
            if (src == null)
            {
                CppLog.Warn("native icon button template (researchQueue.pauseButton) is not available");
                return null;
            }

            GameObject go = Object.Instantiate(src.gameObject);
            go.name = name;
            UIButton btn = go.GetComponent<UIButton>();
            if (btn == null)
            {
                Object.Destroy(go);
                return null;
            }

            RectTransform rt = (RectTransform)go.transform;
            float sourceSize = rt.rect.width > 1f ? rt.rect.width : Mathf.Max(1f, rt.sizeDelta.x);
            float scale = size / sourceSize;
            rt.localScale = new Vector3(scale, scale, scale);

            if (sprite != null)
            {
                Transform icon = go.transform.Find("icon");
                Image image = icon != null ? icon.GetComponent<Image>() : null;
                if (image != null) image.sprite = sprite;
            }

            btn.tips.tipTitle = "";
            btn.tips.tipText = "";
            btn.tips.delay = 0.7f;
            go.SetActive(true);
            return btn;
        }

        /// <summary>Anchors a rect to the top-right corner of its parent; y is negative going down.</summary>
        public static void AnchorTopRight(RectTransform rt, float rightOffset, float topOffset, Vector2 size)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(rightOffset, topOffset);
            rt.sizeDelta = size;
        }

        /// <summary>The prefab of the small text button has no font of its own; keep whatever it carries.</summary>
        public static Text TextOf(UIButton btn)
        {
            if (btn == null) return null;
            Transform t = btn.transform.Find("Text");
            return t != null ? t.GetComponent<Text>() : null;
        }

        public static bool IsInputFocused(UIControlPanelWindow w)
        {
            EventSystem es = EventSystem.current;
            if (es != null)
            {
                GameObject go = es.currentSelectedGameObject;
                if (go != null)
                {
                    InputField field = go.GetComponent<InputField>();
                    if (field != null && field.isFocused) return true;
                    UIInputField uiField = go.GetComponent<UIInputField>();
                    if (uiField != null && uiField.inputField != null && uiField.inputField.isFocused) return true;
                }
            }

            UIControlPanelFilterPanel fp = w != null ? w.filterPanel : null;
            if (fp != null)
            {
                if (fp.astroFilterBox != null && fp.astroFilterBox.searchInput != null && fp.astroFilterBox.searchInput.isFocused) return true;
                if (fp.tagFilterInput != null && fp.tagFilterInput.isFocused) return true;
                if (fp.searchFilterInput != null && fp.searchFilterInput.isFocused) return true;
            }

            UIControlPanelStationInspector inspector = w != null ? w.currentInspector as UIControlPanelStationInspector : null;
            if (inspector != null && inspector.nameInput != null && inspector.nameInput.isFocused) return true;

            return false;
        }

        /// <summary>True when the mouse pointer is over a logistic storage slot (panel inspector or
        /// the station window). UXAssist's "logistic storage capacity" tweak uses the arrow keys there,
        /// so the panel's arrow-key selection must step aside.</summary>
        public static bool MouseOverStorageSlot()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            PointerEventData pointer = new PointerEventData(eventSystem);
            pointer.position = Input.mousePosition;
            List<RaycastResult> hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            for (int i = 0; i < hits.Count; i++)
            {
                GameObject go = hits[i].gameObject;
                if (go == null) continue;
                if (go.GetComponentInParent<UIControlPanelStationStorage>() != null) return true;
                if (go.GetComponentInParent<UIStationStorage>() != null) return true;
            }
            return false;
        }

        /// <summary>
        /// Enables rich text on a Youthcat combo box (the control panel version, not the StatsUITweaks one).
        /// </summary>
        public static void EnableRichText(ComboBox box)
        {
            if (box == null) return;
            if (box.mainLabel != null) box.mainLabel.supportRichText = true;
            if (box.emptyText != null) box.emptyText.supportRichText = true;
            if (box.entryPrefab != null && box.entryPrefab.text != null) box.entryPrefab.text.supportRichText = true;
        }
    }
}
