using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ControlPanelPlus
{
    /// <summary>
    /// Shortens the focus-pulse animation of control panel entries. The vanilla implementation runs
    /// for one second (<c>focusTime &lt; 1f</c>, incremented by Time.deltaTime); when walking the list
    /// with the arrow keys that feels sluggish, so the delta used for the pulse is scaled by 5 -
    /// the same curve plays in 20% of the time.
    /// </summary>
    [HarmonyPatch(typeof(UIControlPanelObjectEntry), "_OnUpdate")]
    internal static class EntryFocusSpeedPatch
    {
        private const float SpeedUp = 5f;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction instruction = list[i];
                if (instruction.opcode != System.Reflection.Emit.OpCodes.Call) continue;
                if (!(instruction.operand is MethodInfo method)) continue;
                if (method.DeclaringType != typeof(UnityEngine.Time) || method.Name != "get_deltaTime") continue;

                list.Insert(i + 1, new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_R4, SpeedUp));
                list.Insert(i + 2, new CodeInstruction(System.Reflection.Emit.OpCodes.Mul));
                return list;
            }
            CppLog.Warn("focus animation speed patch: Time.deltaTime call not found, using vanilla duration");
            return instructions;
        }
    }
}
