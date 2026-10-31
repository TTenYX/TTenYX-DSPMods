using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.Youthcat;

namespace IconFix
{
    /// <summary>
    /// 1) 游戏的 TextEventTrigger.enableIconHover 默认 false 且无任何代码打开它，
    ///    导致绝大多数输入框点击文本内图标时 onTriggerClick 不触发；
    /// 2) 游戏自己的 onTriggerClick 订阅只覆盖 UIInputField / UISmartInputField / UIMarkerDesc
    ///    三种包装，且实测仍有断点（物流塔标题、蓝图说明/作者等无效）。
    /// 这里周期扫描场景中的 TextEventTrigger：对挂在可编辑输入框上的实例打开图标 hover，
    /// 并直接在所在 Text 上订阅 onTriggerClick 原地完成 Shift 升级（与游戏订阅并行，
    /// 同帧门控保证只执行一次）。事件闭包捕获各自关联的 InputField。
    /// </summary>
    internal static class IconHoverEnabler
    {
        private const float ScanInterval = 1f;
        private static float _nextScan;
        private static readonly ConditionalWeakTable<Text, object> Hooked = new ConditionalWeakTable<Text, object>();

        public static void Tick()
        {
            if (Time.unscaledTime < _nextScan)
            {
                return;
            }
            _nextScan = Time.unscaledTime + ScanInterval;
            try
            {
                var triggers = Resources.FindObjectsOfTypeAll<TextEventTrigger>();
                for (var i = 0; i < triggers.Length; i++)
                {
                    var t = triggers[i];
                    if (t == null)
                    {
                        continue;
                    }
                    var field = t.inputField != null ? t.inputField : t.GetComponentInParent<InputField>(true);
                    if (field == null)
                    {
                        continue;
                    }
                    t.enableIconHover = true;

                    var text = t.GetComponent<Text>();
                    if (text != null && !Hooked.TryGetValue(text, out _))
                    {
                        Hooked.Add(text, null);
                        text.onTriggerClick += (e, bounds) => RawDispatch(field, e, bounds);
                    }
                }
            }
            catch
            {
                // 扫描期间对象被销毁等瞬时异常直接跳过本轮
            }
        }

        /// <summary>按输入框包装类型分派到对应处理（marker 落盘 / 富文本着色 / 普通）。</summary>
        private static void RawDispatch(InputField field, PointerEventData e, TextTriggerBounds bounds)
        {
            if (field == null)
            {
                return;
            }
            var markerDesc = field.GetComponentInParent<UIMarkerDesc>(true);
            if (markerDesc != null && markerDesc.todoInputField == field)
            {
                IconTextUpgrader.HandleMarker(markerDesc, e, bounds);
                return;
            }
            if (field.GetComponentInParent<UISmartInputField>(true) != null)
            {
                IconTextUpgrader.HandleSmart(field, e, bounds);
                return;
            }
            IconTextUpgrader.Handle(field, e, bounds);
        }
    }
}
