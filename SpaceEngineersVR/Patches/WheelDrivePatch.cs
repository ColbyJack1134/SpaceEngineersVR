using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    // Filters only the controller's own wheel writes, leaving autopilot and scripts untouched.
    [HarmonyPatch]
    internal static class WheelDrivePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MyShipController),nameof(MyShipController.MoveAndRotate),Type.EmptyTypes);
            yield return AccessTools.Method(typeof(MyShipController),nameof(MyShipController.ClearMovementControl));
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter=AccessTools.PropertySetter(typeof(MyGridWheelSystem),nameof(MyGridWheelSystem.AngularVelocity));
            int count=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(setter))
                {
                    count++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(DriveInput),nameof(DriveInput.Wheels)));
                }
                yield return instruction;
            }
            if(count!=1) throw new InvalidOperationException("Native ship wheel input changed");
        }
    }
}
