using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyShipController),"HandleBuildingMode")]
    internal static class CockpitBuildingPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int keys=0,controls=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(!(instruction.operand is MethodInfo method)) continue;
                string helper=null;
                if(method.Name=="IsNewKeyPressed") { keys++; helper=nameof(CockpitBuilding.Shortcut); }
                if(method.Name=="IsAnyCtrlKeyPressed") { controls++; helper=nameof(CockpitBuilding.Shortcut); }
                if(helper==null) continue;
                // Supply a local Ctrl+G request without changing the native mode/toolbar lifecycle.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CockpitBuilding),helper));
            }
            if(keys!=1 || controls!=1) throw new InvalidOperationException("Native cockpit building shortcut changed");
        }
    }

    [HarmonyPatch(typeof(MyCockpit),"OnControlReleased")]
    internal static class CockpitBuildingExitPatch
    {
        private static void Prefix(MyCockpit __instance,MyEntityController controller)
        {
            if(Main.VrActive && controller.Player==MySession.Static?.LocalHumanPlayer)
                CockpitBuilding.Exit(__instance);
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder),"HandleAdminAndCreativeInput")]
    internal static class CockpitBuildDistancePatch
    {
        internal static MyShipController DistanceOwner(MyShipController owner) => CockpitBuilding.Active ? null:owner;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.opcode!=OpCodes.Isinst || !Equals(instruction.operand,typeof(MyShipController))) continue;
                count++;
                // Retain the surrounding Creative permission checks and native distance bounds.
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CockpitBuildDistancePatch),nameof(DistanceOwner)));
            }
            if(count!=1) throw new InvalidOperationException("Native cockpit distance exclusion changed");
        }
    }
}
