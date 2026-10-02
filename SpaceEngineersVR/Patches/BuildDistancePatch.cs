using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyCubeBuilder),"HandleBlockCreationMovement")]
    internal static class BuildDistancePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => DistanceFactors(instructions,2,"build");
        internal static IEnumerable<CodeInstruction> DistanceFactors(IEnumerable<CodeInstruction> instructions,int expected,string context)
        {
            int count=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.opcode==OpCodes.Ldc_R4 && instruction.operand is float value && value==1.1f)
                {
                    count++;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PlacementControls),nameof(PlacementControls.NativeDistanceFactor)));
                }
            }
            // Retain the native distance bounds and Survival reach checks around both updates.
            if(count!=expected) throw new InvalidOperationException("Native "+context+" distance factors changed");
        }
    }
    [HarmonyPatch]
    internal static class BlueprintDistancePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var type=typeof(Sandbox.Game.Entities.Cube.MyGridClipboard);
            yield return AccessTools.Method(type,"MoveEntityFurther");
            yield return AccessTools.Method(type,"MoveEntityCloser");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => BuildDistancePatch.DistanceFactors(instructions,1,"blueprint");
    }
}
