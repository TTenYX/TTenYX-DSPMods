using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[assembly: InternalsVisibleTo("IconFix.SelfTest")]

namespace IconFix
{
    /// <summary>
    /// 图标选取增强：在「图标选取」面板头部新增勾选框「附带名称」（默认开启）。
    /// 开启时，点选图标会同时插入「图标 + 名称」文本码（\meik-;）；关闭时保持原版行为，仅插入图标（\meik;）。
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class IconFixPlugin : BaseUnityPlugin
    {
        public const string Guid = "TTenYX.DSP.IconFix";
        public const string Name = "IconFix";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> IncludeName;
        internal static ConfigEntry<bool> ShiftClickInsertName;
        internal static IconFixPlugin Instance { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            IncludeName = Config.Bind(
                "UI",
                "IncludeNameWhenPick",
                false,
                "Default state of the picker checkbox. When on, picking an icon also inserts its name text, e.g. \\meik-; (icon + name) instead of \\meik; (icon only). Changes in game are saved automatically.\n" +
                "图标选取勾选框的默认状态。开启时，选取图标会同时插入名称文本，如 \\meik-;（图标+名称）；关闭则仅插入图标 \\meik;。游戏内的改动会自动保存。");
            ShiftClickInsertName = Config.Bind(
                "UI",
                "ShiftClickInsertName",
                true,
                "When on, Shift+LeftClick on an icon temporarily inverts the checkbox state: with the checkbox off, Shift+Click inserts the named tag \\meik-;; with the checkbox on, it inserts the icon-only tag \\meik;.\n" +
                "开启后，Shift+左键点击图标会临时取反勾选框状态：未勾选时按住 Shift 点击插入带名称的 \\meik-;；已勾选时按住 Shift 点击则插入纯图标 \\meik;。");
            _harmony = new Harmony(Guid);
            try
            {
                IconPickerPatches.Apply(_harmony);
            }
            catch (Exception e)
            {
                Log.LogError($"Patch failed: {e}");
            }
            Log.LogInfo($"{Name} {Version} loaded. includeName(default) = {IncludeName.Value}");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>勾选框最终状态计算（供补丁与自测共用）。</summary>
    internal static class IconFixState
    {
        /// <summary>Shift+点击时临时取反勾选框状态。</summary>
        public static bool ApplyShift(bool baseValue, bool shiftHeld)
        {
            if (!shiftHeld || IconFixPlugin.ShiftClickInsertName == null || !IconFixPlugin.ShiftClickInsertName.Value)
            {
                return baseValue;
            }
            return !baseValue;
        }

        /// <summary>按当前键盘 Shift 状态计算本次插入是否附带名称。</summary>
        public static bool EffectiveIncludeName()
        {
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return ApplyShift(IconFixPlugin.IncludeName.Value, shift);
        }
    }

    internal static class IconFixText
    {
        /// <summary>按游戏语言取文案：中文游戏返回中文，其余返回英文。</summary>
        public static string L(string zh, string en)
        {
            try
            {
                return Localization.isZHCN ? zh : en;
            }
            catch
            {
                return en;
            }
        }

        public static string ToggleLabel => L("图标附带名称", "Icon With Name");

        public static string ToggleTipTitle => L("图标附带名称", "Icon With Name");

        public static string ToggleTipText =>
            L("开启时，点选图标会同时插入名称文本（\\meik-;，图标+名称）；关闭则仅插入图标（\\meik;）。\\n按住 Shift 点击图标可临时取反本选项（可在配置文件中关闭）。",
              "When on, clicking an icon also inserts its name text (\\meik-;, icon + name); when off, only the icon (\\meik;) is inserted.\\nHold Shift while clicking an icon to temporarily invert this option (can be disabled in the config file).");
    }
}
