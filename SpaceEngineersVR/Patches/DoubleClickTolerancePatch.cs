using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SpaceEngineersVR.Patches
{
    // Native lists and grids drop a double click when the cursor moves more than 0.005 GUI units between the
    // presses, which a hand-held pointer routinely exceeds. The double click acts on the item under the second
    // press, so the radius stays well below one row.
    internal static class DoubleClickTolerancePatch
    {
        internal const float Native=.005f,Tolerance=.015f;
        private static bool installed;
        internal static IEnumerable<MethodBase> Targets =>
            new[] {"MyGuiControlGrid","MyGuiControlListbox","MyGuiControlTable","MyGuiControlMultiSelectTable","MyGuiControlResearchGraph"}
                .Select(name => AccessTools.DeclaredMethod(AccessTools.TypeByName("Sandbox.Graphics.GUI."+name),"HandleNewMousePress"));
        // Patching runs these controls' class constructors, which need the game's renderer, so install after startup.
        internal static void Install(Harmony harmony)
        {
            if(installed) return;
            installed=true;
            foreach(var method in Targets) harmony.Patch(method,transpiler:new HarmonyMethod(typeof(DoubleClickTolerancePatch),nameof(Transpiler)));
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var code=instructions.ToList();
            var matches=code.Where(c => c.opcode==OpCodes.Ldc_R4 && c.operand is float value && value==Native).ToArray();
            if(matches.Length!=1)
                throw new InvalidOperationException("Unrecognized native double-click tolerance in "+__originalMethod.DeclaringType.FullName);
            matches[0].operand=Tolerance;
            return code;
        }
    }
}
