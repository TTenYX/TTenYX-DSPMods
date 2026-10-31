using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.Youthcat;

namespace IconFix
{
    /// <summary>
    /// 游戏的 TextEventTrigger.enableIconHover 默认 false 且无任何代码打开它，
    /// 导致绝大多数输入框点击文本内图标时 hoveringIconBounds 恒为空、onTriggerClick 不触发
    /// （原版 Ctrl+查看详情与我们的 Shift+升级都不生效，只有个别预制体上手动勾选的输入框可用）。
    /// 这里周期扫描场景中的 TextEventTrigger，为挂在可编辑输入框上的实例打开图标 hover 检测。
    /// 打开后的附带效果：悬停图标会触发原版的文字重绘（高亮），与原版备注框行为一致。
    /// </summary>
    internal static class IconHoverEnabler
    {
        private const float ScanInterval = 1f;
        private static float _nextScan;

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
                    if (t == null || t.enableIconHover)
                    {
                        continue;
                    }
                    // 只对可编辑输入框开启（TextEventTrigger 挂在 Text 上，InputField 在父链）
                    if (t.inputField == null && t.GetComponentInParent<InputField>() == null)
                    {
                        continue;
                    }
                    t.enableIconHover = true;
                }
            }
            catch
            {
                // 扫描期间对象被销毁等瞬时异常直接跳过本轮
            }
        }
    }
}
