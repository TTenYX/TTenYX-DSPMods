using System;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.Youthcat;

namespace IconFix
{
    /// <summary>
    /// Shift+左键点击文本框中的纯图标（\tag;，TextTriggerBounds.type==2）时，
    /// 原地替换为带名称图标（\tag-;）。
    /// 定位策略：TextTriggerBounds.index 是点击命中字符位置，在其 ±4 字符窗口内找
    /// 该图标的纯码（如 \meik;）；带名称码（\meik-;）不包含纯码子串，不会被误匹配；
    /// 窗口未命中时回退全文最近位置。
    /// </summary>
    internal static class IconTextUpgrader
    {
        private static int _lastFrame = -1;

        /// <summary>
        /// 纯逻辑：在 text 中 hintIndex 附近找 plainTag，返回在分号前插入 '-' 后的新文本与光标位置。
        /// 找不到时返回 null。plainTag 由调用方传入（便于自测）。
        /// </summary>
        public static string UpgradeAt(string text, int hintIndex, string plainTag, out int caretPos)
        {
            caretPos = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(plainTag) || plainTag[plainTag.Length - 1] != ';')
            {
                return null;
            }

            var pos = FindNearest(text, plainTag, hintIndex);
            if (pos < 0)
            {
                return null;
            }

            var sb = new StringBuilder(text.Length + 1);
            sb.Append(text, 0, pos + plainTag.Length - 1);
            sb.Append('-');
            sb.Append(text, pos + plainTag.Length - 1, text.Length - (pos + plainTag.Length - 1));
            caretPos = pos + plainTag.Length + 1;
            return sb.ToString();
        }

        /// <summary>在 hint 附近（覆盖整个码长 ±4）优先匹配，失败时回退全文最近出现位置。</summary>
        private static int FindNearest(string text, string tag, int hint)
        {
            var windowFrom = Math.Max(0, hint - tag.Length - 4);
            var p = text.IndexOf(tag, windowFrom, StringComparison.Ordinal);
            if (p >= 0 && p <= hint + 4)
            {
                return p;
            }

            var best = -1;
            var from = 0;
            while ((p = text.IndexOf(tag, from, StringComparison.Ordinal)) >= 0)
            {
                if (best < 0 || Math.Abs(p - hint) < Math.Abs(best - hint))
                {
                    best = p;
                }
                from = p + 1;
            }
            return best;
        }

        private static void DebugLog(string msg)
        {
            if (IconFixPlugin.DebugLog != null && IconFixPlugin.DebugLog.Value)
            {
                IconFixPlugin.Log.LogInfo("[upgrade] " + msg);
            }
        }

        private static bool CanProcess(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (IconFixPlugin.ShiftClickUpgradeIconInText == null || !IconFixPlugin.ShiftClickUpgradeIconInText.Value)
            {
                return false;
            }
            if (e == null || e.button != PointerEventData.InputButton.Left)
            {
                DebugLog($"skip: button={e?.button} field={Describe(field)}");
                return false;
            }
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                DebugLog($"skip: no shift field={Describe(field)}");
                return false;
            }
            if (VFInput.control)
            {
                // Ctrl+Shift 同按时让原版「查看详情」行为独占，避免两个效果叠加
                DebugLog($"skip: ctrl held field={Describe(field)}");
                return false;
            }
            if (bounds.type != 2)
            {
                // ICON_TEXT_TYPE(3) 已带名称，无需升级
                DebugLog($"skip: bounds.type={bounds.type} iconId={bounds.iconId} index={bounds.index} field={Describe(field)}");
                return false;
            }
            return true;
        }

        private static string Describe(InputField field)
        {
            if (field == null)
            {
                return "<null>";
            }
            var t = field.GetType().Name;
            var path = field.gameObject.name;
            var p = field.transform.parent;
            for (var i = 0; p != null && i < 3; i++, p = p.parent)
            {
                path = p.name + "/" + path;
            }
            return $"{t}({path})";
        }

        /// <summary>普通输入框（UIInputField / UISmartInputField）的 Shift 升级处理。</summary>
        public static void Handle(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (field == null || !CanProcess(field, e, bounds))
            {
                return;
            }
            // 同帧只处理一次：UIInputField 的 inputFieldText 与 overText 两个 Text 都挂了同一回调
            if (_lastFrame == Time.frameCount)
            {
                return;
            }

            var plainTag = LDB.signals.IconTag((int)bounds.iconId, false);
            var newText = UpgradeAt(field.text, bounds.index, plainTag, out var caret);
            if (newText == null)
            {
                DebugLog($"miss: tag={plainTag} index={bounds.index} field={Describe(field)}");
                return;
            }

            field.text = newText;
            field.caretPosition = caret;
            field.selectionAnchorPosition = caret;
            field.selectionFocusPosition = caret;
            field.ForceLabelUpdate();
            DebugLog($"ok: field={Describe(field)} caret={caret}");
            _lastFrame = Time.frameCount;
        }

        /// <summary>UISmartInputField 富文本编辑器：替换后还需重算着色。</summary>
        public static void HandleSmart(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (field == null || !CanProcess(field, e, bounds) || _lastFrame == Time.frameCount)
            {
                return;
            }
            var plainTag = LDB.signals.IconTag((int)bounds.iconId, false);
            var newText = UpgradeAt(field.text, bounds.index, plainTag, out var caret);
            if (newText == null)
            {
                DebugLog($"miss: tag={plainTag} index={bounds.index} field={Describe(field)}");
                return;
            }

            field.text = newText;
            field.caretPosition = caret;
            field.selectionAnchorPosition = caret;
            field.selectionFocusPosition = caret;
            field.CalculateColorText();
            field.ForceLabelUpdate();
            DebugLog($"ok(smart): field={Describe(field)} caret={caret}");
            _lastFrame = Time.frameCount;
        }

        /// <summary>行星标记备注（UIMarkerDesc）：替换后同步写回标记存盘并刷新窗口布局。</summary>
        public static void HandleMarker(UIMarkerDesc marker, PointerEventData e, TextTriggerBounds bounds)
        {
            if (marker == null || marker.marker == null || marker.marker.id == 0 || !CanProcess(marker.todoInputField, e, bounds))
            {
                return;
            }
            if (_lastFrame == Time.frameCount)
            {
                return;
            }
            var field = marker.todoInputField;
            if (field == null)
            {
                return;
            }

            var plainTag = LDB.signals.IconTag((int)bounds.iconId, false);
            var newText = UpgradeAt(field.text, bounds.index, plainTag, out var caret);
            if (newText == null)
            {
                return;
            }

            field.text = newText;
            field.caretPosition = caret;
            field.selectionAnchorPosition = caret;
            field.selectionFocusPosition = caret;
            marker.factory?.WriteMarkerComment(marker.marker.id, field.text);
            try
            {
                var tr = Traverse.Create(marker);
                object[] args =
                {
                    tr.Property("advancedSettingsOpened").GetValue<bool>(),
                    0,
                    0,
                    true
                };
                AccessTools.Method(typeof(UIMarkerDesc), "AdaptiveWindowRect").Invoke(marker, args);
                tr.Method("HandleTodoInputFieldHeight").GetValue();
            }
            catch (Exception ex)
            {
                IconFixPlugin.Log.LogWarning($"marker layout refresh failed: {ex.Message}");
            }
            _lastFrame = Time.frameCount;
        }
    }
}
