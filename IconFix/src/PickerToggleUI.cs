using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IconFix
{
    /// <summary>
    /// 在「图标选取」面板头部（标题与原版「展示所有的图标」之间）注入一个勾选框，
    /// 克隆原版 displayUnlockBtn 以获得一致的外观与悬停效果。
    /// 点击切换 IconFixPlugin.IncludeName，并写回 BepInEx 配置持久化。
    /// </summary>
    internal static class PickerToggleUI
    {
        public const string ObjectName = "iconfix-include-name-toggle";
        private const float Gap = 10f;
        /// <summary>标签文本在按钮内的起点 x（checkbox 20 + 间隙 4）。</summary>
        private const float LabelStartX = 24f;

        private static UIButton _button;
        private static Image _checkImage;
        private static Text _label;

        public static void EnsureCreated(UISignalTagPicker picker)
        {
            if (picker == null || picker.displayUnlockBtn == null)
            {
                return;
            }
            if (picker.displayUnlockBtn.transform.parent == null)
            {
                return;
            }

            var parent = picker.displayUnlockBtn.transform.parent;
            var existing = parent.Find(ObjectName);
            if (existing != null)
            {
                // 重进游戏（域重载）后的旧对象：重新抓引用并修正布局
                _button = existing.GetComponent<UIButton>();
                _checkImage = FindCheckImage(picker, existing.gameObject);
                _label = existing.GetComponentInChildren<Text>(true);
                if (_label != null)
                {
                    _label.text = IconFixText.ToggleLabel;
                    FixLabelRect(_label);
                }
                RefreshVisual(picker);
                return;
            }

            CreateToggle(picker, parent);
            RefreshVisual(picker);
        }

        private static void CreateToggle(UISignalTagPicker picker, Transform parent)
        {
            var src = picker.displayUnlockBtn;
            var srcRT = (RectTransform)src.transform;

            var go = Object.Instantiate(src.gameObject, parent);
            go.name = ObjectName;

            // 克隆残留清理（见 MOD-UI克隆残留问题.md）：Localizer 会在启用时覆盖文本
            foreach (var loc in go.GetComponentsInChildren<Localizer>(true))
            {
                Object.Destroy(loc);
            }

            _button = go.GetComponent<UIButton>();
            if (_button != null)
            {
                // 克隆不带运行时事件，重新挂自己的点击处理
                _button.onClick += OnToggleClick;
                var tips = _button.tips;
                tips.tipTitle = IconFixText.ToggleTipTitle;
                tips.tipText = IconFixText.ToggleTipText;
                tips.type = UIButton.ItemTipType.Other;
                tips.delay = 0.2f;
                tips.topLevel = true;
                _button.tips = tips;
            }

            // 文本：优先复用克隆体内的 Text，否则新建
            _label = FindSingleLabel(go);
            if (_label == null)
            {
                var labelGo = new GameObject("label", typeof(Text));
                labelGo.transform.SetParent(go.transform, false);
                _label = labelGo.GetComponent<Text>();
                _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _label.fontSize = 14;
                _label.color = Color.white;
            }
            _label.text = IconFixText.ToggleLabel;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
            FixLabelRect(_label);

            _checkImage = FindCheckImage(picker, go);

            Layout(picker, srcRT, (RectTransform)go.transform);
        }

        /// <summary>
        /// 把标签 rect 锚到按钮内勾选框右侧：克隆自原版的 Text 是「右缘锚 + 宽 160」，
        /// 左对齐时文本会从按钮外左侧排进来压住勾选框；这里改为左缘锚、固定起点。
        /// </summary>
        private static void FixLabelRect(Text label)
        {
            var labelRT = (RectTransform)label.transform;
            labelRT.anchorMin = new Vector2(0f, 0.5f);
            labelRT.anchorMax = new Vector2(0f, 0.5f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.anchoredPosition = new Vector2(LabelStartX, 0f);
            labelRT.sizeDelta = new Vector2(EstimateTextWidth(label), 30f);
        }

        /// <summary>克隆体里只保留一个 Text 作为标签，其余销毁（防原版 label 残留）。</summary>
        private static Text FindSingleLabel(GameObject go)
        {
            var texts = go.GetComponentsInChildren<Text>(true);
            if (texts == null || texts.Length == 0)
            {
                return null;
            }
            var keep = texts[0];
            for (var i = 1; i < texts.Length; i++)
            {
                Object.Destroy(texts[i].gameObject);
            }
            return keep;
        }

        /// <summary>
        /// 在克隆体中定位「勾选框图标」的 Image：按原版 picker.checkbox 相对 displayUnlockBtn
        /// 的层级路径映射；找不到时回退到按钮自身 Image。
        /// </summary>
        private static Image FindCheckImage(UISignalTagPicker picker, GameObject clone)
        {
            var cb = picker.checkbox;
            var srcBtn = picker.displayUnlockBtn.transform;
            if (cb == null)
            {
                return clone.GetComponent<Image>();
            }
            if (cb.transform == srcBtn)
            {
                return clone.GetComponent<Image>();
            }

            var names = new List<string>();
            var t = cb.transform;
            while (t != null && t != srcBtn)
            {
                names.Add(t.name);
                t = t.parent;
            }
            if (t == srcBtn)
            {
                names.Reverse();
                var path = string.Join("/", names.ToArray());
                var found = clone.transform.Find(path);
                if (found != null)
                {
                    var img = found.GetComponent<Image>();
                    if (img != null)
                    {
                        return img;
                    }
                }
            }
            return clone.GetComponent<Image>();
        }

        /// <summary>
        /// 放到原版「展示所有的图标」按钮左侧同一行：复制其锚点/轴心/高度，
        /// 宽度按自身内容（勾选框 + 标签）计算。
        /// 克隆体的 pivot 与原按钮相同，因此按「右缘对齐到原按钮左缘 − 间距」定位。
        /// </summary>
        private static void Layout(UISignalTagPicker picker, RectTransform srcRT, RectTransform myRT)
        {
            myRT.anchorMin = srcRT.anchorMin;
            myRT.anchorMax = srcRT.anchorMax;
            myRT.pivot = srcRT.pivot;
            myRT.localScale = srcRT.localScale;

            var height = srcRT.rect.height > 0f ? srcRT.rect.height : srcRT.sizeDelta.y;
            var labelWidth = 0f;
            if (_label != null)
            {
                labelWidth = EstimateTextWidth(_label);
            }
            var selfWidth = Mathf.Max(48f, LabelStartX + labelWidth + 10f);

            // 原按钮左缘（局部 x）= anchoredPosition.x − width × pivot.x；右缘锚（pivot.x=1）时即 pos.x − width
            var srcLeft = srcRT.anchoredPosition.x - srcRT.rect.width * srcRT.pivot.x;
            myRT.sizeDelta = new Vector2(selfWidth, height);
            myRT.anchoredPosition = new Vector2(srcLeft - Gap, srcRT.anchoredPosition.y);
        }

        /// <summary>标签宽度估算：CJK 字符按 1.0 字号宽、其余按 0.62，加少量余量。</summary>
        private static float EstimateTextWidth(Text label)
        {
            if (label == null || string.IsNullOrEmpty(label.text))
            {
                return 0f;
            }
            var width = 0f;
            foreach (var ch in label.text)
            {
                width += ch >= 0x2E80 ? label.fontSize : label.fontSize * 0.62f;
            }
            return width + 4f;
        }

        private static void OnToggleClick(int obj)
        {
            IconFixPlugin.IncludeName.Value = !IconFixPlugin.IncludeName.Value;
            IconFixPlugin.Instance.Config.Save();
            RefreshVisual(CurrentPicker());
        }

        private static UISignalTagPicker CurrentPicker()
        {
            var root = UIRoot.instance;
            var game = root != null ? root.uiGame : null;
            return game != null ? game.signalTagPicker : null;
        }

        /// <summary>按当前开关状态刷新勾选框图标与标签文本（语言切换后也会走到）。</summary>
        public static void RefreshVisual(UISignalTagPicker picker)
        {
            var on = IconFixPlugin.IncludeName.Value;
            if (_checkImage != null && picker != null)
            {
                _checkImage.sprite = on ? picker.checkboxOn : picker.checkboxOff;
            }
            if (_label != null)
            {
                _label.text = IconFixText.ToggleLabel;
            }
        }
    }
}
