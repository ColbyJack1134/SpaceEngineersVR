using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using VRage.ModAPI;

namespace SpaceEngineersVR.Multiplayer
{
    [HarmonyPatch(typeof(MyModStorageComponent),nameof(MyModStorageComponent.Serialize))]
    internal static class CockpitStoragePatch
    {
        private static void Prefix(MyModStorageComponent __instance,HashSet<Guid> ___m_cachedGuids)
        {
            // Plugin-owned storage has no world-mod definition to claim its serialization key.
            if(__instance.TryGetValue(CockpitMemory.Key,out _)) ___m_cachedGuids.Add(CockpitMemory.Key);
        }
    }
    [HarmonyPatch(typeof(MyObjectBuilder_CubeBlock),nameof(MyObjectBuilder_CubeBlock.Remap))]
    internal static class CockpitRemapPatch
    {
        private static void Postfix(MyObjectBuilder_CubeBlock __instance,IMyRemapHelper remapHelper)
            => CockpitMemory.Remap(__instance,remapHelper);
    }
    [HarmonyPatch]
    internal static class AnalogSavePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Sandbox.Game.Entities.Blocks.MyPistonBase),"GetObjectBuilderCubeBlock");
            yield return AccessTools.Method(typeof(Sandbox.Game.Entities.Cube.MyMotorStator),"GetObjectBuilderCubeBlock");
        }
        private static void Postfix(Sandbox.Game.Entities.MyCubeBlock __instance,MyObjectBuilder_CubeBlock __result)
            => AnalogControl.SavedVelocity(__instance.EntityId,__result);
    }
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.UpdateBeforeSimulation))]
    internal static class RemoteArmsRestorePatch
    {
        private static void Prefix(MyCharacter __instance) => MultiplayerRuntime.Restore(__instance);
    }
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.UpdateAfterSimulation))]
    internal static class RemoteArmsUpdatePatch
    {
        private static void Postfix(MyCharacter __instance) => MultiplayerRuntime.Animate(__instance);
    }
}
