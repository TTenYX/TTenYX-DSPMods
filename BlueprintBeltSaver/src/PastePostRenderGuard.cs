using System;
using HarmonyLib;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// Vanilla safety guard.
    ///
    /// <c>BuildTool_BlueprintPaste.OnCameraPostRender</c> dereferences
    /// <c>PlayerAction_Build.model</c> whenever <c>bpCursor &gt; 0</c>, but the guard in front of it
    /// only checks the planet and the preview count. The player build model can legitimately be
    /// null (space flight, scene transitions, or a blueprint pushed into the tool outside the paste
    /// flow), and the resulting NullReferenceException aborts the frame, disables auto-saving and
    /// shows the game's runtime-error banner - exactly the IL offset 0x4d (the <c>model</c> field
    /// load) reported in Player.log.
    ///
    /// Skipping the draw call in that state is inert: the callback only draws entity signs for the
    /// paste preview, and it already returns early when entity signs are off or the game is paused.
    /// </summary>
    internal static class PastePostRenderGuard
    {
        private static bool _warned;

        internal static bool Prefix(BuildTool_BlueprintPaste __instance)
        {
            try
            {
                if (__instance.bpCursor <= 0)
                {
                    return true;
                }
                PlayerAction_Build actionBuild = __instance.actionBuild;
                if (actionBuild != null && actionBuild.model != null)
                {
                    return true;
                }
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("skipped BuildTool_BlueprintPaste.OnCameraPostRender because the player build model is not ready; this prevents the vanilla NullReferenceException (further occurrences are silent).");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        internal static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(BuildTool_BlueprintPaste), "OnCameraPostRender");
                if (target == null)
                {
                    Log.Warn("BuildTool_BlueprintPaste.OnCameraPostRender not found; the crash guard is not installed.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(PastePostRenderGuard), nameof(Prefix))));
                Log.Info("crash guard installed: BuildTool_BlueprintPaste.OnCameraPostRender");
            }
            catch (Exception ex)
            {
                Log.Error("installing the crash guard failed: " + ex);
            }
        }
    }
}
