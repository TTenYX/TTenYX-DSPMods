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
    /// Shift+左键点击文本框中的纯图标（\tag;），原地替换为带名称图标（\tag-;）。
    ///
    /// 定位：实测 TextTriggerBounds 的 index/iconId 不可靠（恒为 0），改用
    /// InputField.caretPosition——游戏在 PointerDown 阶段就把光标移到点击字符处，
    /// 我们在 Click 阶段执行时它已是本次点击位置。
    /// 算法：从光标处（及前一位兜底）向右找分号，回溯 [字母数字-] 段并验证段前是
    /// 反斜杠，要求点击落在该码范围内；段内已含 '-'（带名称码）则跳过。
    /// </summary>
    internal static class IconTextUpgrader
    {
        public enum UpgradeResult
        {
            NotFound,
            AlreadyNamed,
            Replaced
        }

        private static int _lastFrame = -1;

        /// <summary>纯逻辑：按光标位置定位纯图标码并在分号前插 '-'。返回 null 表示未替换。</summary>
        public static string UpgradeAtCaret(string text, int caret, out int newCaret, out UpgradeResult result)
        {
            newCaret = -1;
            result = UpgradeResult.NotFound;
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            if (caret < 0)
            {
                caret = 0;
            }
            if (caret > text.Length)
            {
                caret = text.Length;
            }

            for (var k = 0; k < 2; k++)
            {
                var anchor = caret - k;
                if (anchor < 0 || anchor >= text.Length)
                {
                    continue;
                }

                // 向右找第一个分号
                var semi = -1;
                var limit = Math.Min(text.Length, anchor + 32);
                for (var i = anchor; i < limit; i++)
                {
                    if (text[i] == ';')
                    {
                        semi = i;
                        break;
                    }
                }
                if (semi < 0)
                {
                    continue;
                }

                // 回溯 tag 段 [start+1 .. semi)
                var j = semi - 1;
                var hasDash = false;
                while (j >= 0)
                {
                    var c = text[j];
                    if (c == '-')
                    {
                        hasDash = true;
                        j--;
                        continue;
                    }
                    if (char.IsLetterOrDigit(c))
                    {
                        j--;
                        continue;
                    }
                    break;
                }
                if (j < 0 || text[j] != '\\')
                {
                    continue;
                }
                var start = j;
                // 点击必须落在该码范围内（anchor ∈ [start, semi]）
                if (start > anchor || semi < anchor)
                {
                    continue;
                }
                if (hasDash)
                {
                    result = UpgradeResult.AlreadyNamed;
                    return null;
                }

                var sb = new StringBuilder(text.Length + 1);
                sb.Append(text, 0, semi);
                sb.Append('-');
                sb.Append(text, semi, text.Length - semi);
                newCaret = semi + 2;
                result = UpgradeResult.Replaced;
                return sb.ToString();
            }
            return null;
        }

        private static void DebugLog(string msg)
        {
            if (IconFixPlugin.DebugLog != null && IconFixPlugin.DebugLog.Value)
            {
                IconFixPlugin.Log.LogInfo("[upgrade] " + msg);
            }
        }

        private static string Describe(InputField field)
        {
            if (field == null)
            {
                return "<null>";
            }
            var path = field.gameObject.name;
            var p = field.transform.parent;
            for (var i = 0; p != null && i < 3; i++, p = p.parent)
            {
                path = p.name + "/" + path;
            }
            return $"{field.GetType().Name}({path})";
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
                DebugLog($"skip: bounds.type={bounds.type} iconId={bounds.iconId} field={Describe(field)}");
                return false;
            }
            return true;
        }

        private static void Apply(InputField field, string newText, int caret)
        {
            field.text = newText;
            field.caretPosition = caret;
            field.selectionAnchorPosition = caret;
            field.selectionFocusPosition = caret;
            field.ForceLabelUpdate();
        }

        private static void Finish(InputField field, TextTriggerBounds bounds, UpgradeResult result, int caret)
        {
            DebugLog($"{result}: caret={caret} bounds(index={bounds.index},iconId={bounds.iconId},type={bounds.type}) field={Describe(field)}");
            _lastFrame = Time.frameCount;
        }

        /// <summary>普通输入框（UIInputField 等）的 Shift 升级处理。</summary>
        public static void Handle(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (field == null || !CanProcess(field, e, bounds) || _lastFrame == Time.frameCount)
            {
                return;
            }
            var caret = field.caretPosition;
            var newText = UpgradeAtCaret(field.text, caret, out var newCaret, out var result);
            if (newText == null)
            {
                Finish(field, bounds, result, caret);
                return;
            }
            Apply(field, newText, newCaret);
            Finish(field, bounds, result, newCaret);
        }

        /// <summary>UISmartInputField 富文本编辑器：替换后还需重算着色。</summary>
        public static void HandleSmart(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (field == null || !CanProcess(field, e, bounds) || _lastFrame == Time.frameCount)
            {
                return;
            }
            var caret = field.caretPosition;
            var newText = UpgradeAtCaret(field.text, caret, out var newCaret, out var result);
            if (newText == null)
            {
                Finish(field, bounds, result, caret);
                return;
            }
            Apply(field, newText, newCaret);
            field.CalculateColorText();
            Finish(field, bounds, result, newCaret);
        }

        /// <summary>行星标记备注（UIMarkerDesc）：替换后同步写回标记存盘并刷新窗口布局。</summary>
        public static void HandleMarker(UIMarkerDesc marker, PointerEventData e, TextTriggerBounds bounds)
        {
            if (marker == null || marker.marker == null || marker.marker.id == 0)
            {
                return;
            }
            var field = marker.todoInputField;
            if (field == null || !CanProcess(field, e, bounds) || _lastFrame == Time.frameCount)
            {
                return;
            }
            var caret = field.caretPosition;
            var newText = UpgradeAtCaret(field.text, caret, out var newCaret, out var result);
            if (newText == null)
            {
                Finish(field, bounds, result, caret);
                return;
            }
            Apply(field, newText, newCaret);
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
            Finish(field, bounds, result, newCaret);
        }
    }
}
