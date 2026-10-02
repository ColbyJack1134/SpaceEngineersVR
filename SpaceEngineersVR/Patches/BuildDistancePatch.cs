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
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
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
            if(count!=2) throw new InvalidOperationException("Native build distance factors changed");
        }
    }
}
