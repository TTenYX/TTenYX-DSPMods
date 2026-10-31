using System;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.UI.Youthcat;

namespace IconFix
{
    /// <summary>
    /// 三个「图标选取」回调是文本码的唯一插入点（原版固定调用 IconTag(signalId) 不带名称）：
    ///   1. UIInputField.OnIconSelectSignalPickerReturn        — 蓝图介绍/星球名/站点名等普通输入框
    ///   2. UISmartInputField.OnIconSelectSignalPickerReturn   — 蓝图介绍等多行富文本编辑器
    ///   3. UIMarkerDesc.OnTodoIconSelectSignalPickerReturn    — 行星标记「备注」输入框
    /// 这里用 prefix 跳过原方法并按勾选框状态重写插入内容（其余逻辑原样保留）。
    /// 同时在 UISignalTagPicker._OnCreate 后注入勾选框 UI。
    /// </summary>
    internal static class IconPickerPatches
    {
        public static void Apply(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(IconPickerPatches), nameof(UIInputField_Return_Prefix));
            harmony.Patch(
                AccessTools.Method(typeof(UIInputField), "OnIconSelectSignalPickerReturn"),
                prefix: prefix);

            harmony.Patch(
                AccessTools.Method(typeof(UISmartInputField), "OnIconSelectSignalPickerReturn"),
                prefix: new HarmonyMethod(typeof(IconPickerPatches), nameof(UISmartInputField_Return_Prefix)));

            harmony.Patch(
                AccessTools.Method(typeof(UIMarkerDesc), "OnTodoIconSelectSignalPickerReturn"),
                prefix: new HarmonyMethod(typeof(IconPickerPatches), nameof(UIMarkerTodo_Return_Prefix)));

            harmony.Patch(
                AccessTools.Method(typeof(UISignalTagPicker), "_OnCreate"),
                postfix: new HarmonyMethod(typeof(IconPickerPatches), nameof(SignalTagPicker_OnCreate_Postfix)));

            // Shift+左键点击文本框中的纯图标 → 原地升级为带名称图标（原版 Ctrl 分支不受影响）
            harmony.Patch(
                AccessTools.Method(typeof(UIInputField), "OnTextTriggerClick"),
                postfix: new HarmonyMethod(typeof(IconPickerPatches), nameof(UIInputField_Trigger_Postfix)));
            harmony.Patch(
                AccessTools.Method(typeof(UISmartInputField), "OnTextTriggerClick"),
                postfix: new HarmonyMethod(typeof(IconPickerPatches), nameof(UISmartInputField_Trigger_Postfix)));
            harmony.Patch(
                AccessTools.Method(typeof(UIMarkerDesc), "OnTextTriggerClick"),
                postfix: new HarmonyMethod(typeof(IconPickerPatches), nameof(UIMarkerTodo_Trigger_Postfix)));
        }

        private static string IconTag(int signalId)
        {
            // 勾选框状态 + Shift 临时取反（点击回调与松键同帧，Input 状态即点击瞬间状态）
            return LDB.signals.IconTag(signalId, IconFixState.EffectiveIncludeName());
        }

        private static bool UIInputField_Return_Prefix(UIInputField __instance, int signalId)
        {
            // 原版条件：signalId != 0 && inputField.isFocused
            if (signalId == 0 || __instance.inputField == null || !__instance.inputField.isFocused)
            {
                return false;
            }
            __instance.inputField.InsertGeneralString(IconTag(signalId), true);
            return false;
        }

        private static bool UISmartInputField_Return_Prefix(UISmartInputField __instance, int signalId)
        {
            // 原版条件：signalId != 0
            if (signalId == 0 || __instance.inputField == null)
            {
                return false;
            }
            __instance.inputField.InsertGeneralString(IconTag(signalId), true);
            return false;
        }

        private static bool UIMarkerTodo_Return_Prefix(UIMarkerDesc __instance, int signalId)
        {
            // 原版条件：!_event_lock && marker != null && marker.id != 0 && signalId != 0
            if (signalId == 0 || __instance.marker == null || __instance.marker.id == 0)
            {
                return false;
            }
            var tr = Traverse.Create(__instance);
            if (tr.Field("_event_lock").GetValue<bool>())
            {
                return false;
            }
            var input = __instance.todoInputField;
            if (input == null)
            {
                return false;
            }
            input.InsertGeneralString(IconTag(signalId), true);
            if (__instance.factory != null)
            {
                __instance.factory.WriteMarkerComment(__instance.marker.id, input.text);
            }
            // 原版随后：AdaptiveWindowRect(advancedSettingsOpened, out _, out _); HandleTodoInputFieldHeight();
            try
            {
                object[] args =
                {
                    tr.Property("advancedSettingsOpened").GetValue<bool>(),
                    0,
                    0,
                    true
                };
                AccessTools.Method(typeof(UIMarkerDesc), "AdaptiveWindowRect").Invoke(__instance, args);
                tr.Method("HandleTodoInputFieldHeight").GetValue();
            }
            catch (Exception e)
            {
                IconFixPlugin.Log.LogWarning($"marker layout refresh failed: {e.Message}");
            }
            return false;
        }

        private static void SignalTagPicker_OnCreate_Postfix(UISignalTagPicker __instance)
        {
            try
            {
                PickerToggleUI.EnsureCreated(__instance);
            }
            catch (Exception e)
            {
                IconFixPlugin.Log.LogWarning($"toggle UI init failed: {e}");
            }
        }

        private static void UIInputField_Trigger_Postfix(UIInputField __instance, PointerEventData e, TextTriggerBounds bounds)
        {
            try
            {
                IconTextUpgrader.Handle(__instance.inputField, e, bounds);
            }
            catch (Exception ex)
            {
                IconFixPlugin.Log.LogWarning($"icon upgrade failed: {ex.Message}");
            }
        }

        private static void UISmartInputField_Trigger_Postfix(UISmartInputField __instance, PointerEventData e, TextTriggerBounds bounds)
        {
            try
            {
                IconTextUpgrader.HandleSmart(__instance.inputField, e, bounds);
            }
            catch (Exception ex)
            {
                IconFixPlugin.Log.LogWarning($"icon upgrade failed: {ex.Message}");
            }
        }

        private static void UIMarkerTodo_Trigger_Postfix(UIMarkerDesc __instance, PointerEventData e, TextTriggerBounds bounds)
        {
            try
            {
                IconTextUpgrader.HandleMarker(__instance, e, bounds);
            }
            catch (Exception ex)
            {
                IconFixPlugin.Log.LogWarning($"icon upgrade failed: {ex.Message}");
            }
        }
    }
}
